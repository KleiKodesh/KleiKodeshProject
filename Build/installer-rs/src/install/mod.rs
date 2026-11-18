//! The install engine. A `Request` (what to do, which products, which options) runs as an
//! ordered list of steps on a worker thread; a `Reporter` carries progress to the UI (or
//! nowhere, in silent mode) and answers the one question the engine may ask: whether
//! running programs may be closed.

pub mod addin;
pub mod addin_settings;
pub mod apps;
pub mod kitvei;
pub mod prereqs;
pub mod service;
pub mod state;
pub mod tutor;

use std::collections::HashSet;

use crate::i18n::Text;
use crate::layout::{Layout, SETUP_EXE, TUTOR_DIR, LOG_FILE};
use crate::payload::{self, Payload, Selection};
use crate::products::Product;
use apps::RunningApp;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Action {
    /// Install or update the chosen products (new install, upgrade, Modify).
    Apply,
    /// Re-apply what is installed: files from the payload if we have one, all registrations.
    Repair,
    Uninstall,
}

#[derive(Clone, Debug)]
pub struct Options {
    pub desktop_shortcut: bool,
    pub open_with: bool,
    pub english: bool,
}

impl Options {
    /// What a fresh install offers, or, when already installed, what is set now - so an
    /// update keeps the user's choices.
    pub fn current(layout: &Layout, english: bool) -> Self {
        if state::installed(layout).is_empty() {
            return Self { desktop_shortcut: false, open_with: true, english };
        }
        Self { desktop_shortcut: kitvei::has_desktop_shortcut(layout), open_with: kitvei::has_open_with(layout), english }
    }
}

#[derive(Clone, Debug)]
pub struct Request {
    pub action: Action,
    /// The products that should be installed when done (ignored for Uninstall).
    pub products: HashSet<Product>,
    pub options: Options,
    /// Remove the previous install first (files, caches, indexes, settings), then install.
    pub clean: bool,
    /// Word add-in options chosen in the installer; None = leave as they are.
    pub addin_settings: Option<addin_settings::AddinSettings>,
}

pub trait Reporter: Send {
    fn step(&self, text: Text);
    fn progress(&self, fraction: f32);
    /// Programs that must close. True = close them and continue; false = stop.
    fn confirm_close(&self, apps: &[RunningApp]) -> bool;
}

/// Why an install stopped. `Text` is shown to the user as-is.
#[derive(Debug)]
pub struct Failure {
    pub text: Text,
    pub detail: String,
    /// Stopped because Word or the app stayed open (silent mode exits with 2).
    pub apps_open: bool,
}

impl Failure {
    fn new(text: Text, detail: impl Into<String>) -> Self {
        Self { text, detail: detail.into(), apps_open: false }
    }

    fn apps_open(text: Text, detail: impl Into<String>) -> Self {
        Self { apps_open: true, ..Self::new(text, detail) }
    }
}

/// The release version, stamped by build.ps1 (KK_VERSION); a dev build reports the crate version.
pub const VERSION: &str = match option_env!("KK_VERSION") {
    Some(v) => v,
    None => env!("CARGO_PKG_VERSION"),
};

pub fn run(layout: &Layout, payload: Option<&Payload>, request: &Request, reporter: &dyn Reporter) -> Result<(), Failure> {
    crate::log::line(&format!("{:?} {:?}", request.action, request.products));
    let result = match request.action {
        Action::Uninstall => uninstall(layout, reporter),
        Action::Apply | Action::Repair => apply(layout, payload, request, reporter),
    };
    match &result {
        Ok(()) => crate::log::line("done"),
        Err(f) => crate::log::line(&format!("failed: {} ({})", f.text.en, f.detail)),
    }
    result
}

fn apply(layout: &Layout, payload: Option<&Payload>, request: &Request, reporter: &dyn Reporter) -> Result<(), Failure> {
    let wanted = &request.products;
    if request.clean {
        if payload.is_none() {
            return Err(Failure::new(
                Text::new("התקנה נקייה דורשת את קובץ ההתקנה המלא.", "A clean install needs the full setup file."),
                "clean install without payload",
            ));
        }
        remove_previous_install(layout, reporter)?;
    }
    let before = state::installed(layout);
    let core = wanted.iter().any(|p| p.needs_core_files());
    let core_before = before.iter().any(|p| p.needs_core_files());
    let repair = request.action == Action::Repair;

    reporter.step(Text::new("בודק רכיבים נדרשים", "Checking requirements"));
    check_prerequisites(layout, wanted, reporter)?;

    // Files are written (core) or the add-in changes: Word and the app must be closed.
    if core || core_before {
        close_apps(layout, &[RunningApp::Word, RunningApp::Kitvei], reporter)?;
        reporter.step(Text::new("עוצר את שירות כתבי הקודש", "Stopping the Kitvei Hakodesh service"));
        service::stop(layout);
    }

    reporter.step(Text::new("מעתיק קבצים", "Copying files"));
    let selection = Selection { core, tutor: wanted.contains(&Product::TypingTutor) };
    copy_files(layout, payload, selection, reporter)?;
    reporter.progress(0.75);

    reporter.step(Text::new("רושם את הרכיבים", "Registering"));
    register_products(layout, wanted, &request.options)?;
    if let (true, Some(settings)) = (wanted.contains(&Product::WordAddin), &request.addin_settings) {
        settings
            .save(layout)
            .map_err(|e| Failure::new(Text::new("שמירת אפשרויות התוסף נכשלה.", "Saving the add-in options failed."), e.to_string()))?;
    }
    if core_before && !core {
        remove_core_files(layout);
    }
    reporter.progress(0.85);

    if core {
        reporter.step(Text::new("מתקין את שירות כתבי הקודש", "Installing the Kitvei Hakodesh service"));
        if !service::ensure_registered(layout, repair) {
            crate::log::line("service registration declined or failed; the apps will start it themselves");
        }
    }

    reporter.step(Text::new("מסיים", "Finishing"));
    finish(layout, payload, wanted, request.options.english)?;
    reporter.progress(1.0);
    Ok(())
}

/// Clean install, first half: everything the previous install left goes - registrations,
/// settings, files, caches and indexes. The service registration stays; it is pointed at
/// the fresh files again right after.
fn remove_previous_install(layout: &Layout, reporter: &dyn Reporter) -> Result<(), Failure> {
    close_apps(layout, &[RunningApp::Word, RunningApp::Kitvei], reporter)?;
    reporter.step(Text::new("מסיר את ההתקנה הקודמת", "Removing the previous install"));
    service::stop(layout);
    addin::unregister(layout);
    kitvei::unregister(layout);
    tutor::unregister(layout);
    state::forget_everything(layout);
    if layout.install_dir.exists() {
        std::fs::remove_dir_all(&layout.install_dir).map_err(|e| {
            Failure::new(
                Text::new("לא ניתן היה למחוק את ההתקנה הקודמת. סגרו את כל התוכנות ונסו שוב.", "The previous install could not be removed. Close all programs and try again."),
                e.to_string(),
            )
        })?;
    }
    crate::log::init(layout.install_dir.join(crate::layout::LOG_FILE));
    reporter.progress(0.1);
    Ok(())
}

fn uninstall(layout: &Layout, reporter: &dyn Reporter) -> Result<(), Failure> {
    close_apps(layout, &[RunningApp::Word, RunningApp::Kitvei], reporter)?;
    reporter.step(Text::new("עוצר את שירות כתבי הקודש", "Stopping the Kitvei Hakodesh service"));
    service::stop(layout);
    service::remove(layout);
    reporter.progress(0.3);

    reporter.step(Text::new("מסיר את הרישומים", "Removing registrations"));
    addin::unregister(layout);
    kitvei::unregister(layout);
    tutor::unregister(layout);
    state::forget_everything(layout);
    let _ = std::fs::remove_dir(&layout.start_menu_dir);
    reporter.progress(0.6);

    reporter.step(Text::new("מוחק קבצים", "Deleting files"));
    delete_install_folder(layout);
    reporter.progress(1.0);
    Ok(())
}

fn check_prerequisites(layout: &Layout, wanted: &HashSet<Product>, reporter: &dyn Reporter) -> Result<(), Failure> {
    let core = wanted.iter().any(|p| p.needs_core_files());
    if core && !prereqs::dotnet48_installed() {
        prereqs::open_page(prereqs::DOTNET_PAGE);
        return Err(Failure::new(
            Text::new("נדרש ‎.NET Framework 4.8. דף ההורדה נפתח; התקינו ונסו שוב.", ".NET Framework 4.8 is required. The download page is open; install it and try again."),
            "dotnet48 missing",
        ));
    }
    if wanted.contains(&Product::WordAddin) && !prereqs::vsto_runtime_installed() {
        prereqs::open_page(prereqs::VSTO_PAGE);
        return Err(Failure::new(
            Text::new("נדרש רכיב VSTO של Office. דף ההורדה נפתח; התקינו ונסו שוב.", "The Office VSTO runtime is required. The download page is open; install it and try again."),
            "vsto runtime missing",
        ));
    }
    if core && !prereqs::webview2_installed() {
        reporter.step(Text::new("מתקין את רכיב התצוגה של Microsoft Edge", "Installing the Microsoft Edge WebView2 runtime"));
        prereqs::install_webview2(&layout.install_dir.join("setup-downloads"))
            .map_err(|e| Failure::new(Text::new("התקנת WebView2 נכשלה.", "Installing WebView2 failed."), e))?;
    }
    if wanted.contains(&Product::TypingTutor) && tutor::edge_exe().is_none() {
        return Err(Failure::new(Text::new("מאמן ההקלדה דורש את Microsoft Edge.", "The typing tutor needs Microsoft Edge."), "edge missing"));
    }
    Ok(())
}

fn close_apps(layout: &Layout, candidates: &[RunningApp], reporter: &dyn Reporter) -> Result<(), Failure> {
    // The sandbox writes nothing the real Word or app could be holding.
    if layout.sandboxed {
        return Ok(());
    }
    let open = apps::running(candidates);
    if open.is_empty() {
        return Ok(());
    }
    if !reporter.confirm_close(&open) {
        return Err(Failure::apps_open(Text::new("ההתקנה בוטלה.", "Setup was cancelled."), "user kept apps open"));
    }
    reporter.step(Text::new("סוגר תוכנות פתוחות", "Closing open programs"));
    if !apps::close(&open) {
        return Err(Failure::apps_open(
            Text::new("לא ניתן היה לסגור את וורד או את כתבי הקודש. סגרו אותם ונסו שוב.", "Word or Kitvei Hakodesh could not be closed. Close them and try again."),
            "apps did not exit",
        ));
    }
    Ok(())
}

fn copy_files(layout: &Layout, payload: Option<&Payload>, selection: Selection, reporter: &dyn Reporter) -> Result<(), Failure> {
    let missing_core = selection.core && !layout.file(crate::layout::VSTO_MANIFEST).exists();
    let missing_tutor = selection.tutor && !tutor::is_installed(layout);
    let Some(payload) = payload else {
        // The setup copy kept in the install folder carries no files: fine for changing
        // registrations, not for adding files that are not there.
        if missing_core || missing_tutor {
            return Err(Failure::new(
                Text::new("כדי להוסיף רכיב זה הריצו את קובץ ההתקנה המלא של כלי קודש.", "To add this part, run the full Klei Kodesh setup file."),
                "no payload",
            ));
        }
        return Ok(());
    };
    payload
        .extract(&layout.install_dir, selection, |f| reporter.progress(0.1 + f * 0.6))
        .map_err(|e| Failure::new(Text::new("העתקת הקבצים נכשלה.", "Copying the files failed."), e.to_string()))?;
    if selection.core {
        payload::purge_stale_files(&layout.install_dir);
    }
    Ok(())
}

fn register_products(layout: &Layout, wanted: &HashSet<Product>, options: &Options) -> Result<(), Failure> {
    let failed = |e: std::io::Error| Failure::new(Text::new("הרישום נכשל.", "Registration failed."), e.to_string());
    if wanted.contains(&Product::WordAddin) {
        addin::register(layout).map_err(failed)?;
    } else {
        addin::unregister(layout);
    }
    if wanted.contains(&Product::KitveiApp) {
        kitvei::register(layout, options.desktop_shortcut, options.open_with).map_err(failed)?;
    } else {
        kitvei::unregister(layout);
    }
    if wanted.contains(&Product::TypingTutor) {
        tutor::register(layout, options.desktop_shortcut).map_err(failed)?;
    } else {
        tutor::unregister(layout);
    }
    Ok(())
}

fn finish(layout: &Layout, payload: Option<&Payload>, wanted: &HashSet<Product>, english: bool) -> Result<(), Failure> {
    let failed = |e: std::io::Error| Failure::new(Text::new("שמירת פרטי ההתקנה נכשלה.", "Saving the install details failed."), e.to_string());
    // Keep a payload-free copy of this setup in the install folder for Modify / Uninstall.
    // The installed copy itself runs without a payload and is already in place.
    if let Some(payload) = payload {
        payload.write_bare_exe(&layout.installed_setup_exe()).map_err(failed)?;
    }
    state::save(layout, wanted, VERSION).map_err(failed)?;
    state::write_uninstall_entry(layout, VERSION, folder_size_kb(&layout.install_dir)).map_err(failed)?;
    state::set_addin_language(layout, english);
    let _ = std::fs::remove_dir_all(layout.install_dir.join("setup-downloads"));
    Ok(())
}

/// Neither the add-in nor the app is wanted any more: remove their files, keep the
/// tutor, the setup copy and the log.
fn remove_core_files(layout: &Layout) {
    let Ok(entries) = std::fs::read_dir(&layout.install_dir) else { return };
    for entry in entries.flatten() {
        let name = entry.file_name().to_string_lossy().to_string();
        if [TUTOR_DIR, SETUP_EXE, LOG_FILE].iter().any(|keep| name.eq_ignore_ascii_case(keep)) {
            continue;
        }
        let path = entry.path();
        let _ = if path.is_dir() { std::fs::remove_dir_all(&path) } else { std::fs::remove_file(&path) };
    }
}

/// The running uninstaller lives in the folder it removes, so the last delete is left to
/// a detached cmd.exe that waits for this process to exit.
fn delete_install_folder(layout: &Layout) {
    let dir = layout.install_dir.display().to_string();
    let running_from_inside = std::env::current_exe().is_ok_and(|exe| exe.starts_with(&layout.install_dir));
    if running_from_inside {
        let _ = crate::system::launch::detached_cmd(&format!("ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{dir}\""));
    } else {
        let _ = std::fs::remove_dir_all(&layout.install_dir);
    }
}

fn folder_size_kb(dir: &std::path::Path) -> u32 {
    fn size(dir: &std::path::Path) -> u64 {
        std::fs::read_dir(dir)
            .map(|entries| {
                entries
                    .flatten()
                    .map(|e| {
                        let p = e.path();
                        if p.is_dir() { size(&p) } else { e.metadata().map(|m| m.len()).unwrap_or(0) }
                    })
                    .sum()
            })
            .unwrap_or(0)
    }
    (size(dir) / 1024).min(u32::MAX as u64) as u32
}
