//! Thin wrappers over Windows: registry, shortcuts, processes, launching, downloads.
//! No product knowledge lives here.

pub mod download;
pub mod launch;
pub mod processes;
pub mod registry;
pub mod shortcut;

/// Tells Explorer that file associations changed, so "Open with" updates at once.
pub fn notify_associations_changed() {
    use windows::Win32::UI::Shell::{SHChangeNotify, SHCNE_ASSOCCHANGED, SHCNF_IDLIST};
    unsafe { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, None, None) };
}
