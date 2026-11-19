//! Build tool: packs the product files and appends them to the bare setup exe.
//!
//!   pack-payload --exe <bare kleikodesh-setup.exe> --core <add-in build output folder>
//!                --tutor <typing tutor dist folder> --out <KleiKodeshSetup.exe>
//!
//! Core files go to the archive root, the tutor under `TypingTutor\`.

#[path = "../payload/format.rs"]
mod format;

use std::fs;
use std::io::{self, Write};
use std::path::{Path, PathBuf};

/// Runtime data a build output may contain (an app was run from it) but must never ship:
/// caches, indexes, WebView2 profiles. Relative to the core folder.
const SKIP_FOLDERS: &[&str] = &[
    "BloomFilters",
    "filesystemindex",
    "logs",
    "webCache",
    "kiwix-webcache",
    r"KitveiHakodesh\cache",
    r"KitveiHakodesh\webcache",
    r"KitveiHakodeshService\FtsIndex",
    r"KitveiHakodeshService\CatalogTocIndex",
    r"KitveiHakodeshService\filesystemindex",
    r"KitveiHakodeshService\hb-cache",
    r"KitveiHakodeshService\convert-cache",
    r"KitveiHakodeshService\updates",
];

fn main() {
    if let Err(e) = run() {
        eprintln!("pack-payload: {e}");
        std::process::exit(1);
    }
}

fn run() -> io::Result<()> {
    let args: Vec<String> = std::env::args().collect();
    let arg = |name: &str| -> io::Result<PathBuf> {
        args.iter()
            .position(|a| a == name)
            .and_then(|i| args.get(i + 1))
            .map(PathBuf::from)
            .ok_or_else(|| io::Error::new(io::ErrorKind::InvalidInput, format!("missing {name}")))
    };
    let (exe, core, tutor, out) = (arg("--exe")?, arg("--core")?, arg("--tutor")?, arg("--out")?);

    let mut files = Vec::new();
    collect(&core, &core, "", &mut files)?;
    collect(&tutor, &tutor, "TypingTutor", &mut files)?;
    let total_bytes: u64 = files.iter().map(|(source, _)| fs::metadata(source).map(|m| m.len()).unwrap_or(0)).sum();

    // One handle from start to finish: copying the exe and reopening it to append lets
    // the antivirus grab the fresh exe in between (os error 33, lock violation).
    let mut target = fs::File::create(&out)?;
    let offset = io::copy(&mut fs::File::open(&exe)?, &mut target)?;
    // xz / LZMA2, preset 9 extreme, 128 MB dictionary: the smallest payload we measured
    // (37 MB vs 41 MB for zstd 19), still unpacks in under ten seconds. Packing takes a few
    // minutes, once, in CI. The setup's decoder reads any dictionary size.
    let mut options = xz2::stream::LzmaOptions::new_preset(9 | format::PRESET_EXTREME)?;
    options.dict_size(format::DICT_SIZE);
    // BCJ x86 first: turns relative call/jump targets in machine code into absolute ones,
    // so the 38 MB native-AOT service exe and the DLLs repeat more. ~0.85 MB smaller
    // payload; the decoder applies it from the stream header, nothing to change there.
    let mut filters = xz2::stream::Filters::new();
    filters.x86();
    filters.lzma2(&options);
    let stream = xz2::stream::Stream::new_stream_encoder(&filters, xz2::stream::Check::Crc32)?;
    let mut encoder = xz2::write::XzEncoder::new_stream(target, stream);
    {
        let mut tar = tar::Builder::new(&mut encoder);
        for (source, name) in &files {
            tar.append_path_with_name(source, name)?;
        }
        tar.finish()?;
    }
    let mut target = encoder.finish()?;
    let footer = format::Footer { offset, total_bytes, file_count: files.len() as u64 };
    target.write_all(&footer.to_bytes())?;

    let size = fs::metadata(&out)?.len();
    println!("packed {} files, {} MB -> {} ({} MB)", files.len(), total_bytes / 1_048_576, out.display(), size / 1_048_576);
    Ok(())
}

/// Files only a developer reads, never the running app: source maps (`*.map`, opened only
/// by browser dev tools) and IntelliSense docs (`X.xml` next to `X.dll`). About 12.5 MB raw.
fn is_dev_only(path: &Path) -> bool {
    let ext = path.extension().map(|e| e.to_string_lossy().to_ascii_lowercase()).unwrap_or_default();
    match ext.as_str() {
        "map" => true,
        "xml" => path.with_extension("dll").is_file(),
        _ => false,
    }
}

/// Every file under `dir`, as (source path, archive name with `prefix`).
fn collect(root: &Path, dir: &Path, prefix: &str, out: &mut Vec<(PathBuf, String)>) -> io::Result<()> {
    for entry in fs::read_dir(dir)? {
        let path = entry?.path();
        let relative = path.strip_prefix(root).unwrap().to_string_lossy().replace('/', "\\");
        if path.is_dir() {
            if SKIP_FOLDERS.iter().any(|s| relative.eq_ignore_ascii_case(s)) {
                continue;
            }
            collect(root, &path, prefix, out)?;
        } else {
            if is_dev_only(&path) {
                continue;
            }
            let name = if prefix.is_empty() { relative } else { format!("{prefix}\\{relative}") };
            out.push((path, name));
        }
    }
    Ok(())
}
