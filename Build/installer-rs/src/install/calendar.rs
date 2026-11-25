//! The calendar option: a shortcut that opens Kitvei Hakodesh's calendar widget (the month
//! beside the picked day's zmanim and daily learning) in a window of its own - the app run
//! with `--calendar`. When the app is already open the calendar opens in it. No files of its
//! own beyond the shortcut's icon, which also marks the option as installed.

use std::path::PathBuf;

use crate::i18n::Text;
use crate::layout::{Layout, CALENDAR_DIR, KITVEI_EXE};
use crate::system::shortcut;

/// The Start-menu name, in the install language.
const SHORTCUT: Text = Text::new("לוח שנה כתבי הקודש", "Kitvei Hakodesh Calendar");
const SHORTCUT_NAMES: [&str; 2] = [SHORTCUT.he, SHORTCUT.en];
const ICON: &str = "calendar.ico";
/// The app's argument that opens the calendar widget (Program.CalendarRequest).
const ARGUMENT: &str = "--calendar";
/// A purple calendar (the typing tutor icon's purple), so the shortcut is told apart from
/// the app's own.
const ICON_BYTES: &[u8] = include_bytes!("../../assets/calendar.ico");

fn icon(layout: &Layout) -> PathBuf {
    layout.install_dir.join(CALENDAR_DIR).join(ICON)
}

pub fn is_installed(layout: &Layout) -> bool {
    icon(layout).exists()
}

pub fn register(layout: &Layout, desktop_shortcut: bool, english: bool) -> std::io::Result<()> {
    let dir = layout.install_dir.join(CALENDAR_DIR);
    // An install from before the calendar became a widget left its offline page here.
    let _ = std::fs::remove_file(dir.join("calendar.html"));
    std::fs::create_dir_all(&dir)?;
    let icon = icon(layout);
    std::fs::write(&icon, ICON_BYTES)?;
    let exe = layout.file(KITVEI_EXE);
    let link = shortcut::Shortcut {
        target: &exe,
        arguments: ARGUMENT,
        working_dir: &layout.install_dir,
        icon: &icon,
        description: Text::new("לוח שנה, זמני היום ולימוד יומי", "Calendar, zmanim and daily learning").get(english),
    };
    layout.place_shortcut(&SHORTCUT_NAMES, SHORTCUT.get(english), english, desktop_shortcut, &link)
}

pub fn unregister(layout: &Layout) {
    layout.remove_shortcuts(&SHORTCUT_NAMES);
    let _ = std::fs::remove_dir_all(layout.install_dir.join(CALENDAR_DIR));
}
