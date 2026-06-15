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
