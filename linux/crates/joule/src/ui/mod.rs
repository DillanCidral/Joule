//! GTK4 / libadwaita panel, tray and notifications.

mod about_view;
mod autostart;
mod bar;
mod fonts;
mod icons;
mod kit;
mod panel_view;
mod pill;
mod ring;
mod settings_view;
mod sound;
mod submission;
mod theme;
mod tray;
mod widget_view;
mod window;

pub use window::run;

/// The name shown to the user (`AppInfo.Name`).
pub const APP_NAME: &str = "Joule";
