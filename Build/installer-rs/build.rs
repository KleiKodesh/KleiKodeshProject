fn main() {
    // A new KK_VERSION (stamped by build.ps1) must rebuild the exe.
    println!("cargo:rerun-if-env-changed=KK_VERSION");
    let config = slint_build::CompilerConfiguration::new().with_style("fluent".into());
    slint_build::compile_with_config("ui/app.slint", config).unwrap();
}
