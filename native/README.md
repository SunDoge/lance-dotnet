# Native ABI

`lance.h` and `LICENSE` are unmodified files from
https://github.com/lance-format/lance-c/tree/v0.1.9.

`tools/GenerateBindings` parses this header using bindgen 0.72.1 and csbindgen
1.9.6. Cargo.lock pins the generator's dependency graph. Its output is checked
in at `src/Lance/Interop/NativeMethods.g.cs`.

`tools/fetch_native.py` downloads the corresponding upstream shared library,
verifies the release SHA256 checksum, and places only the shared library under
`src/Lance/runtimes/<rid>/native`. Native binaries and generator build outputs
are ignored by source control. No private fork or ABI extension is required.

When upgrading, update the header, native version in the fetch script and
LanceRuntime.NativeVersion together, regenerate bindings, then rerun native
symbol, Arrow interoperability and dataset integration tests.
