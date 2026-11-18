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

    let config = slint_build::CompilerConfiguration::new().with_style("fluent".into());
    slint_build::compile_with_config("ui/app.slint", config).unwrap();
}
