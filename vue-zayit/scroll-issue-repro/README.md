# Vite scrollIntoView Issue Reproduction

This is a minimal reproduction of the scrollIntoView positioning issue in Vite development mode.

## The Issue

When using `element.scrollIntoView({ behavior: 'auto', block: 'center' })` in Vite dev mode, the element scrolls to the wrong position. The issue does NOT occur in production builds.

## Setup

```bash
npm install
```

## Test in Development Mode (Issue Present)

```bash
npm run dev
```

Open http://localhost:5173 and click on navigation items. Notice the scroll position is incorrect - the selected item does not appear centered in the viewport.

## Test in Production Mode (Issue Absent)

```bash
npm run build
npm run preview
```

Open the preview URL and click on navigation items. The scroll position is now correct - items appear centered as expected.

## What to Look For

1. **Dev Mode**: Click "Section 50" - the item will NOT be centered in the blue content area
2. **Production**: Click "Section 50" - the item WILL be centered correctly
3. Check browser console for position logs showing the offset discrepancy

## Environment

- Vue: 3.5.22
- Vite: 7.1.11
- Browser: Any modern browser
- OS: Windows (but likely affects all platforms)

## Expected Behavior

The selected item should scroll to the center of the content area (blue border).

## Actual Behavior (Dev Mode)

The selected item scrolls to an incorrect position, often significantly off-center.
