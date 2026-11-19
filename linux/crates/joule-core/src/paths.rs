use std::{
    fs, io,
    path::{Path, PathBuf},
};

pub fn data_dir_from(xdg: Option<&str>, home: Option<&str>) -> PathBuf {
    match (xdg.filter(|s| !s.is_empty()), home) {
        (Some(x), _) => PathBuf::from(x).join("joule"),
        (None, Some(h)) => PathBuf::from(h).join(".local/share/joule"),
        (None, None) => PathBuf::from("/tmp/joule"),
    }
}

pub fn data_dir() -> PathBuf {
    data_dir_from(
        std::env::var("XDG_DATA_HOME").ok().as_deref(),
        std::env::var("HOME").ok().as_deref(),
    )
}

pub fn settings_file(dir: &Path) -> PathBuf {
    dir.join("settings.json")
}
pub fn state_file(dir: &Path) -> PathBuf {
    dir.join("state.json")
}
pub fn log_file(dir: &Path) -> PathBuf {
    dir.join("joule.log")
}

/// Write to a temp file in the same directory, then rename.
pub fn write_atomic(path: &Path, contents: &str) -> io::Result<()> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }
    let tmp = path.with_extension("tmp");
    fs::write(&tmp, contents)?;
    fs::rename(&tmp, path)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn xdg_wins() {
        assert_eq!(
            data_dir_from(Some("/x"), Some("/h")),
            PathBuf::from("/x/joule")
        );
    }
    #[test]
    fn falls_back_to_home() {
        assert_eq!(
            data_dir_from(None, Some("/h")),
            PathBuf::from("/h/.local/share/joule")
        );
        assert_eq!(
            data_dir_from(Some(""), Some("/h")),
            PathBuf::from("/h/.local/share/joule")
        );
    }
    #[test]
    fn atomic_write_creates_dirs() {
        let d = tempfile::tempdir().unwrap();
        let p = d.path().join("a/b/s.json");
        write_atomic(&p, "{}").unwrap();
        assert_eq!(fs::read_to_string(p).unwrap(), "{}");
    }
}
