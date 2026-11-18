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
pub const DISPLAY_NAME_EN: &str = "Klei Kodesh";
pub const PUBLISHER_HE: &str = "צוות כלי קודש";
pub const PUBLISHER_EN: &str = "Klei Kodesh team";

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
    /// The Start menu's Programs folder; our folder in it is named in the UI language.
    pub start_menu_root: PathBuf,
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
            start_menu_root: roaming.join(r"Microsoft\Windows\Start Menu\Programs"),
            desktop_dir: profile.join("Desktop"),
            word_startup_dir: roaming.join(r"Microsoft\Word\STARTUP"),
            hkcu_prefix: String::new(),
            sandboxed: false,
        }
    }

    /// Our Start-menu folder, named in the UI language.
    pub fn start_menu_dir(&self, english: bool) -> PathBuf {
        self.start_menu_root.join(if english { DISPLAY_NAME_EN } else { DISPLAY_NAME_HE })
    }

    /// Both language variants, for finding and removing what an earlier install made.
    pub fn start_menu_dirs(&self) -> [PathBuf; 2] {
        [self.start_menu_dir(false), self.start_menu_dir(true)]
    }

    /// Creates `name.lnk` in our Start-menu folder (and on the desktop when asked), after
    /// removing every earlier spelling of it from both folders and the desktop, so a
    /// language switch leaves no stale shortcut. The other-language folder goes when empty.
    pub fn place_shortcut(&self, names: &[&str], name: &str, english: bool, desktop: bool, link: &crate::system::shortcut::Shortcut) -> std::io::Result<()> {
        self.remove_shortcuts(names);
        let dir = self.start_menu_dir(english);
        crate::system::shortcut::create(&dir.join(format!("{name}.lnk")), link).map_err(std::io::Error::other)?;
        let _ = std::fs::remove_dir(self.start_menu_dir(!english));
        if desktop {
            crate::system::shortcut::create(&self.desktop_dir.join(format!("{name}.lnk")), link).map_err(std::io::Error::other)?;
        }
        Ok(())
    }

    pub fn remove_shortcuts(&self, names: &[&str]) {
        for name in names {
            let file = format!("{name}.lnk");
            for dir in self.start_menu_dirs() {
                let _ = std::fs::remove_file(dir.join(&file));
            }
            let _ = std::fs::remove_file(self.desktop_dir.join(&file));
        }
    }

    pub fn has_shortcut(&self, names: &[&str], desktop: bool) -> bool {
        names.iter().any(|name| {
            let file = format!("{name}.lnk");
            if desktop { self.desktop_dir.join(&file).exists() } else { self.start_menu_dirs().iter().any(|d| d.join(&file).exists()) }
        })
    }

    pub fn sandbox(root: &Path) -> Self {
        Self {
            install_dir: root.join("Install"),
            start_menu_root: root.join("StartMenu"),
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
