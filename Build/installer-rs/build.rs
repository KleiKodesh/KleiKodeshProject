fn main() {
    // A new version or notes file (both set by Build/build.ps1) must rebuild the exe.
    println!("cargo:rerun-if-env-changed=KK_VERSION");
    println!("cargo:rerun-if-env-changed=KK_NOTES");

    // The release notes (release-notes.json from the release workflow) are built in for
    // the "What's new" screen. Without them - dev and debug builds - an empty list.
    let out = std::path::Path::new(&std::env::var("OUT_DIR").unwrap()).join("release-notes.json");
    let notes = std::env::var("KK_NOTES").ok().filter(|p| !p.is_empty());
    match notes {
        Some(path) => {
            println!("cargo:rerun-if-changed={path}");
            std::fs::copy(&path, &out).expect("KK_NOTES file not readable");
        }
        None => std::fs::write(&out, r#"{"sections":[]}"#).unwrap(),
    }

    // The exe's own icon and version details (what Explorer and Properties show).
    let version = std::env::var("KK_VERSION").ok().filter(|v| !v.is_empty()).unwrap_or_else(|| "0.0.0".into());
    let mut res = winresource::WindowsResource::new();
    res.set_icon("assets/kleikodesh.ico")
        .set("ProductName", "כלי קודש")
        .set("FileDescription", "התקנת כלי קודש")
        .set("CompanyName", "צוות כלי קודש")
        .set("ProductVersion", &version)
        .set("FileVersion", &version);
    if let Err(e) = res.compile() {
        println!("cargo:warning=icon / version resource not embedded: {e}");
    }

    let config = slint_build::CompilerConfiguration::new().with_style("fluent".into());
    slint_build::compile_with_config("ui/app.slint", config).unwrap();
}
