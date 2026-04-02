---
inclusion: manual
---

# Virtual Scroller — Scroll Save & Restore

Correct pattern for TanStack Virtual (`@tanstack/vue-virtual`) scroll persistence. Hard-won from extensive debugging of `BookViewLinesContent.vue`.

## The Core Problem

`virtualizer.scrollToIndex(n, { align: 'start' })` uses **estimated** item sizes. On first load (cold virtualizer), estimates are wrong — it lands 10–20 items short of the target. This only affects initial restore, not live navigation (TOC/commentary), because by then items are already measured.

Additionally, `scrollToIndex` triggers a TanStack-internal post-render scroll correction (+`item.size` px). Any `scrollTop` set during that correction window gets overwritten.

## Saving — captureScrollPos

```ts
function captureScrollPos() {
  const first = virtualItems.value[0]
  if (!first || !scrollerEl.value) return null
  // virtualItems[0] may be an overscan item above the viewport — that's fine.
  // scrollOffset = scrollTop - first.start encodes the exact position relative to it.
  // On restore: item.start + scrollOffset = exact original scrollTop.
  return {
    scrollIndex: first.index,
    scrollOffset: Math.max(0, scrollerEl.value.scrollTop - first.start),
  }
}
```

`Math.max(0, ...)` guards against fractional pixel rounding where `scrollTop` could be slightly less than `first.start`.

## Restoring — restoreScrollPos

```ts
function restoreScrollPos(lineIndex: number, scrollOffset = 0) {
  // Step 1: scrollToIndex brings items near lineIndex into the render window so TanStack
  // measures them. It also fires TanStack's internal post-render scroll correction.
  // Step 2: Wait one rAF for that correction to settle — TanStack is now idle.
  // Step 3: Set scrollTop directly using the real measured item.start from measurementsCache.
  //         This sticks because TanStack won't correct again once it's idle.
  // programmaticScrolling suppresses savePos during restore frames.
  programmaticScrolling = true
  virtualizer.value.scrollToIndex(lineIndex, { align: 'start' })
  requestAnimationFrame(() => {
    const item = virtualizer.value.measurementsCache.find((m) => m.index === lineIndex)
    if (item && scrollerEl.value) scrollerEl.value.scrollTop = item.start + scrollOffset
    requestAnimationFrame(() => {
      programmaticScrolling = false
    })
  })
}
```

**Why not `scrollToOffset`?** It goes through TanStack's scroll API which re-triggers the correction. Direct `scrollTop =` after the rAF works because TanStack is no longer in a scrolling state.

**Why not multiple rAF passes?** The correction only happens once, on the `scrollToIndex` call. After one rAF it's done — a second pass is not needed.

## When to Save

Do NOT save on every scroll event. Save only on lifecycle boundaries:

```ts
// Tab switch / app going to background / WebView losing focus
useEventListener(document, 'visibilitychange', () => {
  if (document.visibilityState === 'hidden') savePos()
})
// Page refresh / browser close — onBeforeUnmount does NOT fire on refresh
useEventListener(window, 'beforeunload', savePos)
```

Also guard `savePos` against firing during restore:

```ts
function savePos() {
  if (programmaticScrolling) return // don't save intermediate restore positions
  const pos = captureScrollPos()
  // ...
}
```

## Parent Must Pre-fetch IDB Before Mounting

The component must receive `initialScrollIndex` + `initialScrollOffset` as **props** — never fetch IDB internally. If it fetches IDB itself, there's a race: lines may load before the IDB read completes, so `watch(lines)` fires with no target.

Pattern in `BookViewPage.vue`:

```ts
const initialScrollTop = ref<number | undefined>() // reusing name — holds scrollIndex
const initialScrollOffset = ref<number>(0)
const scrollStateReady = ref(openTocLineIndex != null) // TOC nav: skip IDB wait

onMounted(async () => {
  const bookSaved = await tabStore.getBookViewState(tabId, bookId)
  const lastRead = await tabStore.getLastReadPos(bookId)
  if (openTocLineIndex == null) {
    const scrollIndex = bookSaved?.scrollIndex ?? lastRead?.scrollIndex
    const scrollOffset = bookSaved?.scrollOffset ?? lastRead?.scrollOffset
    if (scrollIndex != null) {
      initialScrollTop.value = scrollIndex
      initialScrollOffset.value = scrollOffset ?? 0
    }
  }
  scrollStateReady.value = true // now mount the component
})
```

```html
<!-- Gate on scrollStateReady so props are set before first render -->
<BookViewLinesContent
  v-if="scrollStateReady"
  :initial-scroll-index="initialScrollTop"
  :initial-scroll-offset="initialScrollOffset"
/>
```

## scrollToLineId — Visibility Check

`scrollToLineId` (TOC and commentary navigation) intentionally skips scrolling if the line is already fully visible:

```ts
if (vItem.start >= viewTop && vItem.start + vItem.size <= viewBottom) return
```

This avoids jarring jumps when the in-app search bar navigates between results already on screen. Do NOT remove this check. Do NOT use `scrollToLineId` for scroll restore.
