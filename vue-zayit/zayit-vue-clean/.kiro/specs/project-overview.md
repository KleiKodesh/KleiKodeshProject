Project Structure Overview:

Mobile-first, tabbed navigation app (Hebrew RTL)
Features: browse books, full-text search, PDF viewing, settings
Organized by feature folders under src/components/
Key Components:

Layout (AppTitleBar, AppTitleBarTabDropdown) - Tab management UI
Home (HomePage, HomePageTile) - Main hub with action tiles
Books (BooksFsPage, BooksBookList, BooksBreadcrumb, useBooksFs) - File system tree browser with search
PDF (PdfViewPage) - PDF.js viewer integration
Settings (SettingsPage) - Placeholder for future settings
State Management:

tabStore.ts - Tab lifecycle (open, switch, close, update)
pdfStore.ts - PDF state (blob URL, filename)
themeStore.ts - Theme persistence and application
Theme System:

30+ theme presets (light/dark variants) in themes.json
Fluent Design principles with Windows 11 colors
Separate UI and reading backgrounds
PDF.js theme syncing with dynamic filters
Custom theme support via localStorage
Database Layer:

db.ts - Unified SQLite client (WebView or HTTP fallback)
queries.sql.ts - All SQL strings (categories, books, TOC, lines, links)
Supports both C# WebView host and dev HTTP server
Styling:

CSS variables for theming (--bg-primary, --text-primary, etc.)
Fluent Design hover/active states (--hover-bg, --active-bg)
RTL-aware with logical CSS properties
Iconify icons via @iconify-prerendered/vue-fluent
Key Patterns:

Components are dumb (props in, template out)
Composables handle feature logic (useBooksFs)
Pure utility functions for tree building (booksFsTree.ts)
Search bar anchored to bottom (iOS-style)
Virtual scrolling for book lists (vue-virtual-scroller)