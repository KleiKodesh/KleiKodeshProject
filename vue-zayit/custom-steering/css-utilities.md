---
inclusion: manual
---

# Available CSS Utilities

## Layout (layout.css)

- `.flex-center` - display: flex + justify-content: center + align-items: center
- `.flex-column` - display: flex + flex-direction: column
- `.flex-row` - display: flex + flex-direction: row + align-items: center + gap: 3px
- `.flex-between` - display: flex + justify-content: space-between + align-items: center
- `.flex-center-start` - display: flex + align-items: center + justify-content: flex-start
- `.flex-center-end` - display: flex + align-items: center + justify-content: flex-end
- `.justify-end` - display: flex + justify-content: flex-end
- `.flex-110` - flex: 1 1 0
- `.flex-11a` - flex: 1 1 auto
- `.height-fill` - height: 100%
- `.width-fill` - width: 100%
- `.fill` - width: 100% + height: 100%
- `.overflow-y` - overflow-y: auto
- `.overflow-x` - overflow-x: auto
- `.pos-relative` - position: relative
- `.pos-absolute` - position: absolute
- `.pos-absolute-fill` - position: absolute + inset: 0
- `.pos-absolute-overlay` - position: absolute + top: 0 + left: 0 + width: 100% + height: 100%
- `.rtl-flip` - transform: scaleX(-1)
- `.rotate-90` - transform: rotate(-90deg)

## Typography (typography.css)

- `.bold` - font-weight: bold
- `.text-primary` - color: var(--text-primary)
- `.text-secondary` - color: var(--text-secondary)

## Interactive (interactive.css)

- `.c-pointer` - cursor: pointer
- `.touch-interactive` - min-height: 44px + touch-action: manipulation

## Components (components.css)

- `.bar` - background: var(--bg-secondary) + border-bottom + padding: 0.5rem
- `.tree-node` - gap: 12px + padding: 12px 20px + min-height: 44px + touch-action: manipulation
- `.small-icon` - width: 12px + height: 12px
- `.setting-group` - padding: 14px 16px + border-bottom
- `.setting-label` - font-size: 14px + margin-bottom: 10px
- `.setting-value` - font-size: 13px + font-weight: normal
- `.button-group` - gap: 8px
- `.button-group.wrap` - flex-wrap: wrap
- `.toggle-btn` - flex: 1 + padding: 10px + background + border + border-radius + transitions
- `.toggle-btn.compact` - padding: 8px 10px + font-size: 12px
- `.toggle-btn.active` - background: var(--accent-color) + color: white
- `.btn-primary` - width: 100% + padding: 10px 12px + background: var(--accent-color) + color: white
- `.btn-icon` - width: 42px + height: 42px + background: var(--accent-color) + border-radius: 8px
- `.tab-btn` - flex: 1 + padding: 10px 6px + transitions
- `.tab-btn.active` - background: var(--hover-bg) + font-weight: 700
- `.section-header` - padding: 16px 16px 12px + font-size: 15px + font-weight: 600 + border-bottom

## Input (input.css)

- `input[type="text"]` - font-size + border + border-radius + padding + focus states + placeholder styling
- `input[type="range"]` - width: 100% + height: 6px + custom thumb styling
- `.input-secondary` - padding: 10px 12px + background: var(--bg-secondary) + border + border-radius: 8px

## Rules

- Add to appropriate file in src/assets/styles/ when CSS pattern appears 2+ times across components
- Update this file when new utilities are added
