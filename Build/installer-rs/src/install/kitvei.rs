//! Kitvei Hakodesh as its own app: Start-menu (and optional desktop) shortcut, the
//! `kitveihakodeshapp://` link scheme, and an optional "Open with" entry for documents.
//!
//! "Open with" uses only `Applications\<exe>` + SupportedTypes. A ProgId with an open
//! command made Windows pick the app as the DEFAULT for .docx on some machines.

use crate::layout::{Layout, KITVEI_EXE};
use crate::system::{self, registry::Hkcu, shortcut};

const SHORTCUT_NAME: &str = "כתבי הקודש.lnk";
const FRIENDLY_NAME: &str = "כתבי הקודש";
const URL_SCHEME: &str = r"Software\Classes\kitveihakodeshapp";
const OPEN_WITH: &str = r"Software\Classes\Applications\כתבי הקודש.exe";
/// Written by old versions; removed whenever we touch associations.
const LEGACY_PROG_ID: &str = "KitveiHakodesh.Document.1";
/// Offered in "Open with": PDF only.
const OPEN_WITH_TYPES: [&str; 1] = [".pdf"];
/// Every type older versions registered, cleaned up whenever we touch associations.
const LEGACY_TYPES: [&str; 7] = [".pdf", ".doc", ".docx", ".rtf", ".txt", ".htm", ".html"];

pub fn is_registered(layout: &Layout) -> bool {
    layout.start_menu_dir.join(SHORTCUT_NAME).exists()
}

pub fn has_open_with(layout: &Layout) -> bool {
    Hkcu::new(layout).exists(OPEN_WITH)
}

pub fn has_desktop_shortcut(layout: &Layout) -> bool {
    layout.desktop_dir.join(SHORTCUT_NAME).exists()
}

pub fn register(layout: &Layout, desktop_shortcut: bool, open_with: bool) -> std::io::Result<()> {
    let exe = layout.file(KITVEI_EXE);
    let link = shortcut::Shortcut {
        target: &exe,
        arguments: "",
        working_dir: &layout.install_dir,
        icon: &exe,
        description: "כתבי הקודש — מאגר ספרי קודש",
    };
    shortcut::create(&layout.start_menu_dir.join(SHORTCUT_NAME), &link).map_err(std::io::Error::other)?;
    let desktop = layout.desktop_dir.join(SHORTCUT_NAME);
    if desktop_shortcut {
        shortcut::create(&desktop, &link).map_err(std::io::Error::other)?;
    } else {
        let _ = std::fs::remove_file(desktop);
    }

    let reg = Hkcu::new(layout);
    let command = format!("\"{}\" \"%1\"", exe.display());
    reg.set_str(URL_SCHEME, "", &format!("URL:{FRIENDLY_NAME}"))?;
    reg.set_str(URL_SCHEME, "URL Protocol", "")?;
    reg.set_str(&format!(r"{URL_SCHEME}\DefaultIcon"), "", &format!("{},0", exe.display()))?;
    reg.set_str(&format!(r"{URL_SCHEME}\shell\open\command"), "", &command)?;

    remove_legacy_prog_id(&reg);
    if open_with {
        reg.set_str(OPEN_WITH, "FriendlyAppName", FRIENDLY_NAME)?;
        reg.delete_tree(&format!(r"{OPEN_WITH}\SupportedTypes"));
        for ext in OPEN_WITH_TYPES {
            reg.set_str(&format!(r"{OPEN_WITH}\SupportedTypes"), ext, "")?;
        }
        reg.set_str(&format!(r"{OPEN_WITH}\shell\open\command"), "", &command)?;
    } else {
        reg.delete_tree(OPEN_WITH);
    }
    system::notify_associations_changed();
    Ok(())
}

pub fn unregister(layout: &Layout) {
    let _ = std::fs::remove_file(layout.start_menu_dir.join(SHORTCUT_NAME));
    let _ = std::fs::remove_file(layout.desktop_dir.join(SHORTCUT_NAME));
    let reg = Hkcu::new(layout);
    reg.delete_tree(URL_SCHEME);
    reg.delete_tree(OPEN_WITH);
    remove_legacy_prog_id(&reg);
    system::notify_associations_changed();
}

fn remove_legacy_prog_id(reg: &Hkcu) {
    reg.delete_tree(&format!(r"Software\Classes\{LEGACY_PROG_ID}"));
    for ext in LEGACY_TYPES {
        reg.delete_value(&format!(r"Software\Classes\{ext}\OpenWithProgids"), LEGACY_PROG_ID);
        reg.delete_value(
            &format!(r"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\OpenWithProgids"),
            LEGACY_PROG_ID,
        );
    }
}
