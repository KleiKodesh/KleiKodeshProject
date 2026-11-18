//! Small HKCU / HKLM helpers. Every HKCU path goes through `Layout::hkcu` so the
//! sandbox stays inside its test key. HKLM is only ever read.

use std::io;

use winreg::enums::{HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE, KEY_READ, KEY_WOW64_32KEY, KEY_WOW64_64KEY};
use winreg::RegKey;

use crate::layout::Layout;

pub struct Hkcu<'a> {
    layout: &'a Layout,
    root: RegKey,
}

impl<'a> Hkcu<'a> {
    pub fn new(layout: &'a Layout) -> Self {
        Self { layout, root: RegKey::predef(HKEY_CURRENT_USER) }
    }

    pub fn create(&self, path: &str) -> io::Result<RegKey> {
        self.root.create_subkey(self.layout.hkcu(path)).map(|(key, _)| key)
    }

    pub fn open(&self, path: &str) -> Option<RegKey> {
        self.root.open_subkey(self.layout.hkcu(path)).ok()
    }

    pub fn exists(&self, path: &str) -> bool {
        self.open(path).is_some()
    }

    pub fn set_str(&self, path: &str, name: &str, value: &str) -> io::Result<()> {
        self.create(path)?.set_value(name, &value)
    }

    pub fn set_dword(&self, path: &str, name: &str, value: u32) -> io::Result<()> {
        self.create(path)?.set_value(name, &value)
    }

    pub fn get_str(&self, path: &str, name: &str) -> Option<String> {
        self.open(path)?.get_value(name).ok()
    }

    /// Deletes a key and everything under it; a missing key is not an error.
    pub fn delete_tree(&self, path: &str) {
        let _ = self.root.delete_subkey_all(self.layout.hkcu(path));
    }

    pub fn delete_value(&self, path: &str, name: &str) {
        if let Ok(key) = self.root.open_subkey_with_flags(self.layout.hkcu(path), winreg::enums::KEY_SET_VALUE) {
            let _ = key.delete_value(name);
        }
    }
}

/// Reads an HKLM string from both the 64- and 32-bit views (first hit wins).
pub fn hklm_str(path: &str, name: &str) -> Option<String> {
    hklm_views(path).find_map(|key| key.get_value::<String, _>(name).ok())
}

pub fn hklm_dword(path: &str, name: &str) -> Option<u32> {
    hklm_views(path).find_map(|key| key.get_value::<u32, _>(name).ok())
}

pub fn hklm_exists(path: &str) -> bool {
    hklm_views(path).next().is_some()
}

fn hklm_views(path: &str) -> impl Iterator<Item = RegKey> + '_ {
    let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
    [KEY_WOW64_64KEY, KEY_WOW64_32KEY]
        .into_iter()
        .filter_map(move |view| hklm.open_subkey_with_flags(path, KEY_READ | view).ok())
}

/// HKCU read without the sandbox prefix: for facts about the machine (e.g. WebView2 per-user).
pub fn hkcu_real_str(path: &str, name: &str) -> Option<String> {
    RegKey::predef(HKEY_CURRENT_USER).open_subkey(path).ok()?.get_value(name).ok()
}
