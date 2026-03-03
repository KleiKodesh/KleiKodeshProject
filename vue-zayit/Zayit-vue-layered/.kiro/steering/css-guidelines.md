# CSS Guidelines

## Utility-First Approach

1. **Check existing utilities first** - Review global stylesheets before creating new CSS
2. **Prefer global over scoped** - Use utility classes when possible, scoped CSS only for component-specific styles
3. **No duplication** - Don't recreate existing styles or browser defaults
4. **Combine utilities** - Prefer combining utility classes over writing new scoped styles

## Class Ordering

Order classes from generic to specific:
1. Layout (flex-row, flex-column, flex-center)
2. Sizing (height-fill, width-fill, overflow-y)
3. Interactive states (hover-bg, focus-accent, click-effect)
4. Cursor (c-pointer)
5. Typography (bold, text-secondary, smaller-em)
6. Component utilities (bar, tree-node, reactive-icon)
7. Component-specific classes (last)

Example: `class="flex-row flex-110 hover-bg focus-accent c-pointer bold tree-node custom-class"`

## Layout Responsibility

**Parent controls child layout:**
- Parent applies: `flex-110`
- Child defines: Internal structure only

```vue
<!-- Good: Parent controls layout -->
<ChildComponent class="flex-110 height-fill" />

<!-- Child only defines internal structure -->
<template>
  <div class="flex-column">
    <div>Internal content</div>
  </div>
</template>
```

## Reactive Styling

**Icons reacting to parent state:**
```vue
<template>
  <div class="tree-node reactive-icon">
    <Icon />
  </div>
</template>

<style scoped>
.tree-node:hover :deep(svg) {
  fill: var(--accent-color);
}
</style>
```

## Minimal Changes

When fixing CSS issues:
- Only modify the specific problem
- Don't change unrelated styles or formatting
- Keep changes focused and minimal
