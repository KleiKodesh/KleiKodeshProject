//! Which files an update must leave alone, and which leftovers it must remove.

use std::path::Path;

use crate::layout::{CALENDAR_DIR, TUTOR_DIR};

/// User data and runtime-built indexes/caches. Never shipped as real content, expensive
/// or impossible to rebuild, so an existing copy always wins over the payload.
const KEEP_FILES: &[&str] = &[
    "WebSitesWhitelist.json",
    "excluded_folders.json",
    r"KitveiHakodeshService\excluded_folders.json",
];

const KEEP_FOLDERS: &[&str] = &[
    r"KitveiHakodesh\cache\",
    r"KitveiHakodesh\webcache\",
    r"BloomFilters\",
    r"filesystemindex\",
    r"KitveiHakodeshService\FtsIndex\",
    r"KitveiHakodeshService\CatalogTocIndex\",
    r"KitveiHakodeshService\filesystemindex\",
    r"KitveiHakodeshService\hb-cache\",
    r"KitveiHakodeshService\convert-cache\",
];

/// Files older versions shipped and newer ones do not, named one by one. Deleting by
/// pattern ("every dll the payload did not bring") was flagged by Defender as dropper
/// behaviour, and silently removes anything a newer feature writes into the folder.
const STALE_FILES: &[&str] = &[
    "DocumentLocator.Service.exe",
    "DocumentLocator.Service.pdb",
    r"x64\SQLite.Interop.dll",
    r"x86\SQLite.Interop.dll",
    "SQLite.Interop.dll",
    "System.Data.SQLite.dll",
];

pub fn keep_existing(relative: &Path) -> bool {
    let path = normalize(relative);
    KEEP_FILES.iter().any(|f| path.eq_ignore_ascii_case(f))
        || KEEP_FOLDERS.iter().any(|dir| starts_with_ignore_case(&path, dir))
}

/// Files only Kitvei Hakodesh uses: the service (search, library data, indexes), the
/// library screens, and the app's own exe. Left out when the add-in is installed alone; the
/// add-in's other tools never touch them, and the add-in updates itself without the service.
const KITVEI_FOLDERS: &[&str] = &[r"KitveiHakodeshService\", r"KitveiHakodesh\"];
const KITVEI_ROOT_FILE_PREFIX: &str = "כתבי הקודש.";

pub fn is_kitvei_file(relative: &Path) -> bool {
    let path = normalize(relative);
    KITVEI_FOLDERS.iter().any(|dir| starts_with_ignore_case(&path, dir))
        || (!path.contains('\\') && path.starts_with(KITVEI_ROOT_FILE_PREFIX))
}

/// Deletes Kitvei Hakodesh's files and its indexes (the user chose to, when unticking it).
/// The service folder's `updates` stays: the add-in downloads its own updates there.
pub fn delete_kitvei_files(install_dir: &Path) {
    fn remove(path: &Path) {
        let _ = if path.is_dir() { std::fs::remove_dir_all(path) } else { std::fs::remove_file(path) };
    }
    let Ok(entries) = std::fs::read_dir(install_dir) else { return };
    for entry in entries.flatten() {
        let name = entry.file_name().to_string_lossy().to_string();
        let path = entry.path();
        if name.eq_ignore_ascii_case(crate::layout::SERVICE_DIR) {
            let Ok(inner) = std::fs::read_dir(&path) else { continue };
            for item in inner.flatten().filter(|i| !i.file_name().to_string_lossy().eq_ignore_ascii_case("updates")) {
                remove(&item.path());
            }
        } else if name.eq_ignore_ascii_case("KitveiHakodesh") || name.starts_with(KITVEI_ROOT_FILE_PREFIX) {
            remove(&path);
        }
    }
    crate::log::line("deleted the Kitvei Hakodesh files");
}

pub fn is_tutor_file(relative: &Path) -> bool {
    starts_with_ignore_case(&normalize(relative), &format!("{TUTOR_DIR}\\"))
}

pub fn is_calendar_file(relative: &Path) -> bool {
    starts_with_ignore_case(&normalize(relative), &format!("{CALENDAR_DIR}\\"))
}

pub fn purge_stale_files(install_dir: &Path) {
    for relative in STALE_FILES {
        let path = install_dir.join(relative);
        if path.exists() && std::fs::remove_file(&path).is_ok() {
            crate::log::line(&format!("removed leftover {relative}"));
        }
    }
}

fn normalize(path: &Path) -> String {
    path.to_string_lossy().replace('/', "\\")
}

/// Byte-wise, so a Hebrew file name never gets sliced mid-character.
fn starts_with_ignore_case(text: &str, prefix: &str) -> bool {
    text.as_bytes().get(..prefix.len()).is_some_and(|head| head.eq_ignore_ascii_case(prefix.as_bytes()))
}
