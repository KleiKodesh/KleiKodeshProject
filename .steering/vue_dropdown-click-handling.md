---
inclusion: fileMatch
fileMatchPattern: '**/*.vue'
---

# Vue Dropdown Click Handling

## CRITICAL: Event Bubbling and Click Propagation

When implementing dropdowns that should toggle on header clicks but not on button clicks, proper event handling is essential to prevent conflicts.

## Common Problem: Dropdown Immediately Closes

**Symptom**: Dropdown toggles to visible but immediately closes
**Cause**: Click event bubbles from header to parent elements that have close handlers

```vue
<!-- ❌ WRONG: Event bubbles and closes dropdown -->
<div @click="closeDropdown">
  <div @click="toggleDropdown">
    Header content
  </div>
</div>
```

**Solution**: Add `@click.stop` to prevent bubbling

```vue
<!-- ✅ CORRECT: Prevents event bubbling -->
<div @click="closeDropdown">
  <div @click.stop="toggleDropdown">
    Header content
  </div>
</div>
```

## Button Container Click Handling

**Problem**: Buttons inside containers with `@click.stop` prevent header clicks from working

```vue
<!-- ❌ WRONG: Container stops all clicks -->
<div @click.stop="toggleDropdown">
  <div @click.stop>  <!-- This prevents header clicks -->
    <button @click="buttonAction">Button</button>
  </div>
</div>
```

**Solution**: Apply `@click.stop` only to individual buttons

```vue
<!-- ✅ CORRECT: Only buttons stop propagation -->
<div @click.stop="toggleDropdown">
  <div>  <!-- No @click.stop here -->
    <button @click.stop="buttonAction">Button</button>
  </div>
</div>
```

## Complete Pattern

```vue
<template>
  <div class="header-container">
    <!-- Header with dropdown toggle -->
    <div class="tab-header" @click.stop="handleHeaderClick">
      
      <!-- Button containers without @click.stop -->
      <div class="button-group">
        <button @click.stop="handleButton1">Button 1</button>
        <button @click.stop="handleButton2">Button 2</button>
      </div>
      
      <!-- Clickable title area -->
      <span class="title">{{ title }}</span>
      
      <!-- More buttons -->
      <div class="button-group">
        <button @click.stop="handleButton3">Button 3</button>
      </div>
    </div>
    
    <!-- Dropdown with proper positioning -->
    <div v-if="isVisible" class="dropdown">
      <!-- Dropdown content -->
    </div>
  </div>
</template>

<style scoped>
.header-container {
  position: relative; /* Required for dropdown positioning */
}

.dropdown {
  position: absolute;
  top: 100%;
  /* Other dropdown styles */
}
</style>
```

## Key Rules

1. **Header container**: `@click.stop` to prevent bubbling to parent close handlers
2. **Button containers**: NO `@click.stop` - let header handle clicks on empty space
3. **Individual buttons**: `@click.stop` to prevent triggering dropdown
4. **Dropdown container**: `position: relative` on parent for proper positioning
5. **Dropdown element**: `position: absolute` with `top: 100%`

## Debugging Tips

Add temporary logging to track event flow:
- Header clicks should emit toggle events
- Button clicks should NOT emit toggle events
- Dropdown state should change correctly
- No immediate close after toggle