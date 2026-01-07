# Quick Start Guide

## Project Structure

```
/
├── zayit-vue/         # Vue 3 frontend (PRIMARY)
├── Zayit-cs/          # C# desktop application
└── vue-tabs/          # Legacy (deprecated)
```

## Development

### Vue Frontend (zayit-vue/)

```bash
# Install dependencies
npm install

# Development server with hot reload
npm run dev
# Opens at http://localhost:5173
# Uses direct SQLite access via Vite plugin

# Build for production
npm run build
# Outputs single HTML file to dist/index.html

# Deploy to C# project
build-and-deploy.bat
```

**IMPORTANT BUILD RULE**: Only build and deploy when explicitly requested by the user or when making final changes. The C# project has smart pre-build that automatically rebuilds Vue when needed. Avoid unnecessary builds during development iterations.

**TESTING RULE**: Never run development servers (`npm run dev`) during testing. Use `npm run build` to test compilation and catch errors without starting long-running processes.

### C# Backend (Zayit-cs/)

```bash
# Build
build.bat

# Build and run
build.bat run

# Clean and rebuild
build.bat rebuild

# Build release
build.bat release
```

## Architecture

### SQL Queries

- **Defined in**: `zayit-vue/src/data/sqlQueries.ts`
- **Used by**: Both development (Vite) and production (C#)
- **Rule**: Never define SQL elsewhere

### Data Flow

**Development Mode**:

```
Component → dbManager → sqliteDb → Vite Plugin → SQLite
```

**Production Mode**:

```
Component → dbManager → csharpBridge → WebView2 → C# → SQLite
```

### Communication

**Vue → C#**:

```typescript
window.chrome.webview.postMessage({
  command: "GetTree",
  args: [],
});
```

**C# → Vue**:

```csharp
await ExecuteScriptAsync("window.receiveTreeData({json});");
```

## Common Tasks

### Add New Database Query

1. Add SQL to `zayit-vue/src/data/sqlQueries.ts`:

```typescript
export const SqlQueries = {
  getMyData: (id: number) => `
    SELECT * FROM myTable WHERE id = ${id}
  `,
};
```

2. Add to `zayit-vue/src/data/sqliteDb.ts`:

```typescript
export async function getMyData(id: number) {
  return await query(SqlQueries.getMyData(id));
}
```

3. Add handler to `zayit-vue/src/data/csharpBridge.ts`:

```typescript
win.receiveMyData = (id: number, data: any) => {
  const request = this.pendingRequests.get(`GetMyData:${id}`);
  if (request) {
    request.resolve(data);
    this.pendingRequests.delete(`GetMyData:${id}`);
  }
};
```

4. Add to `zayit-vue/src/data/dbManager.ts`:

```typescript
async getMyData(id: number) {
  if (this.isWebViewAvailable()) {
    const promise = this.csharp.createRequest(`GetMyData:${id}`)
    this.csharp.send('GetMyData', [id])
    return promise
  } else {
    return await sqliteDb.getMyData(id)
  }
}
```

5. Add C# handler to `Zayit-cs/Zayit/Viewer/ZayitViewer.cs`:

```csharp
private async void GetMyData(int id)
{
    // Copy SQL from sqlQueries.ts
    var result = SeforimDb.DbQueries.ExecuteQuery($@"
        SELECT * FROM myTable WHERE id = {id}
    ");

    string json = JsonSerializer.Serialize(result, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    await ExecuteScriptAsync($"window.receiveMyData({id}, {json});");
}
```

### Update Styles

Edit `zayit-vue/src/main.css` or component-specific styles.

### Add New Component

Create in `zayit-vue/src/components/` using:

```vue
<script setup lang="ts">
// Component logic
</script>

<template>
  <!-- Template -->
</template>

<style scoped>
/* Styles */
</style>
```

## Troubleshooting

### Build fails

```bash
cd zayit-vue
npm install
npm run build
```

### C# can't find HTML

1. Check `Zayit-cs/Zayit/Html/index.html` exists
2. Run `cd zayit-vue && build-and-deploy.bat`
3. Rebuild C# project

### Database queries fail

1. Check SQL in `sqlQueries.ts`
2. Verify C# copied SQL correctly
3. Check database path in `vite.config.ts`

### WebView2 not working

1. Install WebView2 Runtime
2. Check `app.manifest` in C# project
3. Verify `CoreWebView2InitializationCompleted` event

## Resources

- Vue 3 Docs: https://vuejs.org/
- Vite Docs: https://vitejs.dev/
- WebView2 Docs: https://learn.microsoft.com/en-us/microsoft-edge/webview2/
- Pinia Docs: https://pinia.vuejs.org/
