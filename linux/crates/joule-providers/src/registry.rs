//! Pluggable provider registry. Adding hardware means writing a provider and listing it in
//! [`Registry::create_default`]; nothing else in the app needs to know it exists.
//! Providers never write to a device on their own: the only write is the battery question through
//! `joule_hid::request`.

use joule_core::DeviceEntry;
use joule_hid::{HidCollection, gate::Gate};
use std::sync::Arc;

/// A provider that finds and reads its own devices from the HID collections of this reading.
pub trait StandaloneProvider: Send + Sync {
    /// Technical name (shows in the log).
    fn name(&self) -> &str;
    /// This provider already lists this VID:PID, so the scan creates no "unsupported" row for it.
    fn handles(&self, vid: u16, pid: u16) -> bool;
    fn discover(&self, gate: &Arc<Gate>, collections: &[HidCollection]) -> Vec<DeviceEntry>;
}

pub struct Registry {
    standalone: Vec<Box<dyn StandaloneProvider>>,
}

impl Registry {
    pub fn new(standalone: Vec<Box<dyn StandaloneProvider>>) -> Self {
        Self { standalone }
    }

    pub fn create_default() -> Self {
        Self::new(vec![
            // The kernel first (DillanCidral/Joule#174): it only reads sysfs.
            Box::new(crate::kernel_power_supply::KernelProvider),
            Box::new(crate::logitech_headset::LogitechHeadsetProvider),
            Box::new(crate::logitech_receiver::LogitechReceiverProvider::default()),
        ])
    }

    pub fn standalone(&self) -> &[Box<dyn StandaloneProvider>] {
        &self.standalone
    }

    pub fn handles(&self, vid: u16, pid: u16) -> bool {
        self.standalone.iter().any(|p| p.handles(vid, pid))
    }
}
