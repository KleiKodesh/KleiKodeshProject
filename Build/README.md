# Build

One native setup exe (`installer-rs/`, Rust + Slint) and one build script.

## Building

`Build\build.bat`:

1. **Auto**: starts the Release workflow on GitHub. Gemini picks the version and writes the
   notes, then it builds and publishes. Nothing is built locally.
2. **Debug**: local build as version 0.0.0, never released, output in `Build\releases\`.

`build.ps1 -Version X.Y.Z [-Notes release-notes.json]` is what both run:

1. Deletes every `bin\` and `obj\` folder in the repository (build output only: git-tracked
   folders and Rust `src\bin` are kept).
2. Restores packages; stamps the version (put back afterwards); fresh add-in signing key.
3. Builds the Vue frontend, typing tutor, Kitvei Hakodesh service (AOT), Word add-in and
   portable app (Release).
4. Builds the setup exe with the release notes built in, and packs the product files into it.

Output: `KleiKodeshSetup-vX.Y.Z.exe` and `KitveiHakodeshPortable-vX.Y.Z.zip`.

## Release workflow (`.github/workflows/release.yml`)

No inputs. Daily check plus manual runs; never on Shabbat or Yom Tov, the day before or the
night after.

1. **gate**: decides whether to release (a week since the last one and new commits, or a
   manual run).
2. **plan**: `.github/scripts/ai-release-plan.ps1` writes `release-notes.json` and the
   version bump.
3. **release**: computes the version, runs `build.ps1`, turns the notes into the release
   text (`.github/scripts/notes-to-markdown.ps1`), publishes, and starts the website,
   Drive and issue-check follow-ups.

## Release notes

`release-notes.json` is the only notes format: sections (new, improved, fixed), each with
areas and their changes in Hebrew and English. The setup shows it on its "What's new" screen;
the GitHub release text is generated from it.

## Setup exe

See `installer-rs/PLAN.md`. Quick local iteration: `cargo build --profile quick` in
`installer-rs`, then pack with `target\quick\pack-payload.exe`.
