# Quick Start Guide

## Project Status

All communication pipelines between Vue and C# are verified and functional:

- Category Tree Loading ✅
- TOC Loading ✅
- Book Lines Loading ✅ (virtualized with smart batching)
- Commentary Loading ✅
- PDF Files ✅ (SetVirtualHostNameToFolderMapping + session persistence)
- Hebrew Books ✅ (download capture + cache management)

## Project Structure

```
/
├── zayit-vue/         # Vue 3 frontend - Clean services architecture
├── Zayit-cs/          # C# desktop application - Modern services architecture
│   └── ZayitLib/
│       └── Services/  # Clean service layer
└── shared-pdfjs/      # PDF.js distribution
```

## Architecture

### Communication Flow

Vue Component → webviewBridge → WebViewBridgeService → ServiceProvider → Specialized Services (DbService, PdfService, HebrewBooksService)

### Data Flow

**Development Mode**: Component → dbService → devQuery → Vite Plugin → SQLite

**Production Mode**: Component → dbService → webviewBridge → C# ServiceProvider → DbService → SQLite

### SQL Queries

- Single source of truth: `zayit-vue/src/services/sqlQueries.ts`
- Used by both development (Vite) and production (C#)
- Never define SQL elsewhere

## Vue Services

- `webviewBridge.ts` - Singleton bridge with lazy message listener
- `dbService.ts` - Database operations
- `pdfService.ts` - Unified PDF operations with session persistence
- `webviewHebrewBooks.ts` - Hebrew Books operations
- `hebrewBooksHandlers.ts` - Event handlers

## C# Services

- `ServiceProvider.cs` - Central hub with all Vue-required methods
- `WebViewBridgeService.cs` - JSON message parsing and method invocation
- `DbService.cs` - Database operations via DbQueries
- `PdfService.cs` - SetVirtualHostNameToFolderMapping operations
- `HebrewBooksService.cs` - Download capture and cache management

## Development

### Vue Frontend

```bash
npm install              # Install dependencies
npm run dev             # Development server at http://localhost:5173
npm run build           # Build for production
build-and-deploy.bat    # Deploy to C# project
```

**Build Rule**: Only build when explicitly requested or making final changes - C# project has smart pre-build that auto-rebuilds Vue when needed

**Testing Rule**: Never run development servers during testing - use `npm run build` to test compilation

### C# Backend

```bash
dotnet build "Zayit-cs/ZayitSolution.sln" --configuration Debug
# Or use MSBuild if available
msbuild "Zayit-cs/ZayitSolution.sln" /p:Configuration=Debug
```

## Common Tasks

### Add Database Operation

1. Add SQL to `zayit-vue/src/services/sqlQueries.ts`
2. Add Vue service method to `zayit-vue/src/services/dbService.ts`
3. Add C# service method to `Zayit-cs/ZayitLib/Services/DbService.cs`
4. Expose in ServiceProvider `Zayit-cs/ZayitLib/Services/ServiceProvider.cs`

### Add PDF Operation

1. Add to PdfService.cs (use SetVirtualHostNameToFolderMapping for file access)
2. Expose in ServiceProvider
3. Add Vue service call

### Add Hebrew Books Operation

1. Add to HebrewBooksService.cs (use download capture and cache management)
2. Expose in ServiceProvider with proper async handling
3. Add Vue service call

## Troubleshooting

### Build Fails

- Vue: `cd zayit-vue && npm install && npm run build`
- C#: `dotnet build "Zayit-cs/ZayitSolution.sln" --configuration Debug`

### Communication Issues

- Check browser DevTools Console for Vue errors
- Check Visual Studio Output window for C# errors
- Verify WebView2 Runtime is installed
- Test `webviewBridge.isAvailable()` in Vue

### File Operations Not Working

- Verify SetVirtualHostNameToFolderMapping is set up
- Check file permissions and paths
- Ensure virtual URLs use correct host name (zayitHost)

### Hebrew Books Cache Issues

- Check cache directory exists: `Html/pdfjs/web/hebrewbookscache/`
- Verify download event handlers are registered
- Test cache stats: GetHebrewBooksCacheStats
