# KiwixLib

WPF class library for hosting the Kiwix JS web app in a WebView2 control.

## Components

- `KiwixWebview` — Main WPF UserControl hosting the Kiwix web app
- `SplashOverlay` — Loading/splash screen overlay
- `Kiwix.js/` — Prebuilt static files from the Kiwix JS project

## Features

- Displays offline ZIM files (Wikipedia, religious texts, etc.)
- Full-text search on local content
- No internet connection required

## Integration

Referenced by the KleiKodeshVsto add-in and displayed as a task pane when the user clicks the "קורא קיוויקס" (Kiwix Reader) ribbon button.

## Updating Kiwix JS

When updating the Kiwix JS code:

1. Edit sources in `kiwix-js-main/`
2. Run: `npm run kiwix-lib-refresh` from `kiwix-js-main/`
3. This rebuilds and copies the output to `Kiwix.js/`
