# Kiwix VSTO Project

This workspace contains the Kiwix desktop integration project for Windows.

## Repository layout

- `Kiwix.slnx` — Visual Studio solution for the desktop projects.
- `KiwixLib/` — .NET Framework class library that hosts the Kiwix JS app in WebView2.
  - `KiwixLib/Kiwix.js/` — runtime copy of the web app assets used by the library.
- `KiwixDemoApp/` — WinForms demo application that references `KiwixLib` and displays the Kiwix WebView.
- `kiwix-js-main/` — upstream Kiwix JS source tree and build environment.
- `KIWIX_CHANGES.md` — customisation log and build workflow notes.

## How it works

- `kiwix-js-main/` contains the web app source and the JS build pipeline.
- The desktop app does not build the web app itself.
- Instead, `KiwixLib` ships the prebuilt static `Kiwix.js` folder and serves it from WebView2.
- `KiwixDemoApp` references `KiwixLib`, so building the demo app also builds/includes the library.

## Refreshing the Kiwix JS build output

When you change the source in `kiwix-js-main/`, use the ready-made refresh command to rebuild and copy the output into `KiwixLib/Kiwix.js`.

From `kiwix-js-main/`:

```powershell
npm run kiwix-lib-refresh
```

This does the following:

1. Runs `npm run build-src` in `kiwix-js-main/`.
2. Copies the generated `dist/` contents into `../KiwixLib/Kiwix.js/`.

The helper script is located at:

- `kiwix-js-main/scripts/kiwix-lib-refresh.ps1`

## Building the desktop app

After refreshing the JS assets, build the `.NET` projects normally:

- Open `Kiwix.slnx` in Visual Studio and build the solution
- or use MSBuild / `dotnet build` on `KiwixDemoApp/KiwixDemoApp.csproj` and `KiwixLib/KiwixLib.csproj`

## Notes

- `KiwixLib.csproj` includes the `Kiwix.js` subtree as content and copies it to the library output folder.
- `KiwixWebview.cs` maps the local `Kiwix.js` folder into WebView2 at `https://kiwix-app/index.html`.
- If `npm install` changes Bootstrap or other dependencies, re-run `npm run kiwix-lib-refresh` to refresh the runtime assets.

## Useful files

- `kiwix-js-main/package.json` — JS build scripts.
- `KiwixLib/KiwixWebview.cs` — runtime WebView2 host logic.
- `KiwixDemoApp/MainForm.cs` — demo app entry point.
- `KIWIX_CHANGES.md` — custom change log and workflow instructions.
