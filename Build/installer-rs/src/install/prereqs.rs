//! What the products need from Windows, and getting it where we can.
//!
//!  * .NET Framework 4.8  - the add-in and the app. Built into Windows 10 1903+ / 11;
//!                          on older systems we point to the download page.
//!  * VSTO runtime        - the add-in. Ships with current Office; otherwise we point to
//!                          Microsoft's download page (it has no stable direct link).
//!  * WebView2 runtime    - the add-in and the app. Built into Windows 11; otherwise the
//!                          Evergreen bootstrapper installs it silently, per user.

use std::path::Path;

use crate::system::{download, launch, registry};

pub const DOTNET_PAGE: &str = "https://dotnet.microsoft.com/download/dotnet-framework/net48";
pub const VSTO_PAGE: &str = "https://www.microsoft.com/download/details.aspx?id=105890";
const WEBVIEW2_BOOTSTRAPPER: &str = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
const WEBVIEW2_CLIENT: &str = r"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
const DOTNET_48_RELEASE: u32 = 528_040;

pub fn dotnet48_installed() -> bool {
    registry::hklm_dword(r"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release")
        .is_some_and(|release| release >= DOTNET_48_RELEASE)
}

pub fn vsto_runtime_installed() -> bool {
    let by_registry = [r"SOFTWARE\Microsoft\VSTO Runtime Setup\v4R", r"SOFTWARE\Microsoft\VSTO Runtime Setup\v4"]
        .iter()
        .any(|key| registry::hklm_str(key, "Version").is_some_and(|v| !v.is_empty()));
    let windir = std::env::var("WINDIR").unwrap_or_else(|_| r"C:\Windows".into());
    by_registry || Path::new(&windir).join(r"Microsoft.NET\assembly\GAC_MSIL\Microsoft.Office.Tools.v4.0.Framework").exists()
}

pub fn webview2_installed() -> bool {
    let usable = |v: Option<String>| v.is_some_and(|v| !v.is_empty() && v != "0.0.0.0");
    usable(registry::hklm_str(WEBVIEW2_CLIENT, "pv")) || usable(registry::hkcu_real_str(WEBVIEW2_CLIENT, "pv"))
}

/// Downloads and runs the WebView2 bootstrapper silently (per user, no prompt).
pub fn install_webview2(download_dir: &Path) -> Result<(), String> {
    let setup = download_dir.join("MicrosoftEdgeWebview2Setup.exe");
    download::to_file(WEBVIEW2_BOOTSTRAPPER, &setup).map_err(|e| e.to_string())?;
    let code = launch::run_hidden(&setup, &["/silent", "/install"]).map_err(|e| e.to_string())?;
    let _ = std::fs::remove_file(&setup);
    if code == 0 || webview2_installed() { Ok(()) } else { Err(format!("WebView2 setup exit code {code}")) }
}

pub fn open_page(url: &str) {
    let _ = launch::open(Path::new("explorer.exe"), &[url]);
}
