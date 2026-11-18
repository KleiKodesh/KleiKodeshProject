//! Where everything goes: folders, file names, registry roots.
//!
//! The install is per-user and its folder is fixed (`%LOCALAPPDATA%\KleiKodesh`): the
//! service, the updater and the apps all recognise "installed" by that path, and the
//! VSTO add-in must live under the user's own profile. The installer never elevates.
//!
//! `Layout::sandbox` redirects every file and HKCU path under a test folder / test key,
//! so the full flow can be exercised on a machine that has the real product installed.

use std::path::{Path, PathBuf};

pub const PRODUCT_KEY: &str = "KleiKodesh";
pub const DISPLAY_NAME_HE: &str = "כלי קודש";
pub const PUBLISHER_HE: &str = "צוות כלי קודש";

/// The setup exe as kept inside the install folder (payload stripped) for Modify / Uninstall.
pub const SETUP_EXE: &str = "KleiKodeshSetup.exe";
pub const VSTO_MANIFEST: &str = "KleiKodesh.vsto";
pub const KITVEI_EXE: &str = "כתבי הקודש.exe";
pub const SERVICE_DIR: &str = "KitveiHakodeshService";
pub const SERVICE_EXE: &str = "KitveiHakodeshService.exe";
pub const TUTOR_DIR: &str = "TypingTutor";
pub const LOG_FILE: &str = "setup.log";

#[derive(Clone, Debug)]
pub struct Layout {
    pub install_dir: PathBuf,
    pub start_menu_dir: PathBuf,
    pub desktop_dir: PathBuf,
    pub word_startup_dir: PathBuf,
    /// Prefix for every HKCU path: "" normally, a test key in the sandbox.
    hkcu_prefix: String,
    pub sandboxed: bool,
}

impl Layout {
    pub fn real() -> Self {
        let local = env_dir("LOCALAPPDATA");
        let roaming = env_dir("APPDATA");
        let profile = env_dir("USERPROFILE");
        Self {
            install_dir: local.join(PRODUCT_KEY),
            start_menu_dir: roaming.join(r"Microsoft\Windows\Start Menu\Programs").join(DISPLAY_NAME_HE),
            desktop_dir: profile.join("Desktop"),
            word_startup_dir: roaming.join(r"Microsoft\Word\STARTUP"),
            hkcu_prefix: String::new(),
            sandboxed: false,
        }
    }

    pub fn sandbox(root: &Path) -> Self {
        Self {
            install_dir: root.join("Install"),
            start_menu_dir: root.join("StartMenu"),
            desktop_dir: root.join("Desktop"),
            word_startup_dir: root.join("WordStartup"),
            hkcu_prefix: r"Software\KleiKodeshSetupSandbox\".to_string(),
            sandboxed: true,
        }
    }

    /// An HKCU sub-path, redirected under the test key in the sandbox.
    pub fn hkcu(&self, path: &str) -> String {
        format!("{}{}", self.hkcu_prefix, path)
    }

    pub fn file(&self, relative: &str) -> PathBuf {
        self.install_dir.join(relative)
    }

    pub fn service_exe(&self) -> PathBuf {
        self.install_dir.join(SERVICE_DIR).join(SERVICE_EXE)
    }

    pub fn installed_setup_exe(&self) -> PathBuf {
        self.install_dir.join(SETUP_EXE)
    }
}

fn env_dir(name: &str) -> PathBuf {
    std::env::var_os(name).map(PathBuf::from).unwrap_or_else(|| PathBuf::from(r"C:\Users\Public"))
}
