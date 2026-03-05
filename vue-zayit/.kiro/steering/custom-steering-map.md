---
inclusion: always
---

# Custom Steering Map

Reference map for conditional loading of custom steering files based on context.

## Quick Reference

For immediate help, see: #[[file:custom-steering/QUICK_START.md]]

## Conditional Loading Rules

### Vue Frontend Development

- **File patterns**: `*.vue`, `*.ts`, `*.js`, `*.css`, `package.json`, `vite.config.*`
- **Loads**: #[[file:custom-steering/app.md]], #[[file:custom-steering/css-guidelines.md]]

### Database Operations

- **Keywords**: database, sql, query, db, sqlite
- **File patterns**: `*sqlQueries*`, `*dbManager*`, `*sqlite*`
- **Loads**: #[[file:custom-steering/db.md]]

### Database Configuration

- **Keywords**: database path, database config, settings, reset settings
- **File patterns**: `*SettingsPage*`, `*DbQueries*`, `*ServiceProvider*`
- **Loads**: #[[file:custom-steering/database-configuration.md]]

### C# Integration

- **File patterns**: `*.cs`, `*.csproj`, `*.sln`
- **Keywords**: csharp, webview, bridge
- **Loads**: #[[file:custom-steering/csharp-integration.md]]

### Virtualization & Performance

- **Keywords**: virtualization, performance, loading, buffer, batch
- **File patterns**: `*BookLineViewer*`, `*virtualization*`
- **Loads**: #[[file:custom-steering/virtualization.md]]

### Commentary Virtualization

- **Keywords**: commentary, links, scroll, height, estimation
- **File patterns**: `*CommentaryView*`
- **Loads**: #[[file:custom-steering/commentary-virtualization.md]]

### Hebrew Books Feature

- **Keywords**: hebrew, book, download, pdf
- **File patterns**: `*hebrewBooks*`, `*PdfViewer*`
- **Loads**: #[[file:custom-steering/hebrew-book-downloads.md]]

### Hebrew Fonts & Typography

- **Keywords**: font, fonts, hebrew, niqqud, taamim, culmus, kulmus, typography
- **File patterns**: `*hebrewFonts*`, `*font*`
- **Loads**: #[[file:custom-steering/hebrew-fonts.md]]

### Search Functionality

- **Keywords**: search, navigate, highlight, match, scroll, center
- **File patterns**: `*Search*`, `*GenericSearch*`
- **Loads**: #[[file:custom-steering/search-functionality.md]]

### Documentation Tasks

- **Keywords**: documentation, docs, readme, guide
- **Loads**: #[[file:custom-steering/documentation.md]]

### Touch Interactions

- **Keywords**: touch, mobile, tap, gesture, dropdown, click-outside
- **File patterns**: `*Dropdown*`, `*Touch*`, `*Mobile*`
- **Loads**: #[[file:custom-steering/touch-guidelines.md]]

### Workspace Management

- **Keywords**: workspace, session, tabs, switch, manage, פריטים
- **File patterns**: `*WorkspaceManager*`, `*workspace*`
- **Loads**: #[[file:custom-steering/workspace-management.md]]

### Git Operations

- **Keywords**: git, commit, revert, reset
- **Loads**: #[[file:custom-steering/git-safety.md]]

### Iconify Offline Icons

- **Keywords**: iconify, icons, offline, bundle, extract, tree-shake
- **File patterns**: `*iconify*`, `*extract-icons*`, `*Icon*`
- **Loads**: #[[file:custom-steering/iconify-offline.md]]

## Manual Loading Context Keys

- `#app` - Application architecture guidelines
- `#css` - CSS and styling guidelines
- `#db` - Database layer architecture
- `#database-config` - Database configuration and path management
- `#csharp` - C# integration guide
- `#virtualization` - BookLineViewer virtualization
- `#commentary` - Commentary view virtualization and scroll tracking
- `#hebrew-books` - Hebrew book downloads
- `#hebrew-fonts` - Hebrew fonts and typography guidelines
- `#search` - Search functionality and navigation guidelines
- `#docs` - Documentation guidelines
- `#git` - Git safety rules
- `#touch` - Touch interaction guidelines and best practices
- `#workspace` - Workspace management guidelines and implementation
- `#iconify` - Iconify offline icon system and automatic extraction
