---
inclusion: auto
name: architecture-map
description: Map of the app and its architecture
---

# Architecture Map

```
┌─────────────────────────────────────────────────────────────────┐
│                          DATA LAYER                              │
│                     (Framework-agnostic)                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  STORES (Pinia)                                                  │
│  ├─ tabStore           - Active tabs and workspace items        │
│  ├─ workspaceStore     - Workspace sessions and management      │
│  ├─ settingsStore      - App settings and preferences           │
│  ├─ categoryTreeStore  - Book categories and navigation         │
│  ├─ hebrewBooksStore   - Hebrew books catalog and state         │
│  └─ connectionTypesStore - Database connection types            │
│                                                                   │
│  SERVICES                                                        │
│  ├─ dbService          - Core database operations               │
│  ├─ dbQueries          - SQL query execution                    │
│  ├─ webviewBridge      - C# ↔ Vue communication                 │
│  ├─ bookLineLoader     - Book text loading                      │
│  ├─ bookLineViewerService - Virtualized book rendering          │
│  ├─ bookTocService     - Table of contents                      │
│  ├─ bookCommentaryService - Commentary data                     │
│  ├─ bloomSearchService - Bloom filter search engine             │
│  ├─ bloomSearchCacheService - Search result caching             │
│  ├─ hebrewBooksService - Hebrew books main service              │
│  ├─ hebrewBooksPdfService - PDF handling                        │
│  ├─ hebrewBooksSearchService - Hebrew books search              │
│  ├─ hebrewBooksHistoryService - Download history                │
│  ├─ hebrewBooksCsvLoader - CSV data loading                     │
│  ├─ hebrewBooksHandlers - Hebrew books event handlers           │
│  ├─ pdfService         - PDF viewer operations                  │
│  └─ webviewHebrewBooks - Hebrew books C# bridge                 │
│                                                                   │
│  TYPES                                                           │
│  ├─ BloomSearch        - Search types                           │
│  ├─ Book               - Book data types                        │
│  ├─ BookCategoryTree   - Category tree types                    │
│  ├─ BookToc            - Table of contents types                │
│  ├─ ConnectionType     - Database connection types              │
│  ├─ HebrewBook         - Hebrew books types                     │
│  ├─ Link               - Link types                             │
│  ├─ LinkGroup          - Link group types                       │
│  ├─ Tab                - Tab types                              │
│  └─ Topic              - Topic types                            │
│                                                                   │
│  WORKERS                                                         │
│  └─ searchWorker       - Background search processing           │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
                              ▲
                              │
                    (accessed via composables)
                              │
┌─────────────────────────────────────────────────────────────────┐
│                      COMPOSABLES LAYER                           │
│                    (Business logic bridge)                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  Feature composables colocated with components                   │
│  ├─ Access stores and services                                  │
│  ├─ Contain business logic                                      │
│  ├─ Provide reactive state to components                        │
│  └─ Handle side effects                                         │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
                              ▲
                              │
                    (consumed by components)
                              │
┌─────────────────────────────────────────────────────────────────┐
│                     COMPONENTS LAYER                             │
│                  (Presentation-only, dumb)                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  COMPARTMENTS (Feature folders)                                  │
│                                                                   │
│  book/                 - Book viewer and text display            │
│  │                       Data: bookLineLoader, bookLineViewerService,│
│  │                             bookTocService, tabStore           │
│                                                                   │
│  commentary/           - Commentary links and display            │
│  │                       Data: bookCommentaryService, tabStore   │
│                                                                   │
│  workspace/            - Workspace and tab management            │
│  │                       Data: workspaceStore, tabStore          │
│                                                                   │
│  settings/             - Settings and preferences UI             │
│  │                       Data: settingsStore                     │
│                                                                   │
│  home/                 - Home page and category navigation       │
│  │                       Data: categoryTreeStore, dbService      │
│                                                                   │
│  zayitdb-search/       - Search functionality                    │
│  │                       Data: bloomSearchService, bloomSearchCacheService│
│                                                                   │
│  hebrew-books/         - Hebrew books catalog and downloads      │
│  │                       Data: hebrewBooksStore, hebrewBooksService,│
│  │                             hebrewBooksPdfService, hebrewBooksSearchService│
│                                                                   │
│  pdf/                  - PDF viewer                              │
│  │                       Data: pdfService, hebrewBooksPdfService │
│                                                                   │
│  zayitdb-fs/           - File system operations                  │
│  │                       Data: webviewBridge                     │
│                                                                   │
│  shared/               - Reusable UI components                  │
│  │                       Data: Various stores as needed          │
│                                                                   │
│  icons/                - Icon components                         │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                         UTILS LAYER                              │
│                    (Pure functions, no deps)                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ├─ hebrewTextProcessing - Hebrew text utilities                │
│  ├─ hebrewFonts         - Font management                       │
│  ├─ searchHighlighting  - Search result highlighting            │
│  ├─ censorDivineNames   - Divine name censoring                 │
│  ├─ themes              - Theme utilities                       │
│  ├─ themeColorUtils     - Color manipulation                    │
│  ├─ readingBackgrounds  - Background patterns                   │
│  ├─ lruStorage          - LRU cache implementation              │
│  └─ iconify-offline     - Offline icon system                   │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘

IMPORT RULES:
├─ Data layer    → imports nothing (framework-agnostic)
├─ Composables   → imports data + utils
├─ Components    → imports composables + utils (never data directly)
└─ Utils         → imports nothing (pure functions)
```
