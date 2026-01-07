# Zayit Word Add-in - Vue Frontend

A modern Vue.js-based frontend for the Zayit library system, designed as part of **כלי קודש לוורד - ארגז כלים לעורך התורני** (Holy Tools for Word - Toolbox for Torah Editors).

## Overview

This project provides a web-based interface for browsing and reading Jewish texts from the Zayit library. It serves as both a standalone desktop application (via WebView2) and as the frontend component for a Microsoft Word add-in.

### Relationship to Zayit

- **Original Zayit**: Written in Kotlin - [kdroidFilter/Zayit](https://github.com/kdroidFilter/Zayit)
- **This Project**: Vue.js frontend that can be embedded in Word or run standalone
- **Purpose**: Test application and development platform for the Word add-in integration

## Features (Phase 1 - Complete)

### Core Functionality
- ✅ **Multi-tab Interface** - Browse multiple books simultaneously with tab management
- ✅ **Hierarchical Book Browser** - Navigate through categories and books with collapsible tree structure
- ✅ **Table of Contents** - Quick navigation within books with search functionality
- ✅ **Book Reading** - Optimized display of Hebrew texts with proper RTL support
- ✅ **Search** - Fast search across books and within table of contents

### Text Display Features
- ✅ **Diacritics Control** - Toggle between full text, no cantillation marks, or no diacritics
- ✅ **Divine Name Censoring** - Option to replace ה with ק in divine names (יהוה, אדני, אלהים, etc.)
- ✅ **Line Display Modes** - Switch between block and inline line display
- ✅ **Hierarchical Headers** - Properly styled h1-h6 headers for document structure
- ✅ **Customizable Fonts** - Separate font selection for headers and body text
- ✅ **Font Size Control** - Adjustable text size with slider
- ✅ **Line Spacing** - Configurable line padding

### User Experience
- ✅ **Dark/Light Themes** - VS Code-inspired design system
- ✅ **Keyboard Navigation** - Full keyboard support for navigation and selection
- ✅ **Responsive Design** - Mobile and desktop optimized layouts
- ✅ **State Persistence** - Tabs, scroll positions, and settings saved across sessions
- ✅ **Performance Optimized** - Efficient rendering for large books (5000+ lines)

### Technical Features
- ✅ **KeepAlive Caching** - Preserves component state when switching tabs
- ✅ **Lazy Loading** - Content loaded on demand
- ✅ **Chunked Processing** - Non-blocking text processing for large documents
- ✅ **WebView2 Integration** - Seamless C# backend communication

## Architecture

This application follows a **container/presentational pattern** with **centralized routing state**:

- **Tab Store (Pinia)** - Manages application-level navigation state (active tab, content type, book selection). Acts as a lightweight router coordinating between components.

- **Smart Components** - Self-contained components (CategoryTree, TocTree, BookViewer) that read from and write to the tab store for navigation, while maintaining their own local UI state (keyboard navigation, search filtering, animations).

- **Functional Composition** - Each component is independently functional and can work in isolation. The tab store provides coordination without tight coupling.

This pattern combines:
- **Flux/Redux principles** - Unidirectional data flow for navigation state
- **Component autonomy** - Local state management for UI concerns
- **Separation of concerns** - Application state (tabs) vs component state (UI)

## Technology Stack

### Frontend
- **Vue 3** - Composition API with TypeScript
- **Pinia** - State management
- **Vite** - Build tool and dev server

### Backend Integration
- **C# WebView2** - Desktop shell application
- **Message Passing** - Bidirectional communication between C# and JavaScript

### Design System
- **Windows 11 Fluent Design** - Color palette and design language
- **VS Code Theme** - Professional light/dark themes and layout
- **Fluent Design** - Microsoft Fluent icons and patterns
- **RTL Support** - Full right-to-left layout for Hebrew text

### Theming

All colors are defined as CSS variables in `src/assets/main.css`. For image files (PNG/SVG), use the `themed-icon` class to automatically invert colors in dark mode:

```vue
<img src="icon.png" class="themed-icon" />
```

## Project Structure

```
vue-tabs/
├── src/
│   ├── components/
│   │   ├── BookViewer.vue      # Main book reading component
│   │   ├── LandingPage.vue     # Book search and TOC view
│   │   ├── TabHeader.vue       # Tab bar with controls
│   │   ├── TabDropdown.vue     # Tab list dropdown
│   │   ├── TocView.vue         # Table of contents display
│   │   ├── TocSidebar.vue      # Sidebar TOC overlay
│   │   ├── TreeView.vue        # Hierarchical book browser
│   │   ├── TreeNode.vue        # Tree node component
│   │   ├── TocNode.vue         # TOC tree node
│   │   ├── SettingsPane.vue    # Settings panel
│   │   └── AboutPane.vue       # About information
│   ├── stores/
│   │   ├── tabs.ts             # Tab management store
│   │   └── toc.ts              # Table of contents store
│   ├── types/
│   │   ├── Book.ts             # Book type definitions
│   │   ├── Tree.ts             # Tree structure types
│   │   └── Toc.ts              # TOC types
│   ├── data/
│   │   └── hebrewFonts.ts      # Available Hebrew fonts
│   ├── App.vue                 # Root component
│   └── main.ts                 # Application entry point
├── DESIGN_SYSTEM.md            # Design guidelines
├── PERFORMANCE_OPTIMIZATIONS.md # Performance documentation
└── package.json
```

## Installation & Development

### Prerequisites
- Node.js 16+
- npm or yarn

### Setup
```bash
cd vue-tabs
npm install
```

### Development Server
```bash
npm run dev
```

### Build for Production
```bash
npm run build
```

### Type Checking
```bash
npm run type-check
```

## C# Integration

The Vue app communicates with the C# backend through `window.chrome.webview.postMessage()`:

### JavaScript → C#
```javascript
window.chrome.webview.postMessage({
  command: 'OpenBook',
  args: [bookId, tabId]
})
```

### C# → JavaScript
```javascript
window.addLines(tabId, linesArray)
window.receiveTocData(bookId, tocData)
window.receiveTreeData(treeData)
```

## Future Features (Pending)

### Planned Enhancements
- 🔲 **Split Pane View** - Display commentaries and linked texts side-by-side
- 🔲 **Full Text Search** - Search within book content
- 🔲 **Cross-References** - Navigate between linked texts
- 🔲 **Bookmarks** - Save and manage reading positions
- 🔲 **Notes & Highlights** - Annotate texts

### Direction
Future development will align with the direction of the Kotlin Zayit project.

## License

This code is based on [kdroidFilter/SeforimApp](https://github.com/kdroidFilter/SeforimApp) and maintains the same **AGPL-3.0** license.

## Credits

- **Original Zayit**: [kdroidFilter/Zayit](https://github.com/kdroidFilter/Zayit) (Kotlin)
- **SeforimApp**: [kdroidFilter/SeforimApp](https://github.com/kdroidFilter/SeforimApp)
- **Project Context**: Part of כלי קודש לוורד - ארגז כלים לעורך התורני

## Related Links

- [Mitmachim Discussion](https://mitmachim.top/post/1044901)
- [Original Zayit (Kotlin) - SeforimApp](https://github.com/kdroidFilter/Zayit)

## Contributing

This is a test application for Word add-in development. Contributions should align with the overall Zayit project direction.

---

**Status**: Phase 1 Complete ✅
