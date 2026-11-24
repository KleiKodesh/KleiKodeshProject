//! The Kitvei Hakodesh Windows service (`KitveiHakodeshSvc`, LocalSystem, demand start).
//!
//! The installer never elevates itself; only the two service operations that need admin
//! run elevated, each as ONE prompt:
//!   * register: `KitveiHakodeshService.exe --install` (also retires the legacy
//!     DocumentLocatorSvc in the same run);
//!   * remove:   `sc stop` + `sc delete`.
//! If the user declines, the apps still work: they start the service themselves.

use std::io::{Read, Write};
use std::time::Duration;

use windows::core::HSTRING;
use windows::Win32::System::Pipes::WaitNamedPipeW;

use crate::layout::{Layout, SERVICE_EXE};
use crate::system::{launch, processes, registry};

const SERVICE_NAME: &str = "KitveiHakodeshSvc";
const LEGACY_SERVICE_NAME: &str = "DocumentLocatorSvc";
const PIPE: &str = r"\\.\pipe\KitveiHakodesh";

fn service_key(name: &str) -> String {
    format!(r"SYSTEM\CurrentControlSet\Services\{name}")
}

/// Asks the running service to exit so its files can be replaced, then ends any
/// leftover console copy that is ours to end. The SYSTEM service itself exits on the
/// pipe request; a process we may not end is simply left alone.
pub fn stop(layout: &Layout) {
    if layout.sandboxed {
        return;
    }
    send_op("shutdown");
    let exe_name = SERVICE_EXE;
    if !processes::wait_until_gone(exe_name, Duration::from_secs(5)) {
        for pid in processes::find(exe_name) {
            let in_build_output = processes::image_path(pid).is_some_and(|p| p.to_lowercase().contains(r"\bin\"));
            if !in_build_output {
                processes::kill(pid);
            }
        }
        processes::wait_until_gone(exe_name, Duration::from_secs(3));
    }
}

/// Registers the service when it is missing, points elsewhere, is disabled, or the
/// legacy service is still there. Returns false when the user declined the prompt.
pub fn ensure_registered(layout: &Layout, force: bool) -> bool {
    if layout.sandboxed {
        return true;
    }
    let exe = layout.service_exe();
    if !exe.exists() {
        return true;
    }
    let current = registry::hklm_str(&service_key(SERVICE_NAME), "ImagePath")
        .is_some_and(|path| path.to_lowercase().contains(&exe.display().to_string().to_lowercase()));
    let disabled = registry::hklm_dword(&service_key(SERVICE_NAME), "Start") == Some(4);
    let legacy = registry::hklm_exists(&service_key(LEGACY_SERVICE_NAME));
    if current && !disabled && !legacy && !force {
        return true;
    }
    matches!(launch::run_elevated(&exe, "--install"), Ok(Some(0)))
}

/// Stops and deletes both services (ours and the legacy one), one prompt for both.
pub fn remove(layout: &Layout) {
    if layout.sandboxed {
        return;
    }
    let ours = registry::hklm_exists(&service_key(SERVICE_NAME));
    let legacy = registry::hklm_exists(&service_key(LEGACY_SERVICE_NAME));
    if !ours && !legacy {
        return;
    }
    let cmd = std::path::Path::new(r"C:\Windows\System32\cmd.exe");
    let script = format!(
        "/C sc stop {SERVICE_NAME} & sc stop {LEGACY_SERVICE_NAME} & ping -n 4 127.0.0.1 >nul & sc delete {SERVICE_NAME} & sc delete {LEGACY_SERVICE_NAME}"
    );
    let _ = launch::run_elevated(cmd, &script);
}

/// One request on the service pipe: `{Op: name}` in the service's MessagePack envelope,
/// framed as [int32 LE length][payload]. Built by hand: a 1-entry map (0x81), fixstr "Op",
/// fixstr value. Any failure means the service is not running - nothing to do.
fn send_op(op: &str) {
    unsafe {
        if !WaitNamedPipeW(&HSTRING::from(PIPE), 1000).as_bool() {
            return;
        }
    }
    let Ok(mut pipe) = std::fs::OpenOptions::new().read(true).write(true).open(PIPE) else { return };
    let mut message = vec![0x81, 0xA2, b'O', b'p', 0xA0 | op.len() as u8];
    message.extend_from_slice(op.as_bytes());
    let mut frame = (message.len() as i32).to_le_bytes().to_vec();
    frame.extend_from_slice(&message);
    if pipe.write_all(&frame).is_ok() {
        let mut reply = [0u8; 64];
        let _ = pipe.read(&mut reply); // reply, or EOF once the service acted
    }
}
