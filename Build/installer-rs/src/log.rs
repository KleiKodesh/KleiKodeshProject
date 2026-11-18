//! A plain text log beside the product (`setup.log` in the install folder).

use std::io::Write;
use std::path::PathBuf;
use std::sync::Mutex;

static FILE: Mutex<Option<PathBuf>> = Mutex::new(None);

pub fn init(path: PathBuf) {
    if let Some(dir) = path.parent() {
        let _ = std::fs::create_dir_all(dir);
    }
    *FILE.lock().unwrap() = Some(path);
    line(&format!("---- setup {} ----", env!("CARGO_PKG_VERSION")));
}

pub fn line(text: &str) {
    let Some(path) = FILE.lock().unwrap().clone() else { return };
    if let Ok(mut file) = std::fs::OpenOptions::new().create(true).append(true).open(path) {
        let _ = writeln!(file, "{}  {text}", timestamp());
    }
}

fn timestamp() -> String {
    let secs = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0);
    format!("{secs}")
}
