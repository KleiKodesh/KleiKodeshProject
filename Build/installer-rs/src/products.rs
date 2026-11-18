//! The three things the user can install, in display order.

use crate::i18n::Text;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum Product {
    /// The Klei Kodesh VSTO add-in for Word (includes Kitvei Hakodesh inside Word).
    WordAddin,
    /// Kitvei Hakodesh as its own app: Start-menu entry, "Open with", URL scheme.
    KitveiApp,
    /// The Hebrew typing tutor: a single offline page opened as an Edge app window.
    TypingTutor,
}

pub const ALL: [Product; 3] = [Product::WordAddin, Product::KitveiApp, Product::TypingTutor];

impl Product {
    /// Stable id shared with the UI (`Product.id` in state.slint).
    pub fn id(self) -> &'static str {
        match self {
            Product::WordAddin => "addin",
            Product::KitveiApp => "kitvei",
            Product::TypingTutor => "tutor",
        }
    }

    pub fn from_id(id: &str) -> Option<Self> {
        ALL.into_iter().find(|p| p.id() == id)
    }

    /// The add-in and the app share one set of files (the add-in's build output, which
    /// already contains `כתבי הקודש.exe` and the service). The tutor has its own folder.
    pub fn needs_core_files(self) -> bool {
        matches!(self, Product::WordAddin | Product::KitveiApp)
    }

    pub fn title(self) -> Text {
        match self {
            Product::WordAddin => Text::new("תוסף לוורד", "Word add-in"),
            Product::KitveiApp => Text::new("כתבי הקודש", "Kitvei Hakodesh"),
            Product::TypingTutor => Text::new("הקלדה עיוורת", "Touch Typing"),
        }
    }

    pub fn description(self) -> Text {
        match self {
            Product::WordAddin => Text::new(
                "כתבי הקודש, עיצוב תורני, ניקוד, חיפוש רגקס ועוד — בחלוניות בתוך וורד.",
                "Kitvei Hakodesh, Torah formatting, Nakdan, regex search and more, in panes inside Word.",
            ),
            Product::KitveiApp => Text::new(
                "הספרייה כתוכנה עצמאית בחלון משלה — גם בלי וורד.",
                "The library as its own app in its own window, no Word needed.",
            ),
            Product::TypingTutor => Text::new(
                "לימוד הקלדה עיוורת בעברית. נפתח בחלון משלו ועובד גם בלי אינטרנט.",
                "Learn Hebrew touch typing. Opens in its own window and works offline.",
            ),
        }
    }

    pub fn size(self) -> Text {
        match self {
            Product::WordAddin => Text::new("כ־120 MB", "about 120 MB"),
            Product::KitveiApp => Text::new("כ־180 MB", "about 180 MB"),
            Product::TypingTutor => Text::new("פחות מ־1 MB", "under 1 MB"),
        }
    }

    /// Shown instead of the size when the product cannot be installed here.
    pub fn unavailable_note(self) -> Text {
        match self {
            Product::WordAddin => Text::new("וורד לא נמצא במחשב", "Word was not found on this computer"),
            _ => Text::new("", ""),
        }
    }
}
