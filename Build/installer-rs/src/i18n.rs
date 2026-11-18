//! Hebrew / English text. Every user-visible string is a `Text` pair; the UI language
//! picks one side. Hebrew is the default; English when Windows is not in Hebrew.

#[derive(Clone, Copy, Debug)]
pub struct Text {
    pub he: &'static str,
    pub en: &'static str,
}

impl Text {
    pub const fn new(he: &'static str, en: &'static str) -> Self {
        Self { he, en }
    }

    pub fn get(self, english: bool) -> &'static str {
        if english { self.en } else { self.he }
    }
}

/// `--lang en|he` wins; otherwise Hebrew when the Windows display language is Hebrew.
pub fn start_in_english(args: &[String]) -> bool {
    if let Some(i) = args.iter().position(|a| a == "--lang") {
        if let Some(value) = args.get(i + 1) {
            return value != "he";
        }
    }
    const LANG_HEBREW: u16 = 0x0D;
    let primary = unsafe { windows::Win32::Globalization::GetUserDefaultUILanguage() } & 0x3ff;
    primary != LANG_HEBREW
}
