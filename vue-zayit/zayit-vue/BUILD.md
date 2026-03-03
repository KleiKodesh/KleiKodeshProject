# Building Zayit Vue for C# Integration

This Vue application is designed to be embedded in the C# WebView2 application.

## Build Process

1. **Install dependencies:**

   ```bash
   npm install
   ```

2. **Build for production:**

   ```bash
   npm run build
   ```

   This creates a single-file HTML bundle in the `dist/` folder optimized for WebView2.

3. **Copy to C# project:**

   The C# application expects the built files in a folder named `zayit-vue-app` in the output directory.

   Copy the contents of `dist/` to the C# project's output folder as `zayit-vue-app/`:

   ```
   Zayit-cs/[ProjectOutput]/zayit-vue-app/
   ```

## C# Integration Points

The Vue app communicates with C# through the WebView2 bridge (`webviewBridge.ts`):

- **Database operations**: File picker, path management, validation
- **PDF operations**: File picker, virtual URL management
- **Hebrew Books**: Download, caching, viewing
- **Bloom Search**: Full-text search with streaming results
- **System operations**: Open URLs in browser, reload page

## Development

For development with live reload:

```bash
npm run dev
```

This runs a local dev server with SQLite database integration for testing without C#.

## Type Checking

Before building, run type checking:

```bash
npm run type-check
```

## Key Files

- `vite.config.prod.ts` - Production build configuration (single-file bundle)
- `src/data/services/webviewBridge.ts` - C# communication bridge
- `src/data/services/dbQueries.ts` - Database query interface
