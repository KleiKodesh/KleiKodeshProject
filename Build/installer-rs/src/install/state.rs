//! What is installed, and how Windows lists it.
//!
//!  * `HKCU\Software\KleiKodesh`: Version + Products (the product ids, comma separated).
//!  * `...\Uninstall\KleiKodesh`: the "Apps & features" entry. Taken over in place from
//!    older installers (same key), pointing at the setup copy in the install folder.
//!  * The add-in's UI language lives in the add-in's own settings
//!    (`VB and VBA Program Settings\KleiKodesh\General\UiLanguage`); the installer
//!    writes the language it ran in, so a new install opens in the same language.

use std::collections::HashSet;

use crate::install::{addin, kitvei, tutor};
use crate::layout::{Layout, DISPLAY_NAME_HE, PRODUCT_KEY, PUBLISHER_HE};
use crate::products::{Product, ALL};
use crate::system::registry::Hkcu;

const STATE_KEY: &str = r"Software\KleiKodesh";
const UNINSTALL_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Uninstall\KleiKodesh";
const ADDIN_SETTINGS: &str = r"Software\VB and VBA Program Settings\KleiKodesh";

/// What is installed now. The saved list wins; without one (an install made by the old
/// installer) each product is detected from what it registers.
pub fn installed(layout: &Layout) -> HashSet<Product> {
    let saved = Hkcu::new(layout).get_str(STATE_KEY, "Products");
    match saved {
        Some(list) => list.split(',').filter_map(Product::from_id).collect(),
        None => ALL
            .into_iter()
            .filter(|p| match p {
                Product::WordAddin => addin::is_registered(layout),
                Product::KitveiApp => kitvei::is_registered(layout),
                Product::TypingTutor => tutor::is_installed(layout),
            })
            .collect(),
    }
}

pub fn save(layout: &Layout, products: &HashSet<Product>, version: &str) -> std::io::Result<()> {
    let reg = Hkcu::new(layout);
    let ids: Vec<&str> = ALL.into_iter().filter(|p| products.contains(p)).map(Product::id).collect();
    reg.set_str(STATE_KEY, "Products", &ids.join(","))?;
    reg.set_str(STATE_KEY, "Version", version)?;
    reg.set_str(STATE_KEY, "InstallerVariant", "AnyCPU")
}

pub fn write_uninstall_entry(layout: &Layout, version: &str, size_kb: u32) -> std::io::Result<()> {
    let reg = Hkcu::new(layout);
    let setup = layout.installed_setup_exe();
    let setup = setup.display();
    reg.delete_tree(UNINSTALL_KEY); // drop values only the old NSIS entry had (NoModify...)
    reg.set_str(UNINSTALL_KEY, "DisplayName", DISPLAY_NAME_HE)?;
    reg.set_str(UNINSTALL_KEY, "DisplayVersion", version)?;
    reg.set_str(UNINSTALL_KEY, "Publisher", PUBLISHER_HE)?;
    reg.set_str(UNINSTALL_KEY, "DisplayIcon", &format!("{setup},0"))?;
    reg.set_str(UNINSTALL_KEY, "InstallLocation", &layout.install_dir.display().to_string())?;
    reg.set_str(UNINSTALL_KEY, "UninstallString", &format!("\"{setup}\" --uninstall"))?;
    reg.set_str(UNINSTALL_KEY, "ModifyPath", &format!("\"{setup}\" --manage"))?;
    reg.set_dword(UNINSTALL_KEY, "EstimatedSize", size_kb)?;
    reg.set_dword(UNINSTALL_KEY, "NoRepair", 1) // Repair lives in our own Modify screen
}

pub fn set_addin_language(layout: &Layout, english: bool) {
    let _ = Hkcu::new(layout).set_str(&format!(r"{ADDIN_SETTINGS}\General"), "UiLanguage", if english { "en" } else { "he" });
}

/// Everything the product and its older versions kept in HKCU, for a full uninstall.
pub fn forget_everything(layout: &Layout) {
    let reg = Hkcu::new(layout);
    reg.delete_tree(UNINSTALL_KEY);
    reg.delete_tree(STATE_KEY);
    for app in [PRODUCT_KEY, "ZayitApp", "KitveiHakodesh"] {
        reg.delete_tree(&format!(r"Software\VB and VBA Program Settings\{app}"));
    }
}
