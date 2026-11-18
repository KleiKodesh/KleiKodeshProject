//! Reading the payload appended to this exe and writing it into the install folder.

pub mod format;
mod rules;

use std::fs::{self, File};
use std::io::{self, Read, Seek, SeekFrom};
use std::path::{Path, PathBuf};
use std::time::Duration;

use format::{Footer, FOOTER_LEN};
pub use rules::{is_tutor_file, purge_stale_files};

pub struct Payload {
    exe: PathBuf,
    footer: Footer,
}

/// What to take out of the archive.
#[derive(Clone, Copy)]
pub struct Selection {
    pub core: bool,
    pub tutor: bool,
}

impl Payload {
    /// The payload inside the running exe, if this exe carries one (the copy kept in the
    /// install folder for Modify / Uninstall does not).
    pub fn of_current_exe() -> Option<Self> {
        let exe = std::env::current_exe().ok()?;
        Self::open(&exe)
    }

    pub fn open(exe: &Path) -> Option<Self> {
        let mut file = File::open(exe).ok()?;
        let len = file.metadata().ok()?.len();
        if len < FOOTER_LEN as u64 {
            return None;
        }
        file.seek(SeekFrom::End(-(FOOTER_LEN as i64))).ok()?;
        let mut bytes = [0u8; FOOTER_LEN];
        file.read_exact(&mut bytes).ok()?;
        let footer = Footer::from_bytes(&bytes)?;
        (footer.offset < len).then(|| Self { exe: exe.to_path_buf(), footer })
    }

    /// Copies just the setup program (no payload) to `dest`: the small copy kept in the
    /// install folder that "Apps > Modify / Uninstall" runs.
    pub fn write_bare_exe(&self, dest: &Path) -> io::Result<()> {
        let source = File::open(&self.exe)?;
        let mut target = File::create(dest)?;
        io::copy(&mut source.take(self.footer.offset), &mut target)?;
        Ok(())
    }

    /// Extracts the selected parts into `install_dir`, reporting progress 0..=1.
    /// Files that hold user data are never overwritten (see `rules`).
    pub fn extract(&self, install_dir: &Path, selection: Selection, mut progress: impl FnMut(f32)) -> io::Result<()> {
        let mut file = File::open(&self.exe)?;
        file.seek(SeekFrom::Start(self.footer.offset))?;
        let compressed = file.take(self.footer_payload_len());
        let decoder = xz2::read::XzDecoder::new(compressed);
        let mut archive = tar::Archive::new(decoder);

        let total = self.footer.total_bytes.max(1) as f32;
        let mut done = 0u64;
        for entry in archive.entries()? {
            let mut entry = entry?;
            let relative = entry.path()?.into_owned();
            let size = entry.header().size()?;
            let wanted = if is_tutor_file(&relative) { selection.tutor } else { selection.core };
            if wanted && entry.header().entry_type().is_file() {
                let target = install_dir.join(&relative);
                if !(rules::keep_existing(&relative) && target.exists()) {
                    write_entry(&mut entry, &target)?;
                }
            }
            done += size;
            progress(done as f32 / total);
        }
        Ok(())
    }

    fn footer_payload_len(&self) -> u64 {
        // Everything between the payload start and the footer.
        let exe_len = fs::metadata(&self.exe).map(|m| m.len()).unwrap_or(0);
        exe_len.saturating_sub(self.footer.offset + FOOTER_LEN as u64)
    }
}

/// Writes one file. A locked file (the service exe while the service still holds it) is
/// retried for a few seconds, then left as it is: an older file beats a failed install.
fn write_entry(entry: &mut impl Read, target: &Path) -> io::Result<()> {
    if let Some(dir) = target.parent() {
        fs::create_dir_all(dir)?;
    }
    let mut bytes = Vec::new();
    entry.read_to_end(&mut bytes)?;
    for attempt in 0..6 {
        match fs::write(target, &bytes) {
            Ok(()) => return Ok(()),
            Err(e) if is_sharing_violation(&e) && attempt < 5 => std::thread::sleep(Duration::from_millis(500)),
            Err(e) if is_sharing_violation(&e) => {
                crate::log::line(&format!("left in use, not replaced: {}", target.display()));
                return Ok(());
            }
            Err(e) => return Err(e),
        }
    }
    Ok(())
}

fn is_sharing_violation(e: &io::Error) -> bool {
    // ERROR_SHARING_VIOLATION (32) / ERROR_LOCK_VIOLATION (33) / access denied on a running exe (5).
    matches!(e.raw_os_error(), Some(32) | Some(33) | Some(5))
}
