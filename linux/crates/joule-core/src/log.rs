use crate::paths;
use std::{
    fmt::Display,
    fs::OpenOptions,
    io::Write,
    sync::atomic::{AtomicBool, Ordering},
};

static MUTED: AtomicBool = AtomicBool::new(false);

/// Stops all log writing for this process. Tests call it so they never touch the user's real log.
pub fn mute() {
    MUTED.store(true, Ordering::Relaxed);
}

/// Append one line to `joule.log`. Silent when the file cannot be written.
pub fn write(msg: &str) {
    if cfg!(test) || MUTED.load(Ordering::Relaxed) {
        return;
    }
    let dir = paths::data_dir();
    let _ = std::fs::create_dir_all(&dir);
    if let Ok(mut f) = OpenOptions::new()
        .create(true)
        .append(true)
        .open(paths::log_file(&dir))
    {
        let _ = writeln!(f, "{} {msg}", timestamp());
    }
}

pub fn error(ctx: &str, err: &dyn Display) {
    write(&format!("{ctx}: {err}"));
}

fn timestamp() -> String {
    let secs = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs() as libc::time_t)
        .unwrap_or(0);
    // SAFETY: `tm` is a plain C struct that localtime_r fully writes; both pointers are valid for the call.
    let tm = unsafe {
        let mut tm: libc::tm = std::mem::zeroed();
        libc::localtime_r(&secs, &mut tm);
        tm
    };
    format!(
        "{:04}-{:02}-{:02} {:02}:{:02}:{:02}",
        tm.tm_year + 1900,
        tm.tm_mon + 1,
        tm.tm_mday,
        tm.tm_hour,
        tm.tm_min,
        tm.tm_sec
    )
}
