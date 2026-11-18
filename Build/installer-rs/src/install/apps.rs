//! Programs that hold our files open and must close first: Word (loads the add-in) and
//! the Kitvei Hakodesh app. They are closed like clicking X, so they can ask to save.

use std::time::Duration;

use crate::i18n::Text;
use crate::system::processes;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum RunningApp {
    Word,
    Kitvei,
}

impl RunningApp {
    fn exe(self) -> &'static str {
        match self {
            RunningApp::Word => "WINWORD.EXE",
            RunningApp::Kitvei => crate::layout::KITVEI_EXE,
        }
    }

    pub fn name(self) -> Text {
        match self {
            RunningApp::Word => Text::new("וורד", "Word"),
            RunningApp::Kitvei => Text::new("כתבי הקודש", "Kitvei Hakodesh"),
        }
    }
}

pub fn running(candidates: &[RunningApp]) -> Vec<RunningApp> {
    candidates.iter().copied().filter(|app| processes::is_running(app.exe())).collect()
}

/// Closes them and waits (generously: the user may be saving a document).
pub fn close(apps: &[RunningApp]) -> bool {
    apps.iter().all(|app| processes::close_gracefully(app.exe(), Duration::from_secs(90)))
}
