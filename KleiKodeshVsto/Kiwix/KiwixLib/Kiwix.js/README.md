# Kiwix.js — Prebuilt Static Copy

This directory contains a prebuilt static copy of the **Kiwix JS** web application, used as the frontend for the Kiwix ZIM file reader.

## Purpose

The files here are served locally by `KiwixWebview.cs` (in `KiwixLib`) via a WebView2 control. This provides the HTML/JS UI for browsing ZIM file content and searching within offline archives.

## Important

**Do NOT edit files in this directory directly.** The source code lives in the `kiwix-js-main/` project directory. To update the build:

```
npm run kiwix-lib-refresh
```

This copies the latest build artifacts into this directory.

## Upstream Project

The full Kiwix JS source code and issue tracker are at:

https://github.com/kiwix/kiwix-js

Licensed under GPL v3.
