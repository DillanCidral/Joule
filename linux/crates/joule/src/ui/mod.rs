//! The GNOME app: a libadwaita window with the Joule rings, settings and about dialogs, the top bar
//! item and notifications.

mod about;
mod autostart;
mod fonts;
mod icons;
mod pill;
mod prefs;
mod ring;
mod rows;
mod sound;
mod style;
mod submission;
mod theme;
mod tray;
mod window;

pub use window::run;

/// The name shown to the user (`AppInfo.Name`).
pub const APP_NAME: &str = "Joule";
