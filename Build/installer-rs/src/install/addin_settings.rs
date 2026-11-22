//! The Word add-in options set from the installer (and only from it):
//!  * which tools the ribbon shows: `VB and VBA Program Settings\KleiKodesh\Ribbon\{id}_Visible`
//!    = "True"/"False", the add-in's own settings format, read by its `getVisible`;
//!  * which sites the Websites pane lists: `IsVisible` in `WebSitesWhitelist.json` beside
//!    the add-in. Entries with `AllowedUrls` are login helpers, not listed sites, and are
//!    left untouched;
//!  * whether the optional VBA macros template sits in Word's STARTUP folder (the file
//!    is the record, as in the add-in's `VbaMacrosTemplate`).

use std::path::PathBuf;

use serde_json::Value;

use crate::i18n::Text;
use crate::layout::Layout;
use crate::system::registry::Hkcu;

const RIBBON: &str = r"Software\VB and VBA Program Settings\KleiKodesh\Ribbon";
const SITES_FILE: &str = "WebSitesWhitelist.json";
/// The macros template the add-in ships, and every name it ever installed (for removal).
const MACROS_FILE: &str = "KleiKodeshMacros.en.dotm";
const MACROS_SOURCE: &str = r"Resources\KleiKodeshMacros.en.dotm";
pub const MACRO_TEMPLATES: [&str; 3] = [MACROS_FILE, "KleiKodeshMacros.he.dotm", "KleiKodeshMacros.dotm"];
/// The list the add-in ships with, used until the add-in's own copy exists.
const DEFAULT_SITES: &str = include_str!("../../../../KleiKodeshVsto/WebSitesTaskPane/WebSitesTaskPane/WebSitesWhitelist.json");

/// Ribbon control ids, in ribbon order.
pub const TOOLS: [(&str, Text); 7] = [
    ("KitveiHakodesh", Text::new("כתבי הקודש", "Kitvei Hakodesh")),
    ("HebrewBooks", Text::new("היברו-בוקס", "HebrewBooks")),
    ("Kiwix", Text::new("קיוויקס", "Kiwix")),
    ("WebSites", Text::new("דרך האתרים", "Websites")),
    ("DocDesign", Text::new("עיצוב תורני", "Torah formatting")),
    ("Nakdan", Text::new("ניקוד", "Nakdan")),
    ("RegexFind", Text::new("חיפוש רגקס", "Regex search")),
];

#[derive(Clone, Debug)]
pub struct Site {
    pub name: String,
    pub visible: bool,
}

#[derive(Clone, Debug)]
pub struct AddinSettings {
    /// Same order as `TOOLS`.
    pub tools_visible: Vec<bool>,
    pub sites: Vec<Site>,
    /// The VBA macros template is installed in Word's STARTUP folder.
    pub macros: bool,
}

impl AddinSettings {
    pub fn load(layout: &Layout) -> Self {
        let reg = Hkcu::new(layout);
        let tools_visible = TOOLS
            .iter()
            .map(|(id, _)| reg.get_str(RIBBON, &format!("{id}_Visible")).map_or(true, |v| !v.eq_ignore_ascii_case("false")))
            .collect();
        let sites = listed_sites(&read_sites(layout))
            .map(|entry| Site {
                name: entry["Name"].as_str().unwrap_or_default().to_string(),
                visible: entry["IsVisible"].as_bool().unwrap_or(true),
            })
            .collect();
        let macros = layout.word_startup_dir.join(MACROS_FILE).exists();
        Self { tools_visible, sites, macros }
    }

    /// Writes both. The sites file goes beside the add-in, so this runs after its files exist.
    pub fn save(&self, layout: &Layout) -> std::io::Result<()> {
        let reg = Hkcu::new(layout);
        for ((id, _), visible) in TOOLS.iter().zip(&self.tools_visible) {
            reg.set_str(RIBBON, &format!("{id}_Visible"), if *visible { "True" } else { "False" })?;
        }
        let mut all = read_sites(layout);
        if let Value::Array(entries) = &mut all {
            let mut wanted = self.sites.iter();
            for entry in entries.iter_mut().filter(|e| e.get("AllowedUrls").map_or(true, Value::is_null)) {
                if let Some(site) = wanted.next() {
                    entry["IsVisible"] = Value::Bool(site.visible);
                }
            }
        }
        let text = serde_json::to_string_pretty(&all).map_err(std::io::Error::other)?;
        std::fs::write(sites_path(layout), text)?;
        self.save_macros(layout)
    }

    /// Installs (or refreshes) the macros template, or removes every one we ever installed.
    /// Untouched when the choice did not change: Word locks the file while it runs.
    fn save_macros(&self, layout: &Layout) -> std::io::Result<()> {
        if self.macros == layout.word_startup_dir.join(MACROS_FILE).exists() {
            return Ok(());
        }
        let source = layout.file(MACROS_SOURCE);
        if self.macros && !source.exists() {
            crate::log::line(&format!("macros template missing, not installed: {}", source.display()));
            return Ok(());
        }
        for name in MACRO_TEMPLATES {
            let path = layout.word_startup_dir.join(name);
            if path.exists() {
                std::fs::remove_file(path)?;
            }
        }
        if self.macros {
            std::fs::create_dir_all(&layout.word_startup_dir)?;
            std::fs::copy(source, layout.word_startup_dir.join(MACROS_FILE))?;
        }
        Ok(())
    }
}

fn sites_path(layout: &Layout) -> PathBuf {
    layout.file(SITES_FILE)
}

/// The sites file as the current format: an older file is converted in memory (see `migrate`).
fn read_sites(layout: &Layout) -> Value {
    let default = default_sites();
    match std::fs::read_to_string(sites_path(layout)).ok().and_then(|t| parse(&t)) {
        Some(user) => migrate(user, default),
        None => default,
    }
}

fn parse(text: &str) -> Option<Value> {
    serde_json::from_str(text.trim_start_matches('\u{feff}')).ok()
}

fn default_sites() -> Value {
    parse(DEFAULT_SITES).unwrap_or(Value::Array(vec![]))
}

/// Converts an older sites file in place to the current format, keeping the user's choices.
/// The installer never overwrites the file (it is user data), so without this an old file
/// would stay old forever. The first old copy is kept as `WebSitesWhitelist.json.bak`.
/// An unreadable file is left alone.
pub fn migrate_sites_file(layout: &Layout) {
    let path = sites_path(layout);
    let Some(user) = std::fs::read_to_string(&path).ok().and_then(|t| parse(&t)) else { return };
    let migrated = migrate(user.clone(), default_sites());
    if migrated == user {
        return;
    }
    let backup = path.with_extension("json.bak");
    if !backup.exists() {
        let _ = std::fs::copy(&path, &backup);
    }
    let result = serde_json::to_string_pretty(&migrated).map_err(std::io::Error::other).and_then(|t| std::fs::write(&path, t));
    match result {
        Ok(()) => crate::log::line("converted WebSitesWhitelist.json to the current format"),
        Err(e) => crate::log::line(&format!("converting WebSitesWhitelist.json failed: {e}")),
    }
}

/// Current format: every shipped site listed with `IsVisible` true/false, then one last
/// entry holding `AllowedUrls` (login helper domains).
/// Older files list only the sites the user ticked, with no `IsVisible`, and have no helper
/// entry. The user's choices carry over: a shipped site absent from an old file was
/// unticked, so it stays hidden. Sites the user added themselves are kept, and so are edits
/// to a site's name or description.
fn migrate(user: Value, default: Value) -> Value {
    let Some(user_entries) = user.as_array() else { return default };
    let user_sites: Vec<&Value> = listed_sites(&user).collect();
    let old_format = user_sites.iter().any(|e| e.get("IsVisible").is_none());
    let key = |e: &Value| e["Url"].as_str().unwrap_or_default().trim().trim_end_matches('/').to_lowercase();

    let mut out: Vec<Value> = Vec::new();
    let mut seen: Vec<String> = Vec::new();
    for shipped in listed_sites(&default) {
        let k = key(shipped);
        seen.push(k.clone());
        match user_sites.iter().find(|u| key(u) == k) {
            Some(mine) => {
                let mut entry = (*mine).clone();
                for field in ["Name", "Description", "Url"] {
                    if entry.get(field).is_none() {
                        entry[field] = shipped[field].clone();
                    }
                }
                if entry.get("IsVisible").is_none() {
                    entry["IsVisible"] = Value::Bool(true);
                }
                out.push(entry);
            }
            None => {
                let mut entry = shipped.clone();
                if old_format {
                    entry["IsVisible"] = Value::Bool(false);
                }
                out.push(entry);
            }
        }
    }
    for mine in user_sites {
        let k = key(mine);
        if !seen.contains(&k) {
            seen.push(k);
            let mut entry = mine.clone();
            if entry.get("IsVisible").is_none() {
                entry["IsVisible"] = Value::Bool(true);
            }
            out.push(entry);
        }
    }

    let mut domains: Vec<String> = Vec::new();
    for entry in default.as_array().into_iter().flatten().chain(user_entries) {
        for domain in entry["AllowedUrls"].as_array().into_iter().flatten().filter_map(Value::as_str) {
            if !domains.iter().any(|d| d.eq_ignore_ascii_case(domain)) {
                domains.push(domain.to_string());
            }
        }
    }
    if !domains.is_empty() {
        out.push(serde_json::json!({ "AllowedUrls": domains }));
    }
    Value::Array(out)
}

fn listed_sites(all: &Value) -> impl Iterator<Item = &Value> {
    all.as_array()
        .into_iter()
        .flatten()
        .filter(|e| e.get("AllowedUrls").map_or(true, Value::is_null))
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn site(name: &str, url: &str) -> Value {
        json!({ "Name": name, "Description": "d", "Url": url, "IsVisible": true })
    }

    fn default() -> Value {
        json!([site("A", "https://a.com/"), site("B", "https://b.com"), { "AllowedUrls": ["login.net"] }])
    }

    #[test]
    fn old_file_keeps_ticked_hides_unticked_and_gets_helpers() {
        // Old installer: only ticked sites, no IsVisible, no helper entry; plus the user's own site.
        let old = json!([
            { "Name": "A mine", "Description": "d", "Url": "https://A.com" },
            { "Name": "Mine", "Description": "d", "Url": "https://mine.org" }
        ]);
        let out = migrate(old, default());
        let out = out.as_array().unwrap();
        assert_eq!(out.len(), 4);
        assert_eq!(out[0]["Name"], "A mine"); // user's edit kept
        assert_eq!(out[0]["IsVisible"], true);
        assert_eq!(out[1]["Url"], "https://b.com");
        assert_eq!(out[1]["IsVisible"], false); // was unticked
        assert_eq!(out[2]["Name"], "Mine");
        assert_eq!(out[2]["IsVisible"], true);
        assert_eq!(out[3]["AllowedUrls"], json!(["login.net"]));
    }

    #[test]
    fn current_file_keeps_choices_and_picks_up_new_shipped_sites() {
        let mine = json!([
            { "Name": "A", "Description": "d", "Url": "https://a.com/", "IsVisible": false },
            { "AllowedUrls": ["extra.com"] }
        ]);
        let out = migrate(mine, default());
        let out = out.as_array().unwrap();
        assert_eq!(out[0]["IsVisible"], false);
        assert_eq!(out[1]["IsVisible"], true); // new shipped site: shipped default
        assert_eq!(out[2]["AllowedUrls"], json!(["login.net", "extra.com"]));
    }

    #[test]
    fn migrating_twice_changes_nothing() {
        let old = json!([{ "Name": "A", "Description": "d", "Url": "https://a.com/" }]);
        let once = migrate(old, default());
        assert_eq!(migrate(once.clone(), default()), once);
    }
}
