---
inclusion: always
---

# Offline-First Architecture

## CRITICAL: 100% Offline Capability Required

**All applications and components MUST work completely offline.**

This is a non-negotiable architectural requirement. No feature should depend on internet connectivity to function.

## Core Principles

### 1. No Required Network Calls
- **Never block functionality** on network requests
- **No online-only features** - everything must have offline fallback
- **Graceful degradation** - optional enhancements only when online

### 2. Local-First Data
- **All critical data stored locally** - files, databases, cache
- **Network as enhancement** - updates, sync, optional content only
- **Immediate availability** - no loading states waiting for network

### 3. Update System Pattern
```csharp
// ✅ CORRECT: Non-blocking update check
public async Task CheckForUpdatesAsync()
{
    try 
    {
        // Check in background, never block UI
        var update = await updateService.CheckAsync();
        if (update != null) 
        {
            // Notify user, but app continues working
            NotifyUpdateAvailable(update);
        }
    }
    catch 
    {
        // Silently fail - offline is normal
        // App continues functioning
    }
}
```

```javascript
// ✅ CORRECT: Optional online enhancement
async function loadOptionalContent() {
  try {
    const data = await fetch('/api/enhancement');
    return data;
  } catch {
    // Return null or cached data
    // Feature continues without it
    return getCachedData();
  }
}
```

### 4. Resource Loading
- **Bundle all assets** - no CDN dependencies
- **Local fonts, icons, libraries** - everything in the package
- **No external scripts** - all JavaScript bundled locally

## Common Violations to Avoid

❌ **WRONG**: Blocking on network
```csharp
// Never do this
var data = await api.GetRequiredData(); // Blocks if offline
ProcessData(data);
```

❌ **WRONG**: CDN dependencies
```html
<!-- Never do this -->
<script src="https://cdn.example.com/library.js"></script>
```

❌ **WRONG**: Required online features
```javascript
// Never do this
if (!navigator.onLine) {
  showError("Internet required");
  return;
}
```

✅ **CORRECT**: Always functional
```csharp
// Local data always available
var data = localCache.GetData();
ProcessData(data);

// Optional background sync
_ = Task.Run(async () => {
    try { await SyncWithServerAsync(); }
    catch { /* Offline is fine */ }
});
```

## Testing Requirements

Every feature must pass the **Airplane Mode Test**:
1. Disconnect from internet completely
2. Launch application
3. Use all features normally
4. Everything must work without errors or degradation

## Exceptions (Rare)

The only acceptable online requirements:
- **Explicit user-initiated downloads** - "Download new content" button
- **Optional sync features** - clearly marked as requiring connection
- **Update checks** - background only, never blocking

Even these must:
- Show clear offline state
- Provide useful error messages
- Never crash or hang
- Allow app to continue functioning

## Architecture Implications

### Data Storage
- Use local files, SQLite, or embedded databases
- Cache everything needed for operation
- Sync is optional enhancement, not requirement

### Resource Management
- Bundle all dependencies in installer
- Use local virtual hosts for web content
- No external API calls for core functionality

### Error Handling
- Network errors are expected, not exceptional
- Fail silently for optional features
- Never show "connection required" for core features

## Summary

**Offline is not a fallback mode - it's the primary mode.**

Online connectivity is a bonus feature for updates and enhancements, never a requirement for functionality.
