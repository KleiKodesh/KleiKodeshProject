//! Downloading a file with Windows' own URL Monikers (no HTTP library needed).

use std::path::Path;

use windows::core::HSTRING;
use windows::Win32::System::Com::Urlmon::URLDownloadToFileW;

pub fn to_file(url: &str, destination: &Path) -> windows::core::Result<()> {
    if let Some(dir) = destination.parent() {
        let _ = std::fs::create_dir_all(dir);
    }
    unsafe { URLDownloadToFileW(None, &HSTRING::from(url), &HSTRING::from(destination.as_os_str()), 0, None) }
}
