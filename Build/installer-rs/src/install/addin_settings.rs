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
const DEFAULT_SITES: &str = include_str!("../../../../KleiKodeshVsto/WebSitesLib/WebSitesLib/WebSitesWhitelist.json");

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

fn read_sites(layout: &Layout) -> Value {
    let text = std::fs::read_to_string(sites_path(layout)).unwrap_or_else(|_| DEFAULT_SITES.to_string());
    serde_json::from_str(text.trim_start_matches('\u{feff}'))
        .or_else(|_| serde_json::from_str(DEFAULT_SITES.trim_start_matches('\u{feff}')))
        .unwrap_or(Value::Array(vec![]))
}

fn listed_sites(all: &Value) -> impl Iterator<Item = &Value> {
    all.as_array()
        .into_iter()
        .flatten()
        .filter(|e| e.get("AllowedUrls").map_or(true, Value::is_null))
}
