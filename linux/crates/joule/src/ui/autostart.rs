//! Start at login: an XDG autostart entry (`~/.config/autostart`), the Linux counterpart of the
//! Windows "Run" key of `Core/Autostart.cs`. Starts in the top bar only (`--hidden`).

use std::path::PathBuf;

const FILE: &str = "io.github.dillancidral.Joule.desktop";

fn path() -> PathBuf {
    let base = std::env::var_os("XDG_CONFIG_HOME")
        .map(PathBuf::from)
        .filter(|p| p.is_absolute())
        .or_else(|| std::env::var_os("HOME").map(|h| PathBuf::from(h).join(".config")))
        .unwrap_or_else(|| PathBuf::from(".config"));
    base.join("autostart").join(FILE)
}

/// The desktop entry text for a given executable.
pub fn entry(exec: &str) -> String {
    format!(
        "[Desktop Entry]\nType=Application\nName=Joule\nComment=Battery levels of wireless devices\nExec={exec} --hidden\nIcon=battery-full-symbolic\nTerminal=false\nX-GNOME-Autostart-enabled=true\nX-GNOME-Autostart-Delay=5\n"
    )
}

pub fn is_enabled() -> bool {
    path().exists()
}

pub fn set(on: bool) {
    let p = path();
    if on {
        let exe = std::env::current_exe()
            .ok()
            .and_then(|e| e.to_str().map(String::from))
            .unwrap_or_else(|| "joule".into());
        if let Some(dir) = p.parent() {
            let _ = std::fs::create_dir_all(dir);
        }
        if let Err(e) = std::fs::write(&p, entry(&exe)) {
            joule_core::log::error("autostart", &e);
        }
    } else {
        let _ = std::fs::remove_file(&p);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn entry_starts_hidden() {
        let e = entry("/home/u/.local/bin/joule");
        assert!(e.contains("Exec=/home/u/.local/bin/joule --hidden"));
        assert!(e.starts_with("[Desktop Entry]"));
    }
}
