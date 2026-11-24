//! An offline page opened as an Edge app window (`msedge --app=...`): its own window, no
//! browser bar. Edge ships with every supported Windows, so there is nothing to install.
//! The typing tutor and the calendar app both run this way.

use std::path::{Path, PathBuf};

use crate::system::registry;

const EDGE_APP_PATH: &str = r"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe";

pub fn edge_exe() -> Option<PathBuf> {
    let found = registry::hklm_str(EDGE_APP_PATH, "")
        .or_else(|| registry::hkcu_real_str(EDGE_APP_PATH, ""))
        .map(PathBuf::from)?;
    found.exists().then_some(found)
}

/// The page's file URL, as Edge's `--app=` takes it.
pub fn page_url(page: &Path) -> String {
    format!("file:///{}", page.display().to_string().replace('\\', "/"))
}

/// The arguments that open the page as an app window (quoted, for a shortcut).
pub fn arguments(page: &Path) -> String {
    format!("--app=\"{}\"", page_url(page))
}
