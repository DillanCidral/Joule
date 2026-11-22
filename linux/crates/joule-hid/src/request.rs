//! The single write door of the project: one battery question at a time, waiting for the answer.

use crate::{
    HidCollection,
    gate::{Gate, can_ask},
};
use std::{
    fs::{File, OpenOptions},
    io::{Read, Write},
    os::{fd::AsRawFd, unix::fs::OpenOptionsExt},
    path::PathBuf,
    sync::Arc,
    time::{Duration, Instant},
};

const MAX_DRAIN: usize = 64;

pub struct HidRequest {
    gate: Arc<Gate>,
    files: Vec<(PathBuf, File)>,
    asks: usize,
}

impl HidRequest {
    /// Opens each askable collection's node once (read+write, non-blocking).
    pub fn open(gate: Arc<Gate>, listen_on: &[HidCollection]) -> Option<Self> {
        let mut files: Vec<(PathBuf, File)> = Vec::new();
        for c in listen_on.iter().filter(|c| can_ask(c)) {
            if files.iter().any(|(p, _)| *p == c.path) {
                continue;
            }
            match OpenOptions::new()
                .read(true)
                .write(true)
                .custom_flags(libc::O_NONBLOCK)
                .open(&c.path)
            {
                Ok(f) => files.push((c.path.clone(), f)),
                Err(e) => joule_core::log::error(&format!("open {}", c.path.display()), &e),
            }
        }
        if files.is_empty() {
            None
        } else {
            Some(Self {
                gate,
                files,
                asks: 0,
            })
        }
    }

    #[cfg(test)]
    pub(crate) fn with_files(gate: Arc<Gate>, files: Vec<(PathBuf, File)>) -> Self {
        Self {
            gate,
            files,
            asks: 0,
        }
    }

    pub fn asks(&self) -> usize {
        self.asks
    }

    /// Writes `request` (report id in byte 0, zero padded to the output report length) and waits for a
    /// reply that `is_reply` accepts. `None` = refused, no reply, or device gone.
    pub fn ask(
        &mut self,
        target: &HidCollection,
        request: &[u8],
        is_reply: impl Fn(&[u8]) -> bool,
        timeout: Duration,
    ) -> Option<Vec<u8>> {
        if !can_ask(target) || target.output_len < request.len() || !self.gate.permit(target) {
            return None;
        }
        if !self.gate.count_ask() {
            return None;
        }
        self.asks += 1;

        self.drain(); // an old reply is not an answer to this question

        let mut buf = vec![0u8; target.output_len];
        buf[..request.len()].copy_from_slice(request);
        self.gate.note_sent();
        let file = &mut self.files.iter_mut().find(|(p, _)| *p == target.path)?.1;
        if let Err(e) = file.write_all(&buf) {
            joule_core::log::error(&format!("write {}", target.path.display()), &e);
            return None;
        }

        let deadline = Instant::now() + timeout;
        loop {
            let left = deadline.checked_duration_since(Instant::now())?;
            let reply = self.wait_reply(left)?;
            if is_reply(&reply) {
                return Some(reply);
            }
        }
    }

    /// Drops reports queued before this question. Bounded: another program on the same node
    /// (for example RGB software) can keep the queue full forever.
    fn drain(&mut self) {
        let mut buf = [0u8; 256];
        for (_, f) in &mut self.files {
            for _ in 0..MAX_DRAIN {
                if !matches!(f.read(&mut buf), Ok(n) if n > 0) {
                    break;
                }
            }
        }
    }

    /// poll() every node and read one report from whichever is ready.
    fn wait_reply(&mut self, timeout: Duration) -> Option<Vec<u8>> {
        let mut fds: Vec<libc::pollfd> = self
            .files
            .iter()
            .map(|(_, f)| libc::pollfd {
                fd: f.as_raw_fd(),
                events: libc::POLLIN,
                revents: 0,
            })
            .collect();
        let ms = timeout.as_millis().min(i32::MAX as u128) as i32;
        // SAFETY: `fds` is a valid, exclusively borrowed slice of pollfd for the whole call.
        let n = unsafe { libc::poll(fds.as_mut_ptr(), fds.len() as libc::nfds_t, ms) };
        if n <= 0 {
            return None;
        }
        for (i, p) in fds.iter().enumerate() {
            if p.revents & libc::POLLIN != 0 {
                let mut buf = [0u8; 256];
                if let Ok(len) = self.files[i].1.read(&mut buf)
                    && len > 0
                {
                    return Some(buf[..len].to_vec());
                }
            }
            if p.revents & (libc::POLLERR | libc::POLLHUP | libc::POLLNVAL) != 0 {
                return None; // device gone: stop touching it
            }
        }
        None
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::{
        os::{fd::OwnedFd, unix::net::UnixDatagram},
        thread,
    };

    fn col(output_len: usize) -> HidCollection {
        HidCollection {
            path: PathBuf::from("/dev/fake"),
            vid: 0x046D,
            pid: 0x0A5B,
            usage_page: 0xFF43,
            usage: 0x0202,
            input_len: 20,
            output_len,
            feature_len: 0,
            product: "G933".into(),
        }
    }

    fn session(gate: Arc<Gate>) -> (HidRequest, UnixDatagram) {
        joule_core::log::mute();
        let (host, device) = UnixDatagram::pair().unwrap();
        host.set_nonblocking(true).unwrap();
        let file = File::from(OwnedFd::from(host));
        (
            HidRequest::with_files(gate, vec![(PathBuf::from("/dev/fake"), file)]),
            device,
        )
    }

    #[test]
    fn ask_pads_request_and_returns_reply() {
        joule_core::log::mute();
        let gate = Arc::new(Gate::new());
        gate.new_reading(false, &["046D".into()]);
        let (mut req, device) = session(gate.clone());
        let t = thread::spawn(move || {
            let mut buf = [0u8; 64];
            let n = device.recv(&mut buf).unwrap();
            assert_eq!(n, 20, "request must be padded to the output report length");
            assert_eq!(&buf[..4], &[0x11, 0xFF, 0x08, 0x0A]);
            let mut reply = vec![0x11, 0xFF, 0x08, 0x0A, 0x0D, 0xA0, 0x01];
            reply.resize(20, 0);
            device.send(&reply).unwrap();
        });
        let reply = req.ask(
            &col(20),
            &[0x11, 0xFF, 0x08, 0x0A],
            |r| r.len() >= 7 && r[0] == 0x11,
            Duration::from_millis(500),
        );
        t.join().unwrap();
        assert_eq!(reply.unwrap()[4..6], [0x0D, 0xA0]);
        assert_eq!(gate.sent(), 1);
        assert_eq!(req.asks(), 1);
    }

    #[test]
    fn refused_without_consent_sends_nothing() {
        joule_core::log::mute();
        let gate = Arc::new(Gate::new());
        gate.new_reading(false, &[]);
        let (mut req, device) = session(gate.clone());
        assert!(
            req.ask(
                &col(20),
                &[0x11, 0xFF, 0x08, 0x0A],
                |_| true,
                Duration::from_millis(50)
            )
            .is_none()
        );
        device.set_nonblocking(true).unwrap();
        assert!(
            device.recv(&mut [0u8; 64]).is_err(),
            "nothing may reach the device"
        );
        assert_eq!(gate.sent(), 0);
    }

    #[test]
    fn drain_is_bounded_under_a_report_flood() {
        joule_core::log::mute();
        let gate = Arc::new(Gate::new());
        gate.new_reading(false, &["046D".into()]);
        let (mut req, device) = session(gate);
        device.set_nonblocking(true).unwrap(); // a full buffer must not block the flooder
        let flooder = thread::spawn(move || {
            let end = Instant::now() + Duration::from_millis(400);
            while Instant::now() < end {
                let _ = device.send(&[0x11, 0xFF, 0xFF, 0x04, 0x3A, 0x05, 0, 0]);
            }
        });
        let t0 = Instant::now();
        let r = req.ask(
            &col(20),
            &[0x11, 0xFF, 0x08, 0x0A],
            |_| false,
            Duration::from_millis(100),
        );
        assert!(r.is_none());
        assert!(
            t0.elapsed() < Duration::from_millis(300),
            "ask must honour its timeout under a flood"
        );
        flooder.join().unwrap();
    }

    #[test]
    fn timeout_returns_none() {
        joule_core::log::mute();
        let gate = Arc::new(Gate::new());
        gate.new_reading(false, &["046D".into()]);
        let (mut req, _device) = session(gate);
        assert!(
            req.ask(
                &col(20),
                &[0x11, 0xFF, 0x08, 0x0A],
                |_| true,
                Duration::from_millis(50)
            )
            .is_none()
        );
    }

    #[test]
    fn request_longer_than_report_is_refused() {
        joule_core::log::mute();
        let gate = Arc::new(Gate::new());
        gate.new_reading(false, &["046D".into()]);
        let (mut req, _d) = session(gate.clone());
        assert!(
            req.ask(
                &col(3),
                &[0x11, 0xFF, 0x08, 0x0A],
                |_| true,
                Duration::from_millis(50)
            )
            .is_none()
        );
        assert_eq!(gate.sent(), 0);
    }
}
