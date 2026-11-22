mod hotplug;
mod ui;
mod watch;

use joule_core::{ReadingStatus, l, paths, settings::Settings};
use joule_providers::{registry::Registry, service::ReadingService};

fn main() {
    let dir = paths::data_dir();
    let mut settings = Settings::load_from(&dir);
    l::set_language(&settings.language);
    let args: Vec<String> = std::env::args().skip(1).collect();

    match args.first().map(String::as_str) {
        Some("allow") | Some("deny") => {
            let Some(vid) = args.get(1).map(|v| v.to_uppercase()) else {
                eprintln!("usage: joule allow|deny <VID hex>");
                std::process::exit(2)
            };
            if u16::from_str_radix(&vid, 16).is_err() {
                eprintln!("not a hex VID: {vid}");
                std::process::exit(2)
            }
            settings
                .ask_allowed_vids
                .retain(|v| !v.eq_ignore_ascii_case(&vid));
            if args[0] == "allow" {
                settings.ask_allowed_vids.push(vid.clone());
            }
            settings.save_to(&dir);
            println!("{} {vid}", args[0]);
        }
        Some("--udev-rules") => print!("{}", joule_providers::udev::rules()),
        Some("--selftest") => std::process::exit(selftest(settings, dir)),
        Some("--collections") => {
            // Read-only diagnostic: sysfs only, nothing is sent to any device.
            for c in joule_hid::inventory::scan() {
                println!(
                    "{}  {}  {:04X}/{:04X}  in={} out={} feat={}  {}",
                    c.path.display(),
                    c.hardware_id(),
                    c.usage_page,
                    c.usage,
                    c.input_len,
                    c.output_len,
                    c.feature_len,
                    c.product
                );
            }
        }
        Some("--panel") | Some("--hidden") => {
            std::process::exit(ui::run(args[0] == "--hidden"));
        }
        Some("--watch") => {
            watch::run(ReadingService::new(
                Registry::create_default(),
                settings,
                dir,
            ));
        }
        other => {
            let mut svc = ReadingService::new(Registry::create_default(), settings, dir);
            let result = svc.refresh();
            if other == Some("--json") {
                println!("{}", serde_json::to_string_pretty(&result.devices).unwrap());
            } else {
                let all = args.iter().any(|a| a == "--all");
                let mut hidden = 0;
                for e in result.devices.iter().filter(|e| e.is_connected) {
                    if !all && e.reading.status == ReadingStatus::Unsupported {
                        hidden += 1;
                    } else {
                        println!("{}", watch::line(e));
                    }
                }
                if hidden > 0 {
                    println!(
                        "({hidden} other HID devices without support; joule --all lists them)"
                    );
                }
                if result.devices.is_empty() {
                    println!("no HID devices found");
                }
            }
            for a in svc.low_battery_alerts(&result) {
                eprintln!("low battery: {} {}", a.name, a.value);
            }
        }
    }
}

/// Checks of the security rules that do not need a device (SECURITY.md): with "listen only" and with no
/// consent, nothing may reach any device. Runs a real reading, which only reads sysfs when nothing is
/// allowed. Exit code 0 = all fine.
fn selftest(settings: Settings, dir: std::path::PathBuf) -> i32 {
    let mut failed = 0;
    let mut check = |name: &str, ok: bool| {
        println!("{} {name}", if ok { "ok  " } else { "FAIL" });
        if !ok {
            failed += 1;
        }
    };

    let mut listen = settings.clone();
    listen.listen_only = true;
    let mut svc = ReadingService::new(Registry::create_default(), listen, dir.clone());
    svc.refresh();
    check(
        "listen only: nothing sent to any device",
        svc.gate().sent() == 0,
    );

    let mut nobody = settings.clone();
    nobody.listen_only = false;
    nobody.ask_allowed_vids.clear();
    let mut svc = ReadingService::new(Registry::create_default(), nobody, dir);
    svc.refresh();
    check(
        "no consent: nothing sent to any device",
        svc.gate().sent() == 0,
    );

    let collections = joule_hid::inventory::scan();
    check(
        "inventory never lists a system mouse or keyboard as askable",
        collections
            .iter()
            .filter(|c| joule_hid::gate::can_ask(c))
            .all(|c| !joule_hid::inventory::is_system_reserved(c.usage_page, c.usage)),
    );
    check(
        "at most 48 questions per reading",
        joule_hid::gate::MAX_ASKS == 48,
    );
    println!(
        "{}",
        if failed == 0 {
            "selftest passed"
        } else {
            "selftest FAILED"
        }
    );
    i32::from(failed != 0)
}
