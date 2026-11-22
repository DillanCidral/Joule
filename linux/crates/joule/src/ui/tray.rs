//! Top bar item (StatusNotifierItem through ksni) with the drop-down menu. GNOME shows it only with the
//! AppIndicator extension; without a watcher the spawn fails and the app works as a plain window.
//! The icon is Joule's pill (see `pill.rs`); the menu lists every device with its value, like the
//! small widget of the Windows app.

use super::pill::{self, PillState};
use ksni::{
    Icon, MenuItem, ToolTip, Tray,
    blocking::{Handle, TrayMethods},
    menu::{CheckmarkItem, StandardItem},
};

/// What the menu asks the window side to do.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Command {
    /// Left click on the item: open or close the panel.
    Toggle,
    Show,
    Refresh,
    ToggleListenOnly,
    Quit,
}

/// One device line of the menu.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MenuDevice {
    pub icon: String,
    pub label: String,
}

pub struct JouleTray {
    pub tx: async_channel::Sender<Command>,
    pub state: PillState,
    pub devices: Vec<MenuDevice>,
    pub listen_only: bool,
    /// "Last reading at 11:30", or empty before the first reading.
    pub last_reading: String,
}

impl Tray for JouleTray {
    fn id(&self) -> String {
        "io.github.dillancidral.Joule".into()
    }
    fn title(&self) -> String {
        "Joule".into()
    }
    // Empty name: hosts then draw the pixmaps below.
    fn icon_name(&self) -> String {
        String::new()
    }
    fn icon_pixmap(&self) -> Vec<Icon> {
        [22usize, 32, 48, 64]
            .into_iter()
            .map(|s| Icon {
                width: s as i32,
                height: s as i32,
                data: pill::render(self.state, s),
            })
            .collect()
    }
    fn tool_tip(&self) -> ToolTip {
        use joule_core::l::t;
        ToolTip {
            title: "Joule".into(),
            description: if self.devices.is_empty() {
                t("nenhum aparelho com bateria", "no device with battery")
            } else {
                self.devices
                    .iter()
                    .map(|d| d.label.as_str())
                    .collect::<Vec<_>>()
                    .join("\n")
            },
            ..Default::default()
        }
    }
    // Left click opens the panel (the SNI "Activate" call).
    fn activate(&mut self, _x: i32, _y: i32) {
        let _ = self.tx.send_blocking(Command::Toggle);
    }
    fn menu(&self) -> Vec<MenuItem<Self>> {
        use joule_core::l::t;
        let send = |cmd: Command| -> Box<dyn Fn(&mut Self) + Send + Sync> {
            Box::new(move |this: &mut Self| {
                let _ = this.tx.send_blocking(cmd);
            })
        };
        let mut items: Vec<MenuItem<Self>> = Vec::new();
        items.push(
            StandardItem {
                label: if self.last_reading.is_empty() {
                    "Joule".into()
                } else {
                    format!("Joule · {}", self.last_reading)
                },
                enabled: false,
                ..Default::default()
            }
            .into(),
        );
        if self.devices.is_empty() {
            items.push(
                StandardItem {
                    label: t("Nenhum aparelho com bateria", "No device with battery"),
                    enabled: false,
                    ..Default::default()
                }
                .into(),
            );
        }
        for d in &self.devices {
            items.push(
                StandardItem {
                    label: d.label.clone(),
                    icon_name: d.icon.clone(),
                    activate: send(Command::Show),
                    ..Default::default()
                }
                .into(),
            );
        }
        items.push(MenuItem::Separator);
        items.push(
            StandardItem {
                label: t("Abrir painel", "Open panel"),
                icon_name: "view-reveal-symbolic".into(),
                activate: send(Command::Show),
                ..Default::default()
            }
            .into(),
        );
        items.push(
            StandardItem {
                label: t("Atualizar", "Refresh"),
                icon_name: "view-refresh-symbolic".into(),
                activate: send(Command::Refresh),
                ..Default::default()
            }
            .into(),
        );
        items.push(
            CheckmarkItem {
                label: t("Só escutar", "Listen only"),
                checked: self.listen_only,
                activate: send(Command::ToggleListenOnly),
                ..Default::default()
            }
            .into(),
        );
        items.push(MenuItem::Separator);
        items.push(
            StandardItem {
                label: t("Sair", "Quit"),
                icon_name: "application-exit-symbolic".into(),
                activate: send(Command::Quit),
                ..Default::default()
            }
            .into(),
        );
        items
    }
}

/// Starts the tray. `None` when no StatusNotifierWatcher is running.
pub fn spawn(tx: async_channel::Sender<Command>, listen_only: bool) -> Option<Handle<JouleTray>> {
    let tray = JouleTray {
        tx,
        state: PillState::Reading,
        devices: Vec::new(),
        listen_only,
        last_reading: String::new(),
    };
    match tray.spawn() {
        Ok(h) => Some(h),
        Err(e) => {
            joule_core::log::error("tray", &e);
            None
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::Mutex;

    /// The language is process-wide: tests that set it take turns.
    static LANG: Mutex<()> = Mutex::new(());

    fn tray(devices: Vec<MenuDevice>) -> JouleTray {
        let (tx, _rx) = async_channel::unbounded();
        JouleTray {
            tx,
            state: PillState::Ok,
            devices,
            listen_only: false,
            last_reading: "21:45".into(),
        }
    }
    fn labels(t: &JouleTray) -> Vec<String> {
        t.menu()
            .iter()
            .map(|i| match i {
                MenuItem::Standard(s) => s.label.clone(),
                MenuItem::Checkmark(c) => {
                    format!("[{}] {}", if c.checked { "x" } else { " " }, c.label)
                }
                MenuItem::Separator => "---".into(),
                _ => "?".into(),
            })
            .collect()
    }

    #[test]
    fn menu_lists_devices_then_actions() {
        let _g = LANG.lock().unwrap_or_else(|e| e.into_inner());
        joule_core::l::set_language("en");
        let t = tray(vec![MenuDevice {
            icon: "input-mouse-symbolic".into(),
            label: "G703 39%".into(),
        }]);
        assert_eq!(
            labels(&t),
            [
                "Joule · 21:45",
                "G703 39%",
                "---",
                "Open panel",
                "Refresh",
                "[ ] Listen only",
                "---",
                "Quit"
            ]
        );
        joule_core::l::set_language("auto");
    }
    #[test]
    fn menu_without_devices_says_so() {
        let _g = LANG.lock().unwrap_or_else(|e| e.into_inner());
        joule_core::l::set_language("en");
        let l = labels(&tray(vec![]));
        assert_eq!(l[1], "No device with battery");
        joule_core::l::set_language("auto");
    }
    #[test]
    fn icon_comes_as_pixmaps_only() {
        let t = tray(vec![]);
        assert_eq!(t.icon_name(), "");
        let sizes: Vec<i32> = t.icon_pixmap().iter().map(|i| i.width).collect();
        assert_eq!(sizes, [22, 32, 48, 64]);
        assert!(
            t.icon_pixmap()
                .iter()
                .all(|i| i.data.len() == (i.width * i.height * 4) as usize)
        );
    }
}
