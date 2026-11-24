//! Windows .lnk shortcuts through the shell's own IShellLinkW.

use std::path::Path;

use windows::core::{Interface, HSTRING};
use windows::Win32::System::Com::{
    CoCreateInstance, CoInitializeEx, IPersistFile, CLSCTX_INPROC_SERVER, COINIT_APARTMENTTHREADED,
};
use windows::Win32::UI::Shell::{IShellLinkW, ShellLink};

pub struct Shortcut<'a> {
    pub target: &'a Path,
    pub arguments: &'a str,
    pub working_dir: &'a Path,
    pub icon: &'a Path,
    pub description: &'a str,
}

/// Creates or overwrites `path`. Overwriting on every install keeps target and icon current.
pub fn create(path: &Path, shortcut: &Shortcut) -> windows::core::Result<()> {
    if let Some(dir) = path.parent() {
        let _ = std::fs::create_dir_all(dir);
    }
    unsafe {
        // Already initialised on this thread is fine (S_FALSE / RPC_E_CHANGED_MODE).
        let _ = CoInitializeEx(None, COINIT_APARTMENTTHREADED);
        let link: IShellLinkW = CoCreateInstance(&ShellLink, None, CLSCTX_INPROC_SERVER)?;
        link.SetPath(&HSTRING::from(shortcut.target.as_os_str()))?;
        link.SetArguments(&HSTRING::from(shortcut.arguments))?;
        link.SetWorkingDirectory(&HSTRING::from(shortcut.working_dir.as_os_str()))?;
        link.SetIconLocation(&HSTRING::from(shortcut.icon.as_os_str()), 0)?;
        link.SetDescription(&HSTRING::from(shortcut.description))?;
        link.cast::<IPersistFile>()?.Save(&HSTRING::from(path.as_os_str()), true)?;
    }
    Ok(())
}
