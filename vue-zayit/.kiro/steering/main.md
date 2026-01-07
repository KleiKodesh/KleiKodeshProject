---
inclusion: always
---

# Zayit Project Steering

This is the main steering file that conditionally loads relevant guidance based on what you're working on.

## Quick Reference

For immediate help, see: #[[file:custom-steering/QUICK_START.md]]

## Conditional Loading Rules

The following files are loaded automatically based on context:

### Vue Frontend Development

- **File patterns**: `*.vue`, `*.ts`, `*.js`, `*.css`, `package.json`, `vite.config.*`
- **Loads**: #[[file:custom-steering/app.md]], #[[file:custom-steering/css-guidelines.md]]

### Database Operations

- **Keywords**: database, sql, query, db, sqlite
- **File patterns**: `*sqlQueries*`, `*dbManager*`, `*sqlite*`
- **Loads**: #[[file:custom-steering/db.md]]

### C# Integration

- **File patterns**: `*.cs`, `*.csproj`, `*.sln`
- **Keywords**: csharp, webview, bridge
- **Loads**: #[[file:custom-steering/csharp-integration.md]]

### Virtualization & Performance

- **Keywords**: virtualization, performance, loading, buffer, batch
- **File patterns**: `*BookLineViewer*`, `*virtualization*`
- **Loads**: #[[file:custom-steering/virtualization.md]]

### Hebrew Books Feature

- **Keywords**: hebrew, book, download, pdf
- **File patterns**: `*hebrewBooks*`, `*PdfViewer*`
- **Loads**: #[[file:custom-steering/hebrew-book-downloads.md]]

### Documentation Tasks

- **Keywords**: documentation, docs, readme, guide
- **Loads**: #[[file:custom-steering/documentation.md]]

### Git Operations

- **Keywords**: git, commit, revert, reset
- **Loads**: #[[file:custom-steering/git-safety.md]]

## Manual Loading

You can manually load specific guidance using these context keys:

- `#app` - Application architecture guidelines
- `#css` - CSS and styling guidelines
- `#db` - Database layer architecture
- `#csharp` - C# integration guide
- `#virtualization` - BookLineViewer virtualization
- `#hebrew-books` - Hebrew book downloads
- `#docs` - Documentation guidelines
- `#git` - Git safety rules
- `#quickstart` - Quick start guide

## Core Principles

1. **Minimal Context**: Only load what's relevant to current work
2. **Smart Detection**: Automatically detect context from files and keywords
3. **Manual Override**: Use context keys when automatic detection isn't enough
4. **Single Source**: Each topic has one authoritative guide
