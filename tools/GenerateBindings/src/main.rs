use std::{error::Error, path::PathBuf};

fn main() -> Result<(), Box<dyn Error>> {
    let root = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../..");
    let intermediate = root.join("tools/GenerateBindings/target/lance.rs");
    std::fs::create_dir_all(intermediate.parent().unwrap())?;
    bindgen::Builder::default()
        .header(root.join("native/lance.h").to_string_lossy())
        .allowlist_function("lance_.*")
        .allowlist_type("Lance.*|Arrow.*")
        .default_enum_style(bindgen::EnumVariation::Rust { non_exhaustive: false })
        .layout_tests(false)
        .generate()?
        .write_to_file(&intermediate)?;
    csbindgen::Builder::default()
        .input_bindgen_file(intermediate)
        .csharp_dll_name("lance_c")
        .csharp_namespace("SunDoge.Lance.Interop")
        .csharp_class_name("NativeMethods")
        .csharp_class_accessibility("public")
        .always_included_types(["LanceWriteMode"])
        .generate_csharp_file(root.join("src/Lance/Interop/NativeMethods.g.cs"))?;
    // csbindgen leaves trailing spaces in blank XML documentation lines.
    let output = root.join("src/Lance/Interop/NativeMethods.g.cs");
    let generated = std::fs::read_to_string(&output)?;
    let normalized = generated.lines().map(str::trim_end).collect::<Vec<_>>().join("\n") + "\n";
    std::fs::write(output, normalized)?;
    Ok(())
}
