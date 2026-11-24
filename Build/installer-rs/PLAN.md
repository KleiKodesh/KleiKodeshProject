# New installer — Rust + Slint (plan)

Designed from scratch. Nothing is copied from the WPF installer or the NSIS script.
They are kept only as a list of requirements (what happens on disk and in the registry),
never as code or UI to port.

## Goals
- One native exe (`KleiKodeshSetup.exe`). No .NET, no NSIS, no runtime of any kind.
- Per-user install. Never elevate.
- Products the user can pick, in any combination:
  1. **Klei Kodesh Word add-in** (VSTO). Includes Kitvei Hakodesh inside Word.
  2. **Kitvei Hakodesh as a separate app**: standalone window, Start-menu shortcut and file associations.
     It can be installed with the add-in, or alone.
  3. **Hebrew Typing Tutor**: offline local copy with its own shortcut.
- The same exe handles install, modify (add or remove a product), repair, update and uninstall.
- Looks like Kitvei Hakodesh: same palette tokens (`--accent-color` #0078d4 / #60cdff dark,
  `--bg-primary`, `--bg-secondary`), light and dark following Windows, RTL Hebrew first, English as the second locale.

## Why this removes NSIS
NSIS does three jobs today: it extracts the payload, checks prerequisites and registers the uninstaller.
The Rust exe takes over all three:
- **Payload.** A solid compressed archive (zstd or lzma) is appended to the exe or embedded as a resource,
  and streamed straight to the target folder. It is never fully unpacked to %TEMP% first.
- **Prereqs.** Read the registry directly: .NET Framework 4.8 release key, VSTO runtime and WebView2 Evergreen.
  A missing piece is installed silently, per user where the vendor allows it.
- **Uninstall.** The exe copies itself into the install folder and writes
  `HKCU\...\Uninstall\KleiKodesh` with `UninstallString = "<self> --uninstall"`.

## Architecture
```
installer-rs/
  Cargo.toml                 slint, windows-rs, zstd, sha2, serde
  ui/                        .slint files only — no logic
    theme.slint              KH tokens, light/dark, RTL
    pages/ welcome · choose-products · options · progress · done · manage
  src/
    main.rs                  CLI args → mode (install/manage/uninstall/silent update)
    products/                one module per product, all with the same trait:
      word_addin.rs            detect · install · remove · repair
      kitvei_app.rs
      typing_tutor.rs
    payload.rs               read embedded archive, per-product sections, hash check
    prereqs.rs               .NET 4.8 / VSTO / WebView2 checks + silent bootstrap
    running_apps.rs          detect and close Word / KH service / KH app before file swaps
    registry.rs              HKCU only: add-in keys, uninstall entry, file associations
    shortcuts.rs             Start menu / desktop (IShellLink via windows-rs)
    plan.rs                  the selected products turn into an ordered step list; progress comes from steps
  build.rs                   embeds icon, manifest (asInvoker, PerMonitorV2, UTF-8)
```
Key rules:
- **One step list.** The UI only shows what the plan produces. The same plan runs silently for `--update`.
- **Per-product payload sections.** Selecting fewer products extracts less. Updates can skip unchanged sections (hash compare).
- **Transactional.** Every step has an undo. A failure rolls back to the previous working install.
- **Logs** go to a file beside the app (install folder), never elsewhere.

## Screens
1. **Welcome**: logo, version, one big "Install" button and a small "Customize" link.
2. **Choose products**: three cards with toggles (add-in · KH app · typing tutor), each with its size and a short description.
   If Word is missing, the add-in card is disabled and says why.
3. **Options**: only for chosen products (install folder, desktop shortcut, file associations). Can be skipped.
4. **Progress**: one smooth bar plus the current step in plain words. A "close Word" prompt appears inline, never as a modal.
5. **Done**: launch buttons for whatever was installed.
6. **Manage** (opens when already installed): modify / repair / uninstall, with the same cards showing installed state.

## Build and CI
- `cargo build --release --target x86_64-pc-windows-msvc`, static CRT (`+crt-static`), one exe.
- `release.yml`: build the payloads (VSTO, KH app, service AOT, tutor `dist`) → pack the sections → append them to the exe → sign → upload.
  The NSIS step and the WPF installer csproj go away once the new installer is at parity.
- The existing self-update flow switches to `KleiKodeshSetup.exe --update --silent`.

## Phases
1. Skeleton: Slint window with the KH theme, RTL, light/dark, all screens on fake data.
2. Payload format + packer (a Rust bin run in CI) + streaming extraction.
3. Typing tutor product (simplest, end to end).
4. Kitvei Hakodesh standalone app product.
5. Word add-in product (prereqs, VSTO registration, close Word, service handling).
6. Manage / repair / uninstall / silent update.
7. Switch CI to the new installer. Retire NSIS and the WPF installer.

## Decisions (2026-11-18)
- **Typing tutor host:** its own app window, with no extra exe. The single-file `dist/index.html` is copied next to the app, and a shortcut runs `msedge --app=file:///…/index.html`, so it works offline. Edge's silent PWA install needs admin policy, so it is not used.
- **Existing installs:** the new installer takes over the current `HKCU\...\Uninstall\KleiKodesh` entry by overwriting it in place. There is no migration code for persisted data.
- **Signing:** keep the current self-signed certificate flow (`Update-SigningCertificate`). It signs the setup exe and the tutor exe.
