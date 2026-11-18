//! Starting other programs: plainly, elevated for one command (UAC), or fully detached.

use std::os::windows::process::CommandExt;
use std::path::Path;
use std::process::Command;

use windows::core::{HSTRING, PCWSTR};
use windows::Win32::Foundation::CloseHandle;
use windows::Win32::System::Threading::{GetExitCodeProcess, WaitForSingleObject, INFINITE};
use windows::Win32::UI::Shell::{ShellExecuteExW, SEE_MASK_NOASYNC, SEE_MASK_NOCLOSEPROCESS, SHELLEXECUTEINFOW};
use windows::Win32::UI::WindowsAndMessaging::SW_HIDE;

const CREATE_NO_WINDOW: u32 = 0x0800_0000;
const DETACHED_PROCESS: u32 = 0x0000_0008;

/// Runs one program elevated (one UAC prompt) and waits for it.
/// `Ok(None)` = the user declined the prompt; `Ok(Some(code))` = the program's exit code.
pub fn run_elevated(exe: &Path, args: &str) -> windows::core::Result<Option<u32>> {
    let file = HSTRING::from(exe.as_os_str());
    let params = HSTRING::from(args);
    let verb = HSTRING::from("runas");
    let mut info = SHELLEXECUTEINFOW {
        cbSize: std::mem::size_of::<SHELLEXECUTEINFOW>() as u32,
        fMask: SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC,
        lpVerb: PCWSTR(verb.as_ptr()),
        lpFile: PCWSTR(file.as_ptr()),
        lpParameters: PCWSTR(params.as_ptr()),
        nShow: SW_HIDE.0,
        ..Default::default()
    };
    unsafe {
        if ShellExecuteExW(&mut info).is_err() {
            // ERROR_CANCELLED (1223): the user said no.
            return Ok(None);
        }
        let mut code = 0u32;
        if !info.hProcess.is_invalid() {
            WaitForSingleObject(info.hProcess, INFINITE);
            let _ = GetExitCodeProcess(info.hProcess, &mut code);
            let _ = CloseHandle(info.hProcess);
        }
        Ok(Some(code))
    }
}

/// Runs a program hidden and waits; returns its exit code.
pub fn run_hidden(exe: &Path, args: &[&str]) -> std::io::Result<i32> {
    let status = Command::new(exe).args(args).creation_flags(CREATE_NO_WINDOW).status()?;
    Ok(status.code().unwrap_or(-1))
}

/// Starts a program and does not wait (the "Open" buttons on the last screen).
pub fn open(exe: &Path, args: &[&str]) -> std::io::Result<()> {
    Command::new(exe).args(args).spawn().map(|_| ())
}

/// A hidden cmd.exe that outlives this process: used to remove the install folder,
/// which holds the running uninstaller itself.
pub fn detached_cmd(command_line: &str) -> std::io::Result<()> {
    Command::new("cmd.exe")
        .raw_arg(format!("/C {command_line}"))
        .creation_flags(CREATE_NO_WINDOW | DETACHED_PROCESS)
        .spawn()
        .map(|_| ())
}
