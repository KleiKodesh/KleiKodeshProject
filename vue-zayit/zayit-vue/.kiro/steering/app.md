# Application Architecture Guidelines

## State Management

### Component State Ownership
- Each component owns its local state
- Parent components control child layout, not children themselves
- State flows down via props, events flow up via emits
- Use stores for shared state across components

### Persistence Patterns
- Save state on user actions (scroll, tab switch, close)
- Restore state on mount/activation
- Use debouncing for frequent events (300ms for scroll)
- Immediate save on critical events (unmount, tab close)

## Large Data Loading

### Progressive Loading Strategy
1. **Render placeholders first** - Show structure immediately
2. **Load visible content** - Prioritize what user sees
3. **Background load rest** - Non-blocking, can be aborted
4. **Buffer optimization** - Pre-load data without blocking UI

### Performance Principles
- No complex calculations - Keep logic simple
- Always responsive - UI never blocks
- Progressive enhancement - Start minimal, add more
- Abort on navigation - Cancel unnecessary work

### Loading States
- **Initial load**: Placeholders only
- **Restore**: Load saved position + surrounding content
- **User action**: Load target + surrounding content, apply buffer

## Tab Management

### Tab Lifecycle
- Reuse lowest available ID when creating tabs
- Each tab has isolated component instance (use unique keys)
- Clean up resources on tab close
- Preserve state when switching between tabs

### Component Keys
Use composite keys for proper isolation:
```vue
:key="`${tabId}-${pageType}`"
```

## Data Layer

### Separation of Concerns
- **Router layer**: Environment detection and routing only
- **Implementation layer**: Actual data operations
- **Query layer**: SQL templates (single source of truth)

### Rules
- All SQL in query templates file
- No SQL in routing or implementation layers
- Components use router only, never direct implementation
- No code duplication across layers

## Documentation

### Keep It Minimal
- One doc file per feature/module maximum
- Prefer inline comments and JSDoc
- Only create docs when explicitly needed
- Update existing docs, don't create new ones

### When to Document
1. Complex architecture needs explanation
2. Integration with external systems
3. User explicitly requests it

**Don't create**: SUMMARY.md, MIGRATION.md, VERIFICATION.md, CLEANUP.md, etc.
