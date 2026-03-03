# File Length and Refactoring Policy

## Critical Rule: File Length Limits

**If a file exceeds reasonable length, refactoring is REQUIRED before adding new code.**

### Length Thresholds

- **Vue Components**: 400 lines (template + script + style combined)
- **TypeScript/JavaScript**: 500 lines
- **C# Classes**: 500 lines
- **Any file**: 1000 lines is a hard stop

### Why This Matters

Long files indicate:

- Multiple responsibilities mixed together
- Lack of proper abstraction
- Difficult to understand and maintain
- High cognitive load for developers
- Increased risk of bugs and conflicts

### Required Actions When File is Too Long

1. **Stop and assess** - Don't add more code to an already bloated file
2. **Identify responsibilities** - What distinct concerns does this file handle?
3. **Extract components/classes** - Split into focused, single-purpose units
4. **Create utilities** - Move reusable logic to shared utilities
5. **Simplify** - Remove duplication and dead code first

### Refactoring Strategies

#### Vue Components

```typescript
// ❌ BAD: 600-line component doing everything
<template>
  <!-- Complex UI with multiple features -->
</template>

<script setup lang="ts">
// Feature A logic (100 lines)
// Feature B logic (150 lines)
// Feature C logic (120 lines)
// Shared utilities (80 lines)
// API calls (100 lines)
</script>

// ✅ GOOD: Split into focused components
// ParentComponent.vue (150 lines)
// FeatureA.vue (120 lines)
// FeatureB.vue (130 lines)
// FeatureC.vue (110 lines)
// composables/useSharedLogic.ts (80 lines)
// api/featureApi.ts (100 lines)
```

#### C# Classes

```csharp
// ❌ BAD: 800-line god class
public class BookManager
{
    // Database operations (200 lines)
    // UI state management (150 lines)
    // File I/O (180 lines)
    // Search logic (200 lines)
    // Configuration (70 lines)
}

// ✅ GOOD: Separated concerns
public class BookRepository { } // 180 lines - DB only
public class BookSearchService { } // 220 lines - Search only
public class BookFileManager { } // 200 lines - File I/O only
public class BookStateManager { } // 150 lines - State only
```

### Common Extraction Patterns

1. **Composables** (Vue) - Extract reusable reactive logic
2. **Utility functions** - Pure functions with no side effects
3. **Service classes** - Business logic and API calls
4. **Child components** - UI sections that can stand alone
5. **Constants/Types** - Move to dedicated files

### Red Flags That Demand Refactoring

🚩 Scrolling through multiple screens to understand one file
🚩 Multiple unrelated imports at the top
🚩 Comments like "Section A", "Section B" dividing the file
🚩 Difficulty finding where specific functionality lives
🚩 Fear of touching the file because it might break something
🚩 Multiple developers avoiding working on the same file

### Enforcement

- **Before adding features**: Check file length first
- **During code review**: Reject PRs that make long files longer
- **Regular audits**: Identify and refactor long files proactively
- **No exceptions**: "Just this once" leads to permanent technical debt

### The Rule

**If you're about to add code to a file that's already too long, STOP. Refactor first, then add your feature to the appropriate extracted component/class.**

This is not optional. Long files are a form of technical debt that compounds over time.

## Folder Nesting Policy

### Critical Rule: Avoid Deep Nesting

**Keep folder structures flat and avoid unnecessary nesting levels.**

### Maximum Nesting Depth

- **Components**: Maximum 2 levels deep from `src/components/`
  - ✅ GOOD: `src/components/book/BookView.vue`
  - ✅ GOOD: `src/components/icons/DiacriticsIcon.vue`
  - ❌ BAD: `src/components/shared/icons/DiacriticsIcon.vue` (3 levels)
  - ❌ BAD: `src/components/book/toolbar/buttons/ZoomButton.vue` (4 levels)

### Why Flat Structure Matters

Deep nesting causes:

- Harder to find files
- Longer import paths
- More cognitive overhead
- Difficult to navigate codebase
- Unclear organization boundaries

### Folder Organization Rules

1. **Colocation over nesting** - Keep related files together at the same level
2. **Feature folders** - Group by feature/domain, not by type
3. **Flat is better** - If you need more than 2 levels, reconsider your organization
4. **No "utils/helpers/common" subfolders** - Keep utilities at the top level of their domain

### Examples

```
✅ GOOD: Flat structure
src/
  components/
    book/
      BookView.vue
      LineView.vue
      useBookViewer.ts
      useToc.ts
    icons/
      DiacriticsIcon.vue
      CommentaryIcon.vue
    shared/
      Button.vue
      Dialog.vue
      useDialog.ts

❌ BAD: Deep nesting
src/
  components/
    shared/
      icons/              ← Unnecessary nesting
        DiacriticsIcon.vue
      dialogs/            ← Unnecessary nesting
        CustomDialog.vue
    book/
      components/         ← Unnecessary nesting
        LineView.vue
      composables/        ← Unnecessary nesting
        useBookViewer.ts
```

### When to Create a Subfolder

Only create a subfolder when:

1. You have 10+ files in a single folder that naturally group together
2. The grouping represents a clear subdomain or feature
3. The files in the subfolder are tightly coupled and rarely used outside

### Refactoring Deep Nesting

If you find deep nesting:

1. Move files up to parent folder
2. Use clear naming to indicate relationships (e.g., `BookLineView.vue` instead of `book/line/View.vue`)
3. Colocate related files (component + composable + types in same folder)
4. Update imports to use flatter paths
