---
inclusion: fileMatch
fileMatchPattern: '**/*.js|**/*.vue'
---

# JavaScript Standards

## Function-Based Architecture
**Avoid ES6 classes** - Use functions for better flexibility:

```javascript
// ✅ GOOD - Function-based approach
function createComponent() {
    const state = { /* ... */ };
    
    function initialize() { /* ... */ }
    
    return { initialize, show, hide };
}

// ❌ AVOID - ES6 classes
class Component { /* ... */ }
```

## Data Flow Architecture
**Client-Server Model**: JavaScript UI = Program, C# = Server

### Three Primary Actions
1. **SEARCH** - Find matches in document
2. **REPLACE** - Replace current match  
3. **REPLACE ALL** - Replace all matches

## State Management Rules
- **State lives ONLY in**: UI elements, JavaScript variables, DOM attributes
- **State is NEVER**: Synchronized to C#, sent outside search/replace operations
- **C# Communication ONLY during**: Search, replace, replaceAll operations

## UI Component Responsibilities
- **Color Picker**: Handle color selection, store data in UI, no C# communication
- **Clear Buttons**: Immediate UI reset, no C# communication
- **Regex Tips**: Pure UI helper, insert patterns to focused input