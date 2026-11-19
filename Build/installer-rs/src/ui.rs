//! The window: fills the screens from the engine's view of the machine and turns button
//! presses into engine requests. The engine runs on a worker thread; it talks back
//! through `UiReporter`, which hops onto the UI thread for every change.

use std::cell::RefCell;
use std::collections::HashSet;
use std::rc::Rc;
use std::sync::mpsc;
use std::sync::{Arc, Mutex};

use slint::{ComponentHandle, Model, ModelRc, SharedString, VecModel};

use crate::i18n::Text;
use crate::install::addin_settings::{AddinSettings, TOOLS};
use crate::install::{self, apps::RunningApp, Action, Options, Request};
use crate::layout::{Layout, KITVEI_EXE};
use crate::payload::Payload;
use crate::products::{Product, ALL};
use crate::system::{launch, registry};
use crate::{AppWindow, Choice, Installer, Lang, Page};

const MACROS_TITLE: Text = Text::new("התקן פקודות מאקרו (VBA)", "Install VBA macros");

type Products = Rc<VecModel<crate::ProductRow>>;

/// The Word add-in options: what is saved, and the copy being edited on screen.
struct AddinOptions {
    saved: RefCell<AddinSettings>,
    tools: Rc<VecModel<Choice>>,
    websites: Rc<VecModel<Choice>>,
    /// One row: install the VBA macros template.
    macros: Rc<VecModel<Choice>>,
}

impl AddinOptions {
    fn new(saved: AddinSettings) -> Rc<Self> {
        let options = Rc::new(Self { saved: RefCell::new(saved), tools: Rc::default(), websites: Rc::default(), macros: Rc::default() });
        options.show_saved(false);
        options
    }

    /// Puts the saved options back on screen (open, Cancel, language change keeps edits).
    fn show_saved(&self, english: bool) {
        let saved = self.saved.borrow();
        self.tools.set_vec(
            TOOLS.iter().zip(&saved.tools_visible).map(|((_, name), on)| Choice { title: name.get(english).into(), on: *on }).collect::<Vec<_>>(),
        );
        self.macros.set_vec(vec![Choice { title: MACROS_TITLE.get(english).into(), on: saved.macros }]);
        self.websites.set_vec(saved.sites.iter().map(|s| Choice { title: s.name.as_str().into(), on: s.visible }).collect::<Vec<_>>());
    }

    fn retitle(&self, english: bool) {
        if let Some(mut row) = self.macros.row_data(0) {
            row.title = MACROS_TITLE.get(english).into();
            self.macros.set_row_data(0, row);
        }
        for (i, (_, name)) in TOOLS.iter().enumerate() {
            if let Some(mut row) = self.tools.row_data(i) {
                row.title = name.get(english).into();
                self.tools.set_row_data(i, row);
            }
        }
    }

    /// The options as currently on screen.
    fn edited(&self) -> AddinSettings {
        let mut settings = self.saved.borrow().clone();
        settings.tools_visible = self.tools.iter().map(|c| c.on).collect();
        settings.macros = self.macros.row_data(0).is_some_and(|c| c.on);
        for (site, choice) in settings.sites.iter_mut().zip(self.websites.iter()) {
            site.visible = choice.on;
        }
        settings
    }
}

fn show_notes(ui: &AppWindow, english: bool) {
    use crate::release_notes::LineKind;
    let lines: Vec<crate::NoteLine> = crate::release_notes::lines(english)
        .into_iter()
        .map(|l| crate::NoteLine {
            kind: match l.kind {
                LineKind::Section => 0,
                LineKind::Area => 1,
                LineKind::Change => 2,
            },
            text: l.text.into(),
        })
        .collect();
    ui.global::<Installer>().set_notes(ModelRc::new(VecModel::from(lines)));
}

/// The site list is only offered while the Websites tool is on the ribbon.
fn show_websites_if_tool_on(ui: &AppWindow, addin: &AddinOptions) {
    let index = TOOLS.iter().position(|(id, _)| *id == "WebSites").unwrap_or(usize::MAX);
    let on = addin.tools.row_data(index).is_some_and(|row| row.on);
    ui.global::<Installer>().set_websites_tool_on(on);
}

fn flip(model: &VecModel<Choice>, i: i32) {
    if let Some(mut row) = model.row_data(i as usize) {
        row.on = !row.on;
        model.set_row_data(i as usize, row);
    }
}

pub struct Context {
    pub layout: Layout,
    pub payload: Option<Arc<Payload>>,
    pub installed: HashSet<Product>,
    pub start_in_manage: bool,
}

pub fn run(ctx: Context, english: bool) -> Result<(), slint::PlatformError> {
    // Create the native window light: otherwise winit opens it with the Windows theme
    // and the title bar starts dark until the first `set_theme`.
    slint::BackendSelector::new()
        .with_winit_window_attributes_hook(|attrs| attrs.with_theme(Some(slint::winit_030::winit::window::Theme::Light)))
        .select()?;
    let ui = AppWindow::new()?;
    ui.global::<Lang>().set_en(english);
    let st = ui.global::<Installer>();

    let word_installed = registry::hklm_exists(r"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Winword.exe");
    let fresh = ctx.installed.is_empty();
    // The landing page (install or update) offers every program ticked; Modify shows what is installed.
    let landing = !(!fresh && (ctx.start_in_manage || ctx.payload.is_none()));
    let products: Products = Rc::new(VecModel::from(
        ALL.iter()
            .map(|&p| {
                let available = p != Product::WordAddin || word_installed;
                crate::ProductRow {
                    id: p.id().into(),
                    selected: available && (landing || ctx.installed.contains(&p)),
                    installed: ctx.installed.contains(&p),
                    available,
                    ..Default::default()
                }
            })
            .collect::<Vec<_>>(),
    ));
    st.set_products(ModelRc::from(products.clone()));
    st.set_version(install::VERSION.into());
    st.set_updating(!fresh && ctx.payload.is_some());
    st.set_page(if landing { Page::Welcome } else { Page::Manage });
    let current = Options::current(&ctx.layout, english);
    st.set_desktop_shortcut(current.desktop_shortcut);
    st.set_file_associations(current.open_with);
    st.set_installed_desktop_shortcut(current.desktop_shortcut);
    st.set_installed_file_associations(current.open_with);

    let addin = AddinOptions::new(AddinSettings::load(&ctx.layout));
    addin.show_saved(english);
    st.set_ribbon_tools(ModelRc::from(addin.tools.clone()));
    st.set_websites(ModelRc::from(addin.websites.clone()));
    st.set_macros(ModelRc::from(addin.macros.clone()));
    show_websites_if_tool_on(&ui, &addin);
    retranslate(&ui, &products);
    st.set_has_notes(crate::release_notes::any());
    show_notes(&ui, english);

    let ctx = Rc::new(ctx);
    let answer: Arc<Mutex<Option<mpsc::Sender<bool>>>> = Arc::default();
    wire(&ui, &products, &ctx, &answer, &addin);

    ui.show()?;
    {
        let w = ui.as_weak();
        slint::Timer::single_shot(std::time::Duration::ZERO, move || {
            if let Some(ui) = w.upgrade() {
                set_title_bar_dark(&ui, false);
            }
        });
    }
    slint::run_event_loop()
}

fn wire(ui: &AppWindow, products: &Products, ctx: &Rc<Context>, answer: &Arc<Mutex<Option<mpsc::Sender<bool>>>>, addin: &Rc<AddinOptions>) {
    let st = ui.global::<Installer>();
    {
        let (a, w) = (addin.clone(), ui.as_weak());
        st.on_toggle_tool(move |i| {
            flip(&a.tools, i);
            if let Some(ui) = w.upgrade() {
                show_websites_if_tool_on(&ui, &a);
            }
        });
        let a = addin.clone();
        st.on_toggle_website(move |i| flip(&a.websites, i));
        let a = addin.clone();
        st.on_toggle_macros(move || flip(&a.macros, 0));
        let (a, w) = (addin.clone(), ui.as_weak());
        st.on_discard_addin_options(move || {
            if let Some(ui) = w.upgrade() {
                a.show_saved(ui.global::<Lang>().get_en());
                show_websites_if_tool_on(&ui, &a);
            }
        });
        let (a, layout) = (addin.clone(), ctx.layout.clone());
        st.on_save_addin_options(move || {
            let edited = a.edited();
            match edited.save(&layout) {
                Ok(()) => *a.saved.borrow_mut() = edited,
                Err(e) => crate::log::line(&format!("saving add-in options failed: {e}")),
            }
        });
    }
    {
        let (w, p) = (ui.as_weak(), products.clone());
        st.on_toggle_product(move |i| {
            let i = i as usize;
            if let Some(mut row) = p.row_data(i) {
                if row.available {
                    row.selected = !row.selected;
                    p.set_row_data(i, row);
                }
            }
            if let Some(ui) = w.upgrade() {
                refresh(&ui, &p);
            }
        });
    }
    {
        let (w, p, a) = (ui.as_weak(), products.clone(), addin.clone());
        st.on_language_changed(move |english| {
            if let Some(ui) = w.upgrade() {
                retranslate(&ui, &p);
                a.retitle(english);
                show_notes(&ui, english);
            }
        });
    }
    let start = |action: Action| {
        let (w, p, ctx, answer, a) = (ui.as_weak(), products.clone(), ctx.clone(), answer.clone(), addin.clone());
        move || {
            if let Some(ui) = w.upgrade() {
                start_engine(&ui, &p, &ctx, &answer, action, a.edited());
            }
        }
    };
    st.on_install(start(Action::Apply));
    st.on_apply_changes(start(Action::Apply));
    st.on_repair(start(Action::Repair));
    st.on_uninstall(start(Action::Uninstall));
    {
        let answer = answer.clone();
        st.on_close_apps(move |ok| {
            if let Some(tx) = answer.lock().unwrap().take() {
                let _ = tx.send(ok);
            }
        });
    }
    {
        let layout = ctx.layout.clone();
        st.on_launch(move |id| launch_product(&layout, id.as_str()));
    }
    st.on_quit(|| {
        let _ = slint::quit_event_loop();
    });
    {
        let w = ui.as_weak();
        st.on_theme_changed(move |dark| {
            if let Some(ui) = w.upgrade() {
                set_title_bar_dark(&ui, dark);
            }
        });
    }
}

fn start_engine(
    ui: &AppWindow,
    products: &Products,
    ctx: &Rc<Context>,
    answer: &Arc<Mutex<Option<mpsc::Sender<bool>>>>,
    action: Action,
    addin_settings: AddinSettings,
) {
    let st = ui.global::<Installer>();
    let english = ui.global::<Lang>().get_en();
    let wanted: HashSet<Product> = products.iter().filter(|p| p.selected).filter_map(|p| Product::from_id(&p.id)).collect();
    let request = Request {
        action,
        products: wanted,
        options: Options { desktop_shortcut: st.get_desktop_shortcut(), open_with: st.get_file_associations(), english },
        clean: action == Action::Apply && st.get_clean_install(),
        addin_settings: Some(addin_settings),
    };
    let from_manage = st.get_page() == Page::Manage;
    st.set_confirm_uninstall(false);
    st.set_apps_to_close(SharedString::new());
    st.set_progress(0.0);
    st.set_step_text(SharedString::new());
    st.set_progress_title(progress_title(action, st.get_updating()).get(english).into());
    st.set_page(Page::Progress);

    let reporter = UiReporter { ui: ui.as_weak(), english, answer: answer.clone() };
    let (layout, payload) = (ctx.layout.clone(), ctx.payload.clone());
    let w = ui.as_weak();
    std::thread::spawn(move || {
        let result = install::run(&layout, payload.as_deref(), &request, &reporter);
        let _ = w.upgrade_in_event_loop(move |ui| finish(&ui, &request, result, english, from_manage));
    });
}

fn progress_title(action: Action, updating: bool) -> Text {
    match action {
        Action::Apply if updating => Text::new("מעדכן את כלי קודש", "Updating Klei Kodesh"),
        Action::Apply => Text::new("מתקין את כלי קודש", "Installing Klei Kodesh"),
        Action::Repair => Text::new("מתקן את ההתקנה", "Repairing"),
        Action::Uninstall => Text::new("מסיר את כלי קודש", "Removing Klei Kodesh"),
    }
}

fn finish(ui: &AppWindow, request: &Request, result: Result<(), install::Failure>, english: bool, from_manage: bool) {
    let st = ui.global::<Installer>();
    let products = st.get_products();
    // The Modify screen must show the new state when the user goes back to it.
    if result.is_ok() && request.action != Action::Uninstall {
        for i in 0..products.row_count() {
            let mut row = products.row_data(i).unwrap();
            row.installed = Product::from_id(&row.id).is_some_and(|p| request.products.contains(&p));
            products.set_row_data(i, row);
        }
        st.set_installed_desktop_shortcut(request.options.desktop_shortcut);
        st.set_installed_file_associations(request.options.open_with);
        st.set_has_changes(false);
    }
    st.set_back_to_manage(from_manage && request.action != Action::Uninstall);
    let launch: Vec<crate::ProductRow> = match (&result, request.action) {
        (Ok(()), Action::Apply | Action::Repair) => products
            .iter()
            .filter(|p| Product::from_id(&p.id).is_some_and(|prod| request.products.contains(&prod)))
            .collect(),
        _ => vec![],
    };
    st.set_launchables(ModelRc::new(VecModel::from(launch)));
    let (title, text) = match (&result, request.action) {
        (Err(failure), _) => (Text::new("ההתקנה לא הושלמה", "Setup did not finish"), failure.text),
        (Ok(()), Action::Uninstall) => (Text::new("כלי קודש הוסר", "Klei Kodesh was removed"), Text::new("תודה שהשתמשתם בכלי קודש.", "Thank you for using Klei Kodesh.")),
        (Ok(()), Action::Repair) => (Text::new("התיקון הושלם", "Repair complete"), Text::new("כל הרכיבים נבדקו ונרשמו מחדש.", "Everything was checked and registered again.")),
        (Ok(()), Action::Apply) => (Text::new("הכל מוכן", "You're all set"), Text::new("כלי קודש מופיע גם בתפריט ההתחלה.", "Klei Kodesh is also in the Start menu.")),
    };
    st.set_failed(result.is_err());
    st.set_done_title(title.get(english).into());
    st.set_done_text(text.get(english).into());
    st.set_page(Page::Done);
}

struct UiReporter {
    ui: slint::Weak<AppWindow>,
    english: bool,
    answer: Arc<Mutex<Option<mpsc::Sender<bool>>>>,
}

impl install::Reporter for UiReporter {
    fn step(&self, text: Text) {
        crate::log::line(text.en);
        let text = text.get(self.english);
        let _ = self.ui.upgrade_in_event_loop(move |ui| ui.global::<Installer>().set_step_text(text.into()));
    }

    fn progress(&self, fraction: f32) {
        let _ = self.ui.upgrade_in_event_loop(move |ui| ui.global::<Installer>().set_progress(fraction));
    }

    fn confirm_close(&self, apps: &[RunningApp]) -> bool {
        let (tx, rx) = mpsc::channel();
        *self.answer.lock().unwrap() = Some(tx);
        let names: Vec<&str> = apps.iter().map(|a| a.name().get(self.english)).collect();
        let joined = names.join(if self.english { ", " } else { ", " });
        let _ = self.ui.upgrade_in_event_loop(move |ui| ui.global::<Installer>().set_apps_to_close(joined.into()));
        let ok = rx.recv().unwrap_or(false);
        let _ = self.ui.upgrade_in_event_loop(|ui| ui.global::<Installer>().set_apps_to_close(SharedString::new()));
        ok
    }
}

/// Refill every language-dependent text, keeping the selection.
fn retranslate(ui: &AppWindow, products: &Products) {
    let en = ui.global::<Lang>().get_en();
    for i in 0..products.row_count() {
        let mut row = products.row_data(i).unwrap();
        if let Some(p) = Product::from_id(&row.id) {
            row.title = p.title().get(en).into();
            row.description = p.description().get(en).into();
            row.size = p.size().get(en).into();
            row.note = p.unavailable_note().get(en).into();
            products.set_row_data(i, row);
        }
    }
    refresh(ui, products);
}

/// Recompute everything derived from the selection.
fn refresh(ui: &AppWindow, products: &Products) {
    let st = ui.global::<Installer>();
    let en = ui.global::<Lang>().get_en();
    let chosen: Vec<crate::ProductRow> = products.iter().filter(|p| p.selected).collect();
    let summary = if chosen.is_empty() {
        Text::new("לא נבחר דבר להתקנה", "Nothing selected").get(en).to_string()
    } else {
        let names: Vec<&str> = chosen.iter().map(|p| p.title.as_str()).collect();
        format!("{}{}", Text::new("יותקנו: ", "Will install: ").get(en), names.join(" · "))
    };
    st.set_summary(summary.into());
    st.set_any_selected(!chosen.is_empty());
    st.set_kitvei_selected(chosen.iter().any(|p| p.id == Product::KitveiApp.id()));
    st.set_addin_selected(chosen.iter().any(|p| p.id == Product::WordAddin.id()));
    st.set_has_changes(products.iter().any(|p| p.selected != p.installed));
}

fn launch_product(layout: &Layout, id: &str) {
    match Product::from_id(id) {
        Some(Product::WordAddin) => {
            let _ = launch::detached_cmd("start \"\" winword.exe");
        }
        Some(Product::KitveiApp) => {
            let _ = launch::open(&layout.file(KITVEI_EXE), &[]);
        }
        Some(Product::TypingTutor) => {
            if let Some(edge) = install::tutor::edge_exe() {
                let url = format!("--app=file:///{}", install::tutor::page(layout).display().to_string().replace('\\', "/"));
                let _ = launch::open(&edge, &[&url]);
            }
        }
        None => {}
    }
}

/// Title bar follows the installer's own theme, not Windows'.
fn set_title_bar_dark(ui: &AppWindow, dark: bool) {
    use slint::winit_030::{winit::window::Theme, WinitWindowAccessor};
    ui.window().with_winit_window(|w| w.set_theme(Some(if dark { Theme::Dark } else { Theme::Light })));
}
