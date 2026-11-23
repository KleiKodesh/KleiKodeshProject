//! The calendar app: the app's calendar page (zmanim, daily learning) built as one offline
//! file (`Calendar\calendar.html`, vue-frontend vite.calendar.config.ts), opened by a
//! shortcut as an Edge app window (see `edge_app`). No service runs for it; a click on a
//! daily learning hands a kitveihakodeshapp:// link to Kitvei Hakodesh.

use std::path::PathBuf;

use crate::i18n::Text;
use crate::install::edge_app;
use crate::layout::{Layout, CALENDAR_DIR};
use crate::system::shortcut;

/// The Start-menu name, in the install language.
const SHORTCUT: Text = Text::new("לוח שנה כתבי הקודש", "Kitvei Hakodesh Calendar");
const SHORTCUT_NAMES: [&str; 2] = [SHORTCUT.he, SHORTCUT.en];
const PAGE: &str = "calendar.html";
const ICON: &str = "calendar.ico";
/// A green calendar (the app's calendar color), written beside the page so the shortcut
/// shows it instead of Edge's.
const ICON_BYTES: &[u8] = include_bytes!("../../assets/calendar.ico");

pub fn page(layout: &Layout) -> PathBuf {
    layout.install_dir.join(CALENDAR_DIR).join(PAGE)
}

pub fn is_installed(layout: &Layout) -> bool {
    page(layout).exists()
}

pub fn register(layout: &Layout, desktop_shortcut: bool, english: bool) -> std::io::Result<()> {
    let edge = edge_app::edge_exe().ok_or_else(|| std::io::Error::other("Microsoft Edge was not found"))?;
    let arguments = edge_app::arguments(&page(layout));
    let dir = layout.install_dir.join(CALENDAR_DIR);
    let icon = dir.join(ICON);
    std::fs::write(&icon, ICON_BYTES)?;
    let link = shortcut::Shortcut {
        target: &edge,
        arguments: &arguments,
        working_dir: &dir,
        icon: &icon,
        description: Text::new("לוח שנה, זמני היום ולימוד יומי", "Calendar, zmanim and daily learning").get(english),
    };
    layout.place_shortcut(&SHORTCUT_NAMES, SHORTCUT.get(english), english, desktop_shortcut, &link)
}

pub fn unregister(layout: &Layout) {
    layout.remove_shortcuts(&SHORTCUT_NAMES);
    let _ = std::fs::remove_dir_all(layout.install_dir.join(CALENDAR_DIR));
}
