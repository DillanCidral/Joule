//! Source-level checks of the SECURITY.md rules for the Linux port: no network code, and the
//! only write to a device is in `joule-hid/src/request.rs`.

use std::{
    fs,
    path::{Path, PathBuf},
};

fn rust_files(dir: &Path, out: &mut Vec<PathBuf>) {
    for e in fs::read_dir(dir).unwrap().flatten() {
        let p = e.path();
        if p.is_dir() {
            if p.file_name().is_some_and(|n| n != "target" && n != "tests") {
                rust_files(&p, out);
            }
        } else if p.extension().is_some_and(|x| x == "rs") {
            out.push(p);
        }
    }
}

fn sources() -> Vec<(String, String)> {
    let crates = Path::new(env!("CARGO_MANIFEST_DIR")).join("..");
    let mut files = Vec::new();
    rust_files(&crates, &mut files);
    files
        .into_iter()
        .map(|p| {
            (
                p.strip_prefix(&crates)
                    .unwrap()
                    .to_string_lossy()
                    .into_owned(),
                fs::read_to_string(&p).unwrap(),
            )
        })
        .collect()
}

/// The part of a file before its unit tests.
fn production(src: &str) -> &str {
    src.split("#[cfg(test)]\nmod tests").next().unwrap_or(src)
}

#[test]
fn no_network_code_in_the_linux_crates() {
    for (name, src) in sources() {
        for banned in [
            "TcpStream",
            "UdpSocket",
            "reqwest",
            "ureq",
            "hyper::",
            "std::net::",
        ] {
            assert!(!production(&src).contains(banned), "{name} uses {banned}");
        }
    }
}

#[test]
fn only_the_request_door_writes_to_a_device() {
    // Files that write their own data (log, settings, cache, autostart, font cache, a PNG): never a device.
    let data_writers = [
        "joule-core/src/log.rs",
        "joule-core/src/paths.rs",
        "joule/src/ui/autostart.rs",
        "joule/src/ui/fonts.rs",
    ];
    for (name, src) in sources() {
        let prod = production(&src);
        let writes = prod.contains(".write_all(")
            || prod.contains("libc::write(")
            || prod.contains("OpenOptions");
        if name.ends_with("joule-hid/src/request.rs") {
            assert!(prod.contains(".write_all("), "the write door must exist");
            continue;
        }
        if data_writers.iter().any(|w| name.ends_with(w)) {
            continue;
        }
        assert!(!writes, "{name} writes outside the request door");
    }
}

#[test]
fn the_request_door_checks_the_gate_before_writing() {
    let (_, src) = sources()
        .into_iter()
        .find(|(n, _)| n.ends_with("joule-hid/src/request.rs"))
        .unwrap();
    let ask = &src[src.find("pub fn ask(").unwrap()..];
    let permit = ask.find("self.gate.permit(").unwrap();
    let write = ask.find(".write_all(").unwrap();
    assert!(permit < write, "permit() must come before write_all()");
    assert!(
        ask.find("self.gate.count_ask()").unwrap() < write,
        "the cap is counted before writing"
    );
}
