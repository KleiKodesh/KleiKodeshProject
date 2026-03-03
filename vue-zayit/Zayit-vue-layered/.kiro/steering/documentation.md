---
inclusion: always
---

# Documentation Guidelines

## Minimal Documentation Principle

**Before**: ~400+ lines across multiple files
**After**: ~80 lines in one file

This is the standard. Keep documentation concise and consolidated.

## Rules

1. **One file per feature/module** - Maximum one ARCHITECTURE.md per feature
2. **Prefer inline comments** - JSDoc and code comments over separate files
3. **Only when needed** - Create docs only when explicitly requested or for complex architecture
4. **Update, don't create** - If docs exist, update them instead of creating new ones

## What NOT to Create

❌ Multiple documentation files:
- SUMMARY.md
- MIGRATION.md
- VERIFICATION.md
- CLEANUP.md
- GUIDE.md
- INTEGRATION.md (unless external system)

✅ One comprehensive file:
- ARCHITECTURE.md (architecture + integration + usage)

## Documentation Style

When creating documentation:
- **Concise and direct** - No fluff
- **Code examples** - Show, don't tell
- **"How to use"** - Not "what was done"
- **Keep updated** - Update with code changes

## Example Structure

```markdown
# Feature Architecture

## Overview
Brief description

## Usage
Code examples

## Architecture
Diagram or flow

## Integration (if external system)
Implementation guide
```

That's it. No more.
