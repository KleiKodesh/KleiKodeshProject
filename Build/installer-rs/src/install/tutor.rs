//! The typing tutor: one offline page (`TypingTutor\index.html`) opened by a shortcut as
//! an Edge app window (see `edge_app`).

use std::path::PathBuf;

use crate::install::edge_app;
use crate::layout::{Layout, TUTOR_DIR};
use crate::system::shortcut;

use crate::i18n::Text;

const SHORTCUT: Text = Text::new("הקלדה עיוורת", "Touch Typing");
/// Every name the shortcut may have now or had before (first builds: the old Hebrew name).
const SHORTCUT_NAMES: [&str; 3] = [SHORTCUT.he, SHORTCUT.en, "מאמן הקלדה"];
const PAGE: &str = "index.html";
const ICON: &str = "tutor.ico";
/// The tutor's own icon (its favicon, a blue aleph), written beside the page so the
/// shortcut shows it instead of Edge's.
const ICON_BYTES: &[u8] = include_bytes!("../../assets/tutor.ico");

pub fn page(layout: &Layout) -> PathBuf {
    layout.install_dir.join(TUTOR_DIR).join(PAGE)
}

pub fn is_installed(layout: &Layout) -> bool {
    page(layout).exists()
}

pub fn register(layout: &Layout, desktop_shortcut: bool, english: bool) -> std::io::Result<()> {
    let edge = edge_app::edge_exe().ok_or_else(|| std::io::Error::other("Microsoft Edge was not found"))?;
    let arguments = edge_app::arguments(&page(layout));
    let tutor_dir = layout.install_dir.join(TUTOR_DIR);
    let icon = tutor_dir.join(ICON);
    std::fs::write(&icon, ICON_BYTES)?;
    let link = shortcut::Shortcut {
        target: &edge,
        arguments: &arguments,
        working_dir: &tutor_dir,
        icon: &icon,
        description: Text::new("הקלדה עיוורת בעברית", "Hebrew touch typing").get(english),
    };
    layout.place_shortcut(&SHORTCUT_NAMES, SHORTCUT.get(english), english, desktop_shortcut, &link)?;
    Ok(())
}

pub fn unregister(layout: &Layout) {
    layout.remove_shortcuts(&SHORTCUT_NAMES);
    let _ = std::fs::remove_dir_all(layout.install_dir.join(TUTOR_DIR));
}
