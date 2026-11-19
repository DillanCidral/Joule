//! Hardware arrival and removal from kernel uevents (netlink, no extra library).
//! Only `hidraw` nodes count. The event says nothing about the device: the reading service
//! describes it from sysfs afterwards, and `turns` waits for it to settle (udev still has to give
//! the user access to the node).

use std::{
    os::fd::{AsRawFd, FromRawFd, OwnedFd},
    thread,
};

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct HidrawEvent {
    pub arrived: bool,
    /// Device node, for example `/dev/hidraw5`.
    pub path: String,
}

/// Parses one kernel uevent datagram: `add@/devices/...\0KEY=VALUE\0...`.
pub fn parse_uevent(msg: &[u8]) -> Option<HidrawEvent> {
    let mut action = None;
    let mut subsystem = None;
    let mut devname = None;
    for field in msg.split(|b| *b == 0) {
        let Ok(field) = std::str::from_utf8(field) else {
            continue;
        };
        if let Some(v) = field.strip_prefix("ACTION=") {
            action = Some(v);
        } else if let Some(v) = field.strip_prefix("SUBSYSTEM=") {
            subsystem = Some(v);
        } else if let Some(v) = field.strip_prefix("DEVNAME=") {
            devname = Some(v);
        }
    }
    if subsystem? != "hidraw" {
        return None;
    }
    let arrived = match action? {
        "add" => true,
        "remove" => false,
        _ => return None,
    };
    let name = devname?;
    if !name.starts_with("hidraw") || name.contains('/') {
        return None;
    }
    Some(HidrawEvent {
        arrived,
        path: format!("/dev/{name}"),
    })
}

/// Starts a thread that hands every hidraw event to `on_event` (return false to stop).
/// Returns an error when the socket cannot be opened.
pub fn spawn(
    mut on_event: impl FnMut(HidrawEvent) -> bool + Send + 'static,
) -> std::io::Result<()> {
    // SAFETY: plain syscalls with checked results; the fd is owned by `OwnedFd` right after creation.
    let fd = unsafe {
        let raw = libc::socket(
            libc::AF_NETLINK,
            libc::SOCK_DGRAM | libc::SOCK_CLOEXEC,
            libc::NETLINK_KOBJECT_UEVENT,
        );
        if raw < 0 {
            return Err(std::io::Error::last_os_error());
        }
        OwnedFd::from_raw_fd(raw)
    };
    // SAFETY: zeroed sockaddr_nl is a valid value; fields are set before the bind call.
    let rc = unsafe {
        let mut addr: libc::sockaddr_nl = std::mem::zeroed();
        addr.nl_family = libc::AF_NETLINK as u16;
        addr.nl_groups = 1; // kernel events
        libc::bind(
            fd.as_raw_fd(),
            &addr as *const libc::sockaddr_nl as *const libc::sockaddr,
            std::mem::size_of::<libc::sockaddr_nl>() as u32,
        )
    };
    if rc < 0 {
        return Err(std::io::Error::last_os_error());
    }
    thread::Builder::new()
        .name("hotplug".into())
        .spawn(move || {
            let mut buf = [0u8; 8192];
            loop {
                // SAFETY: `buf` is valid for `buf.len()` bytes for the whole call.
                let n =
                    unsafe { libc::recv(fd.as_raw_fd(), buf.as_mut_ptr().cast(), buf.len(), 0) };
                if n < 0 {
                    if std::io::Error::last_os_error().kind() == std::io::ErrorKind::Interrupted {
                        continue;
                    }
                    return;
                }
                if let Some(ev) = parse_uevent(&buf[..n as usize])
                    && !on_event(ev)
                {
                    return;
                }
            }
        })?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn msg(parts: &[&str]) -> Vec<u8> {
        let mut v = Vec::new();
        for p in parts {
            v.extend_from_slice(p.as_bytes());
            v.push(0);
        }
        v
    }

    #[test]
    fn add_of_a_hidraw_node() {
        let m = msg(&[
            "add@/devices/pci0000:00/0000:00:14.0/usb1/1-3/1-3:1.2/0003:046D:0A5B.0007/hidraw/hidraw5",
            "ACTION=add",
            "DEVPATH=/devices/x/hidraw/hidraw5",
            "SUBSYSTEM=hidraw",
            "MAJOR=240",
            "DEVNAME=hidraw5",
        ]);
        assert_eq!(
            parse_uevent(&m),
            Some(HidrawEvent {
                arrived: true,
                path: "/dev/hidraw5".into()
            })
        );
    }
    #[test]
    fn remove_of_a_hidraw_node() {
        let m = msg(&[
            "remove@/x",
            "ACTION=remove",
            "SUBSYSTEM=hidraw",
            "DEVNAME=hidraw5",
        ]);
        assert_eq!(
            parse_uevent(&m),
            Some(HidrawEvent {
                arrived: false,
                path: "/dev/hidraw5".into()
            })
        );
    }
    #[test]
    fn other_subsystems_and_actions_are_ignored() {
        assert_eq!(
            parse_uevent(&msg(&[
                "add@/x",
                "ACTION=add",
                "SUBSYSTEM=usb",
                "DEVNAME=bus/usb/001/005"
            ])),
            None
        );
        assert_eq!(
            parse_uevent(&msg(&[
                "bind@/x",
                "ACTION=bind",
                "SUBSYSTEM=hidraw",
                "DEVNAME=hidraw5"
            ])),
            None
        );
        assert_eq!(
            parse_uevent(&msg(&[
                "change@/x",
                "ACTION=change",
                "SUBSYSTEM=hidraw",
                "DEVNAME=hidraw5"
            ])),
            None
        );
    }
    #[test]
    fn a_devname_that_is_not_a_hidraw_node_is_refused() {
        assert_eq!(
            parse_uevent(&msg(&[
                "add@/x",
                "ACTION=add",
                "SUBSYSTEM=hidraw",
                "DEVNAME=../etc/passwd"
            ])),
            None
        );
        assert_eq!(
            parse_uevent(&msg(&["add@/x", "ACTION=add", "SUBSYSTEM=hidraw"])),
            None
        );
    }
    #[test]
    fn garbage_is_ignored() {
        assert_eq!(parse_uevent(&[0xFF, 0xFE, 0, 1, 2]), None);
        assert_eq!(parse_uevent(&[]), None);
    }
}
