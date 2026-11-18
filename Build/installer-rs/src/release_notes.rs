//! "What's new": the release notes built into this exe (release-notes.json, written by
//! .github/scripts/ai-release-plan.ps1). Sections new / improved / fixed; in each, areas
//! with their changes, in Hebrew and English.

use serde_json::Value;

const NOTES: &str = include_str!(concat!(env!("OUT_DIR"), "/release-notes.json"));

/// One line of the "What's new" screen.
pub struct Line {
    pub kind: LineKind,
    pub text: String,
}

#[derive(Clone, Copy, PartialEq, Eq)]
pub enum LineKind {
    Section,
    Area,
    Change,
}

pub fn lines(english: bool) -> Vec<Line> {
    let lang = if english { "en" } else { "he" };
    let notes: Value = serde_json::from_str(NOTES.trim_start_matches('\u{feff}')).unwrap_or(Value::Null);
    let mut out = Vec::new();
    for section in notes["sections"].as_array().into_iter().flatten() {
        let heading = match (section["kind"].as_str(), english) {
            (Some("new"), false) => "חדש",
            (Some("new"), true) => "New",
            (Some("improved"), false) => "שיפורים",
            (Some("improved"), true) => "Improved",
            (_, false) => "תיקונים",
            (_, true) => "Fixed",
        };
        out.push(Line { kind: LineKind::Section, text: heading.into() });
        for area in section["areas"].as_array().into_iter().flatten() {
            out.push(Line { kind: LineKind::Area, text: area[lang].as_str().unwrap_or_default().into() });
            for change in area["changes"].as_array().into_iter().flatten() {
                out.push(Line { kind: LineKind::Change, text: change[lang].as_str().unwrap_or_default().into() });
            }
        }
    }
    out
}

pub fn any() -> bool {
    !lines(false).is_empty()
}
