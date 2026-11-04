# KitveiHakodesh Vue Frontend

Vue 3 + TypeScript frontend for the KitveiHakodesh seforim viewer.

## Structure

- `src/` — Source code
  - `features/` — Feature modules (book-view, search, settings, etc.)
  - `components/` — Reusable Vue components
  - `stores/` — Pinia state management
  - `webview-host/` — WebView2 host bridge communication
  - `utils/` — Utility functions
  - `layout/` — Layout components
  - `theme/` — Theming and styling

- `public/` — Static assets
- `dist/` — Production build output

## Build

```bash
npm install
npm run dev       # Development server
npm run build     # Production build
npm run preview   # Preview production build
```

## Integration

This frontend runs in a WebView2 control hosted by `KitveiHakodeshLib` (C# backend). Communication between Vue and C# happens via:
- `webview-host/bridge.ts` — Message passing interface
- `JsBridge.cs` — C# backend handler
- Named events for async communication

## Key Features

- Torah text display with commentary
- Full-text search with Ftslib
- PDF/HTML viewer
- Hebrew calendar integration
- Dictionary lookups
- Responsive RTL layout

## Hosted end-to-end rig (real WebView2 host)

`node e2e-hosted/run-hosted-rig.mjs` drives the real desktop host (KitveiHakodeshDemoApp)
through the real relay -> service -> Core path with Playwright over CDP, then tears it all down.

- Builds the host into a temp folder (MSBuild; the shared AOT payload and `dist/` are reused,
  never rebuilt), then this frontend into the same copy with `VITE_KH_TEST_HOOKS=1`
  (keeps `window.__khTest` / `__fastDocViewTest`; normal builds strip them).
- Starts its own service copy (shadow of `KitveiHakodeshService/bin/Release/net10.0-windows`,
  data via `KHS_DATA_DIR`) on a private pipe `KitveiHakodesh.rig-<pid>`. The launcher never
  elevates or replaces a private-pipe copy, and the installed service leaves it alone.
- Starts the host with `KH_TEST_CDP_PORT=<free port>` and `KH_TEST_PROFILE_DIR` (see
  `KitveiHakodeshLib/HostTestMode.cs`): DevTools on that port, WebView2 user data and host
  files in the temp profile, no single-instance handoff to the user's running app.
- Checks: open a .docx from the command line, scroll, jump, notes pane, search, pictures as
  object URLs that decode, a missing picture as a sized empty box, object URLs revoked on tab
  close, no console errors. Exit code 0 = all passed.
- Kills only the pids it started (`taskkill /T`), deletes the temp folder.

Options: `--doc <docx>`, `--pictures <docx>`, `--service-dir <folder>`, `--keep` (keep the temp
folder), `--host-dir <folder>` (reuse the `host` folder of a kept run; skips the MSBuild step).
One run takes about 1-3 minutes plus the host build.
