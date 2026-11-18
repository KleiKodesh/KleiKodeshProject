//! Registering the VSTO add-in with Word, per user.
//!
//!  * `Office\Word\Addins\KleiKodesh` (+ `AddinsData`): what Word loads, LoadBehavior 3.
//!  * `Office\<ver>\Word\Resiliency\DoNotDisableAddinList`: Word's slow-add-in check must
//!    not flip it off.
//!  * `VSTO\Security\Inclusion`: trust the manifest by its public key, so Word never shows
//!    "Install this add-in?". The URL is the bare manifest URL (no `|vstolocal`) - that is
//!    the form VSTO matches against.
//!  * `VSTO\Security\TrustedPaths`: the install folder.

use std::path::Path;

use crate::layout::{Layout, DISPLAY_NAME_HE, PRODUCT_KEY, VSTO_MANIFEST};
use crate::system::registry::Hkcu;

const ADDINS: &str = r"Software\Microsoft\Office\Word\Addins\KleiKodesh";
const ADDINS_DATA: &str = r"Software\Microsoft\Office\Word\AddinsData\KleiKodesh";
const INCLUSION: &str = r"Software\Microsoft\VSTO\Security\Inclusion";
const TRUSTED_PATHS: &str = r"Software\Microsoft\VSTO\Security\TrustedPaths";
const OFFICE_VERSIONS: [&str; 2] = ["15.0", "16.0"];
/// The legacy macro templates the add-in can place in Word's STARTUP folder.
const MACRO_TEMPLATES: [&str; 3] = ["KleiKodeshMacros.dotm", "KleiKodeshMacros.he.dotm", "KleiKodeshMacros.en.dotm"];

pub fn is_registered(layout: &Layout) -> bool {
    Hkcu::new(layout).exists(ADDINS)
}

pub fn register(layout: &Layout) -> std::io::Result<()> {
    let reg = Hkcu::new(layout);
    let manifest = layout.file(VSTO_MANIFEST);
    let load_value = format!("file:///{}|vstolocal", manifest.display());
    for key in [ADDINS, ADDINS_DATA] {
        reg.set_str(key, "Description", DISPLAY_NAME_HE)?;
        reg.set_str(key, "FriendlyName", DISPLAY_NAME_HE)?;
        reg.set_str(key, "Manifest", &load_value)?;
        reg.set_dword(key, "LoadBehavior", 3)?;
    }
    for version in OFFICE_VERSIONS {
        let _ = reg.set_dword(&resiliency_key(version), PRODUCT_KEY, 1);
    }
    trust(layout, &manifest);
    Ok(())
}

pub fn unregister(layout: &Layout) {
    let reg = Hkcu::new(layout);
    reg.delete_tree(ADDINS);
    reg.delete_tree(ADDINS_DATA);
    for version in OFFICE_VERSIONS {
        reg.delete_value(&resiliency_key(version), PRODUCT_KEY);
        reg.delete_value(&format!(r"Software\Microsoft\Office\{version}\Word\AddInLoadTimes"), PRODUCT_KEY);
    }
    remove_inclusion_entries(&reg, &manifest_url(&layout.file(VSTO_MANIFEST)));
    reg.delete_tree(&format!(r"{TRUSTED_PATHS}\{}", trusted_path_key(&layout.install_dir)));
    for template in MACRO_TEMPLATES {
        let _ = std::fs::remove_file(layout.word_startup_dir.join(template));
    }
}

fn resiliency_key(version: &str) -> String {
    format!(r"Software\Microsoft\Office\{version}\Word\Resiliency\DoNotDisableAddinList")
}

/// Best effort: without the manifest's public key an entry is useless, and replacing a
/// working one (written by VSTO itself) would only break trust.
fn trust(layout: &Layout, manifest: &Path) {
    let Some(public_key) = manifest_public_key(manifest) else { return };
    let reg = Hkcu::new(layout);
    let url = manifest_url(manifest);
    remove_inclusion_entries(&reg, &url);
    let entry = format!(r"{INCLUSION}\{}", new_guid());
    let _ = reg.set_str(&entry, "Url", &url);
    let _ = reg.set_str(&entry, "PublicKey", &public_key);

    let folder = format!(r"{TRUSTED_PATHS}\{}", trusted_path_key(&layout.install_dir));
    let _ = reg.set_str(&folder, "Path", &layout.install_dir.display().to_string());
    let _ = reg.set_dword(&folder, "AllowSubfolders", 1);
}

fn remove_inclusion_entries(reg: &Hkcu, url: &str) {
    let Some(inclusion) = reg.open(INCLUSION) else { return };
    let ours: Vec<String> = inclusion
        .enum_keys()
        .flatten()
        .filter(|name| {
            inclusion
                .open_subkey(name)
                .and_then(|k| k.get_value::<String, _>("Url"))
                .is_ok_and(|u| u.to_lowercase().starts_with(&url.to_lowercase()))
        })
        .collect();
    for name in ours {
        reg.delete_tree(&format!(r"{INCLUSION}\{name}"));
    }
}

fn manifest_url(manifest: &Path) -> String {
    format!("file:///{}", manifest.display().to_string().replace('\\', "/"))
}

fn manifest_public_key(manifest: &Path) -> Option<String> {
    let text = std::fs::read_to_string(manifest).ok()?;
    let start = text.find("<RSAKeyValue>")?;
    let end = text[start..].find("</RSAKeyValue>")? + start + "</RSAKeyValue>".len();
    Some(text[start..end].to_string())
}

/// Same key name scheme VSTO accepts for a trusted folder: the path, base64-encoded.
fn trusted_path_key(dir: &Path) -> String {
    base64(dir.display().to_string().as_bytes())
}

fn new_guid() -> String {
    let guid = unsafe { windows::Win32::System::Com::CoCreateGuid() }.unwrap_or_default();
    format!("{guid:?}").trim_matches(|c| c == '{' || c == '}').to_lowercase()
}

fn base64(bytes: &[u8]) -> String {
    const ALPHABET: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut out = String::new();
    for chunk in bytes.chunks(3) {
        let b = [chunk[0], *chunk.get(1).unwrap_or(&0), *chunk.get(2).unwrap_or(&0)];
        let n = (b[0] as u32) << 16 | (b[1] as u32) << 8 | b[2] as u32;
        for i in 0..4 {
            if i <= chunk.len() {
                out.push(ALPHABET[(n >> (18 - 6 * i) & 63) as usize] as char);
            } else {
                out.push('=');
            }
        }
    }
    out
}
