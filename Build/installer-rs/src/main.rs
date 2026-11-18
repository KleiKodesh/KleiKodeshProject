//! Klei Kodesh setup: one native exe, no runtime.
//!
//!   KleiKodeshSetup.exe                 install / update (window)
//!   KleiKodeshSetup.exe --manage        modify, repair or remove (window)
//!   KleiKodeshSetup.exe --uninstall     remove (window, opens on the modify screen)
//!   KleiKodeshSetup.exe --silent        install / update the installed products, no window
//!   KleiKodeshSetup.exe --silent --uninstall
//!   --lang he|en                        UI language (default: Windows display language)
//!   --sandbox <dir>                     everything under <dir> + a test registry key
//!   --close-apps                        silent mode may close Word / the app
//!
//! Silent exit codes: 0 done, 1 failed, 2 programs open (pass --close-apps to close them).
#![windows_subsystem = "windows"]

mod i18n;
mod install;
mod layout;
mod log;
mod payload;
mod products;
mod system;
mod ui;

use std::collections::HashSet;
use std::path::PathBuf;
use std::sync::Arc;

use install::{apps::RunningApp, Action, Options, Request};
use layout::Layout;
use payload::Payload;
use products::Product;

slint::include_modules!();

fn main() {
    let args: Vec<String> = std::env::args().collect();
    let flag = |name: &str| args.iter().any(|a| a == name);
    let value = |name: &str| args.iter().position(|a| a == name).and_then(|i| args.get(i + 1)).cloned();

    let layout = match value("--sandbox") {
        Some(dir) => Layout::sandbox(&PathBuf::from(dir)),
        None => Layout::real(),
    };
    log::init(layout.install_dir.join(layout::LOG_FILE));
    // Release builds abort on panic with no window to show it: keep the reason in the log.
    std::panic::set_hook(Box::new(|info| log::line(&format!("panic: {info}"))));
    let english = i18n::start_in_english(&args);
    let payload = Payload::of_current_exe().map(Arc::new);
    let installed = install::state::installed(&layout);

    if flag("--silent") {
        let code = run_silent(&layout, payload.as_deref(), &installed, flag("--uninstall"), flag("--close-apps"), english);
        std::process::exit(code);
    }

    let ctx = ui::Context { layout, payload, installed, start_in_manage: flag("--manage") || flag("--uninstall") };
    if let Err(e) = ui::run(ctx, english) {
        log::line(&format!("ui error: {e}"));
    }
}

/// No window: updates exactly what is installed (or installs everything on a fresh machine).
fn run_silent(layout: &Layout, payload: Option<&Payload>, installed: &HashSet<Product>, uninstall: bool, close_apps: bool, english: bool) -> i32 {
    let products = if installed.is_empty() { products::ALL.into_iter().collect() } else { installed.clone() };
    let request = Request {
        action: if uninstall { Action::Uninstall } else { Action::Apply },
        products,
        options: Options { desktop_shortcut: false, open_with: false, english },
    };
    let reporter = SilentReporter { close_apps };
    match install::run(layout, payload, &request, &reporter) {
        Ok(()) => 0,
        Err(failure) if failure.apps_open => 2,
        Err(_) => 1,
    }
}

struct SilentReporter {
    close_apps: bool,
}

impl install::Reporter for SilentReporter {
    fn step(&self, text: i18n::Text) {
        log::line(text.en);
    }
    fn progress(&self, _: f32) {}
    fn confirm_close(&self, _: &[RunningApp]) -> bool {
        self.close_apps
    }
}
