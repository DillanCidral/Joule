//! Registers the bundled Geist SemiBold (the font of the Joule name, SIL OFL 1.1) with fontconfig for
//! this process only, so no system font install is needed.

use std::{
    ffi::CString,
    os::raw::{c_char, c_int, c_void},
    path::PathBuf,
};

#[link(name = "fontconfig")]
unsafe extern "C" {
    fn FcConfigAppFontAddFile(config: *mut c_void, file: *const c_char) -> c_int;
}

static GEIST: &[u8] = include_bytes!("../../../../../src/BatteryTray/Fonts/Geist-SemiBold.ttf");

fn cache_file() -> PathBuf {
    let base = std::env::var_os("XDG_CACHE_HOME")
        .map(PathBuf::from)
        .filter(|p| p.is_absolute())
        .or_else(|| std::env::var_os("HOME").map(|h| PathBuf::from(h).join(".cache")))
        .unwrap_or_else(std::env::temp_dir);
    base.join("joule").join("Geist-SemiBold.ttf")
}

/// Call before the first widget is created. Returns false when the font could not be registered
/// (the name then shows in the default UI font).
pub fn register() -> bool {
    let path = cache_file();
    if let Some(dir) = path.parent() {
        let _ = std::fs::create_dir_all(dir);
    }
    if std::fs::read(&path).map(|b| b != GEIST).unwrap_or(true)
        && std::fs::write(&path, GEIST).is_err()
    {
        return false;
    }
    let Some(c) = path.to_str().and_then(|s| CString::new(s).ok()) else {
        return false;
    };
    // SAFETY: `c` is a valid NUL-terminated path that outlives the call; a null config means the current one.
    unsafe { FcConfigAppFontAddFile(std::ptr::null_mut(), c.as_ptr()) != 0 }
}

#[cfg(test)]
mod tests {
    #[test]
    fn the_embedded_font_is_a_truetype_file() {
        assert_eq!(&super::GEIST[..4], &[0, 1, 0, 0], "TrueType magic");
        assert!(super::GEIST.len() > 10_000);
    }
}
