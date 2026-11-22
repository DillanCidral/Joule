pub mod descriptor;
pub mod gate;
pub mod inventory;
pub mod request;

use std::path::PathBuf;

/// One top-level HID collection on a hidraw node.
#[derive(Clone, Debug)]
pub struct HidCollection {
    pub path: PathBuf,
    pub vid: u16,
    pub pid: u16,
    pub usage_page: u16,
    pub usage: u16,
    /// Report lengths include the report id byte, like Windows `HIDP_CAPS`. 0 = none of that kind.
    pub input_len: usize,
    pub output_len: usize,
    pub feature_len: usize,
    pub product: String,
}

impl HidCollection {
    pub fn hardware_id(&self) -> String {
        format!("{:04X}:{:04X}", self.vid, self.pid)
    }
}
