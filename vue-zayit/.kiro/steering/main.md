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

## Manual Loading

You can manually load specific guidance using these context keys:

- `#app` - Application architecture guidelines
- `#css` - CSS and styling guidelines
- `#db` - Database layer architecture
- `#database-config` - Database configuration and path management
- `#csharp` - C# integration guide
- `#virtualization` - BookLineViewer virtualization
- `#hebrew-books` - Hebrew book downloads
- `#hebrew-fonts` - Hebrew fonts and typography guidelines
- `#search` - Search functionality and navigation guidelines
- `#docs` - Documentation guidelines
- `#git` - Git safety rules
- `#touch` - Touch interaction guidelines and best practices
- `#workspace` - Workspace management guidelines and implementation

## Core Principles

1. **Minimal Context**: Only load what's relevant to current work
2. **Smart Detection**: Automatically detect context from files and keywords
3. **Manual Override**: Use context keys when automatic detection isn't enough
4. **Single Source**: Each topic has one authoritative guide

## Clean Code Principles

Based on analysis of problematic code patterns in this project, follow these essential practices:

### 1. Keep Constructors Simple

- Constructors should only initialize object state, not perform complex operations
- Avoid file I/O, network calls, or heavy computation in constructors
- Use factory methods or dependency injection for complex initialization

### 2. Eliminate Debug Pollution

- Remove excessive logging and debug statements from production code
- Use proper logging frameworks with configurable levels instead of Console.WriteLine
- Debug code should never make it to production

### 3. Single Responsibility Principle

- Each class should have one reason to change
- Separate concerns: database logic ≠ UI logic ≠ configuration management
- If a class is doing multiple unrelated things, split it

### 4. Proper Error Handling

- Don't catch exceptions just to log and continue - handle them meaningfully
- Fail fast when encountering unrecoverable errors
- Use specific exception types, not generic Exception catching

### 5. Manage State Appropriately

- Use static state only when it represents truly shared application state
- Ensure thread safety when using static fields in multi-threaded scenarios
- Consider the lifecycle and scope of your data when choosing between static and instance state

### 6. Eliminate Code Duplication

- Extract repeated logic into reusable methods
- Use the DRY principle (Don't Repeat Yourself)
- Common patterns should be abstracted into utilities

### 7. Separation of Concerns

- Keep UI logic separate from business logic
- Database access should be isolated in dedicated layers
- Configuration management should be its own responsibility

### 8. Simplify Complex Methods

- Long methods with multiple responsibilities should be broken down
- Each method should do one thing well
- Use early returns to reduce nesting and improve readability

### 9. Proper Resource Management

- Use `using` statements or proper disposal patterns for resources
- Don't leave connections or files open indefinitely
- Handle resource cleanup in finally blocks or disposal methods

### 10. Clear and Consistent APIs

- Method signatures should be predictable and consistent
- Avoid methods that sometimes return different types based on conditions
- Use meaningful parameter names and return types
