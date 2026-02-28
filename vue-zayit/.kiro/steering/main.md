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

## Manual Loading

You can manually load specific guidance using these context keys:

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

## Core Principles

1. **Minimal Context**: Only load what's relevant to current work
2. **Smart Detection**: Automatically detect context from files and keywords
3. **Manual Override**: Use context keys when automatic detection isn't enough
4. **Single Source**: Each topic has one authoritative guide
5. **No Unsolicited Documentation**: Do NOT create markdown files to document your work unless explicitly requested by the user

## Vue.js Development Standards

### Async Operations and Timing

- **ALWAYS prefer `nextTick()` over `setTimeout()`** for waiting on DOM updates
- `nextTick()` waits for Vue's reactive system to flush updates to the DOM
- Only use `setTimeout()` when you need a specific time delay for non-Vue reasons (animations, external APIs, etc.)
- Never use arbitrary timeouts (100ms, 200ms) to wait for Vue updates - use `nextTick()` instead

**Examples:**

```typescript
// ✅ GOOD: Wait for DOM update after reactive change
currentIndex.value = 5;
await nextTick();
scrollToItem(currentIndex.value);

// ❌ BAD: Arbitrary timeout hoping DOM is ready
currentIndex.value = 5;
setTimeout(() => {
  scrollToItem(currentIndex.value);
}, 200); // Why 200ms? What if it needs 300ms?

// ✅ GOOD: Multiple updates, single nextTick
items.value = newItems;
selectedIndex.value = 0;
await nextTick();
updateUI();

// ❌ BAD: Nested timeouts
items.value = newItems;
setTimeout(() => {
  selectedIndex.value = 0;
  setTimeout(() => {
    updateUI();
  }, 100);
}, 150);
```

**When setTimeout IS appropriate:**

- Debouncing user input
- Animation delays
- Polling external resources
- Rate limiting API calls

### VueUse Composables

- **ALWAYS prefer VueUse composables** when available instead of implementing from scratch
- Check [VueUse documentation](https://vueuse.org/) before writing custom utilities
- VueUse provides battle-tested, reactive, and well-maintained solutions
- Common use cases: DOM events, browser APIs, state management, sensors, animations

**Examples:**

- Use `useMagicKeys` instead of manual keyboard event listeners
- Use `useLocalStorage` instead of manual localStorage access
- Use `useEventListener` instead of `addEventListener`
- Use `useIntersectionObserver` instead of manual IntersectionObserver setup
- Use `useResizeObserver` instead of manual ResizeObserver setup

### Keyboard Event Handling

- **ALWAYS use `useEventListener` with `event.code`** for keyboard shortcuts to support any keyboard layout
- **NEVER use `useMagicKeys`** - it checks `event.key` which varies by keyboard layout (Hebrew, Russian, etc.)
- **NEVER use `event.key`** for shortcuts - use `event.code` which represents physical key position
- Use `useFocus` from VueUse to check if component has focus before responding to shortcuts
- Keep simple element-specific handlers (`@keydown.enter`, `@keydown.space`) in templates

**Example:**

```typescript
import { useEventListener, useFocus } from "@vueuse/core";

const containerRef = ref<HTMLElement>();
const { focused: hasFocus } = useFocus(containerRef);

// Keyboard shortcuts that work with any keyboard layout
useEventListener("keydown", (event: KeyboardEvent) => {
  if (!hasFocus.value) return;

  const hasCtrlOrMeta = event.ctrlKey || event.metaKey;

  // Use event.code (physical key) not event.key (character produced)
  if (hasCtrlOrMeta && event.code === "KeyF") {
    event.preventDefault();
    openSearch();
  }

  if (event.code === "ArrowDown") {
    navigateDown();
  }
});
```

**Why event.code instead of event.key:**

- `event.code`: Physical key position (always 'KeyA' regardless of layout)
- `event.key`: Character produced (varies: 'a' on English, 'ש' on Hebrew keyboard)
- Shortcuts must work on any keyboard layout, so use `event.code`

### Scroll Behavior

- **ALWAYS use `block: 'nearest'` with `scrollIntoView()`** to prevent aggressive scrolling
- `block: 'nearest'` only scrolls if the element is not visible and does minimal scroll
- **NEVER use `block: 'center'` or `block: 'start'`** - they can cause parent containers to scroll unexpectedly
- This prevents the entire app from shifting when scrolling to elements

**Examples:**

```typescript
// ✅ GOOD: Minimal scroll, only if needed
element.scrollIntoView({
  behavior: "smooth",
  block: "nearest",
  inline: "nearest",
});

// ❌ BAD: Aggressive centering can scroll parent containers
element.scrollIntoView({
  behavior: "smooth",
  block: "center",
});

// ❌ BAD: Forces alignment even if already visible
element.scrollIntoView({
  behavior: "smooth",
  block: "start",
});
```

**Why block: 'nearest' is safer:**

- Only scrolls if element is outside viewport
- Does minimal scroll to bring element into view
- Doesn't try to center or force specific alignment
- Reduces unwanted parent container scrolling

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

## State Management Patterns

### Centralized Computed State

When implementing features with multiple modes (global vs per-item), centralize the logic in the store:

**Pattern:**

```typescript
// ✅ GOOD: Single source of truth in store
const currentState = computed(() => {
  const settingsStore = useSettingsStore();

  if (settingsStore.globalMode) {
    return settingsStore.globalState;
  }

  return activeItem.value?.localState || defaultValue;
});

// Components just bind to this
const state = computed(() => store.currentState);
```

**Anti-pattern:**

```typescript
// ❌ BAD: Logic duplicated in every component
const state = computed(() => {
  if (settingsStore.globalMode) {
    return settingsStore.globalState;
  }
  return tabStore.activeTab?.localState || 0;
});
```

**Benefits:**

- Single source of truth
- No duplication across components
- Easier to maintain and test
- Consistent behavior everywhere

**Example: Diacritics State**

- Settings store manages the preference (`globalDiacritics`, `globalDiacriticsState`)
- Tab store provides centralized computed property (`currentDiacriticsState`)
- All UI components bind to `tabStore.currentDiacriticsState`
- Toggle logic in tab store updates appropriate state based on mode
