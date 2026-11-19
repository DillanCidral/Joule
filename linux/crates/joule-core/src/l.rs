use std::sync::RwLock;

static FORCE: RwLock<Option<bool>> = RwLock::new(None); // Some(true) = pt

/// "auto" follows `LANG`, "pt" and "en" force a language.
pub fn set_language(setting: &str) {
    *FORCE.write().unwrap() = match setting {
        "pt" => Some(true),
        "en" => Some(false),
        _ => None,
    };
}

pub fn t(pt: &str, en: &str) -> String {
    let use_pt = FORCE.read().unwrap().unwrap_or_else(|| {
        std::env::var("LANG")
            .map(|v| v.starts_with("pt"))
            .unwrap_or(false)
    });
    if use_pt { pt } else { en }.to_string()
}

/// Tests that set or depend on the language hold this lock: the language is process-wide.
#[cfg(test)]
pub(crate) static TEST_LOCK: std::sync::Mutex<()> = std::sync::Mutex::new(());

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn forced_language_wins() {
        let _g = TEST_LOCK.lock().unwrap_or_else(|e| e.into_inner());
        set_language("pt");
        assert_eq!(t("oi", "hi"), "oi");
        set_language("en");
        assert_eq!(t("oi", "hi"), "hi");
        set_language("auto");
    }
}
