import { catalogBookTitle } from '@/stores/catalogBookTitle'
import { computed, ref, watch } from 'vue'
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { notifyTabsChanged, notifyTabIcons, hasNativeChromeTabs } from './bridge'
import { rasterizeTabIcons } from './rasterizeTabIcons'
import { addinTabIconKey, rasterizeAddinTabIcons, rasterizeSiteTabIcons, siteTabIconKey } from './rasterizeSiteTabIcons'
import { loadWebsites, websiteForUrl, websitesRef } from '@/features/websites/websitesList'
import { iconKeyForEntry } from '@/utils/documentIcons'
import { onWebviewEvent } from './seforimDb'
import {
  activateTabAnyPane,
  closeTabAnyPane,
  openNewTabInPane,
} from '@/composables/useCrossPaneTabActions'
import type { Tab } from '@/stores/tabStore'
import type { MirroredTab } from './bridge'

/**
 * Mirrors the Vue tab store to the native chrome tab strip (FluentChromeTabsForm
 * hosted by C#) and applies native strip gestures back onto the store.
 *
 * Vue is the source of truth:
 *  - Outbound: any change to pane-1 tab membership, titles, or the active tab
 *    sends a full snapshot via the 'tabsChanged' bridge action. The strip
 *    mirrors pane 1's view of the world (tabStore.pane1Tabs): its own tabs,
 *    plus pane-2 orphans adopted while split view is off. Orphans activate
 *    in pane 1 — never reopening the split shell — and return to pane 2 when
 *    it reopens.
 *  - Inbound: strip gestures arrive as push events and are applied to the store,
 *    which in turn produces the next snapshot:
 *      chromeTabActivated      { tabId } — user clicked a strip tab
 *      chromeTabCloseRequested { tabId } — user clicked a strip close button
 *      chromeTabNewRequested             — user clicked "+" (or Ctrl+T on the strip)
 *
 * Call once from main.ts after the stores are initialized. No-op in dev browser
 * and in the VSTO task pane (no native tab strip there).
 */

/** "title · tocPath" — the string shown in the dropdown row and tab tooltip. */
function listTitleFor(t: Tab): string {
  const title = catalogBookTitle(t)
  return t.tocPath ? `${title} · ${t.tocPath}` : title
}

export function initTabMirror(): void {
  if (!hasNativeChromeTabs) return

  const tabStore = useTabStore()
  const bookViewStore = useBookViewStore()

  // Both panes' visible tabs (pane1Tabs is split-aware: it includes adopted
  // orphans while split view is off; pane2Tabs is empty then). Sorted by id so the
  // strip keeps a stable visual order regardless of store order — it only needs
  // membership, titles, split state, and the per-pane active tabs.
  // Window resizes move the rendered divider without touching any store state —
  // bump a tick so the snapshot recomputes and the divider is re-measured.
  // Only with split view on: there is no divider otherwise, and the bump rebuilt every
  // tab's snapshot (titles, breadcrumbs, icons) on each resize frame for nothing.
  const resizeTick = ref(0)
  window.addEventListener('resize', () => {
    if (bookViewStore.splitViewEnabled) resizeTick.value++
  })

  // ── Favicons ────────────────────────────────────────────────────────────────
  // Rasterized once for the whole set and referenced by key, so tab snapshots stay
  // small however often they fire. Re-sent when the device pixel ratio changes
  // (moving the window to a differently-scaled monitor), because the bitmaps are
  // rendered for the exact pixel size the strip draws them at — that is what keeps
  // them crisp instead of scaled-and-soft.
  // Websites add their own icons ("site:<id>"); the strip takes the whole set at once, so both
  // kinds are sent together, again whenever the websites list arrives.
  const siteIconKeys = ref(new Set<string>())
  let lastRatio = 0
  let lastSiteCount = -1
  let lastAddinPaths = ''
  // Otzaria addins add theirs ("addin:<entry path>") for every addin open in a tab.
  const openAddinPaths = computed(() =>
    [...new Set([...tabStore.pane1Tabs, ...tabStore.pane2Tabs]
      .filter((t) => t.route === '/html-view' && t.isOtzariaAddin && t.localFilePath)
      .map((t) => t.localFilePath!))].sort(),
  )
  function sendTabIcons() {
    const ratio = window.devicePixelRatio || 1
    const sites = websitesRef.value
    const addinPaths = openAddinPaths.value
    const addinKey = addinPaths.join('|').toLowerCase()
    if (ratio === lastRatio && sites.length === lastSiteCount && addinKey === lastAddinPaths) return
    lastRatio = ratio
    lastSiteCount = sites.length
    lastAddinPaths = addinKey
    void Promise.all([rasterizeTabIcons(), rasterizeSiteTabIcons(sites), rasterizeAddinTabIcons(addinPaths)]).then(([icons, siteIcons, addinIcons]) => {
      siteIconKeys.value = new Set([...siteIcons, ...addinIcons].map((icon) => icon.key))
      const all = [...icons, ...siteIcons, ...addinIcons]
      if (all.length) notifyTabIcons(all)
    })
  }
  sendTabIcons()
  void loadWebsites()
  watch(websitesRef, sendTabIcons)
  watch(openAddinPaths, sendTabIcons)
  // devicePixelRatio changes fire as a resize, not as their own event.
  window.addEventListener('resize', sendTabIcons)

  /** A website's or Otzaria addin's tab icon, once it is rasterized (else the route's globe / puzzle piece). */
  function siteIconKeyFor(t: Tab): string | undefined {
    if (t.route === '/html-view' && t.isOtzariaAddin && t.localFilePath) {
      const key = addinTabIconKey(t.localFilePath)
      return siteIconKeys.value.has(key) ? key : undefined
    }
    if (t.route !== '/web-view' || !t.webUrl) return undefined
    const site = websiteForUrl(t.webUrl, websitesRef.value)
    const key = site && siteTabIconKey(site)
    return key && siteIconKeys.value.has(key) ? key : undefined
  }

  const snapshot = computed(() => {
    void resizeTick.value
    const splitOn = bookViewStore.splitViewEnabled
    return {
      tabs: [...tabStore.pane1Tabs, ...tabStore.pane2Tabs]
        .sort((a, b) => Number(a.id) - Number(b.id))
        .map((t): MirroredTab => ({
          id: t.id,
          title: catalogBookTitle(t),
          // Full breadcrumb for the native tab-list dropdown and tab tooltip; the
          // strip caption itself is always the plain title.
          listTitle: listTitleFor(t),
          pane: t.pane === 2 ? 2 : 1,
          // Favicon key into the set pushed by sendTabIcons — same shared mapping
          // the in-page lists use, so the strip shows the identical glyph.
          iconKey: siteIconKeyFor(t) ?? iconKeyForEntry(t),
        })),
      activeTabId: tabStore.activeTabId,
      pane2ActiveTabId: splitOn ? tabStore.pane2ActiveTabId : '',
      splitView: splitOn,
      focusedPane: (splitOn ? bookViewStore.focusedPaneId : 1) as 1 | 2,
      // Raw store fraction as the reactive dependency; the watcher replaces it with
      // the MEASURED divider center before sending (see dividerCenterFraction).
      splitFraction: bookViewStore.splitViewFraction,
    }
  })

  // The strip divider aligns to the DEVICE PIXELS the browser actually rendered
  // for .split-divider — no fraction×width rounding, so alignment holds at every
  // window width. getBoundingClientRect × devicePixelRatio recovers the snapped
  // pixel bounds (the webview's device width equals the form's client width).
  // lastDividerDelta converts the strip's center-fraction drag reports back to
  // the store fraction.
  let lastDividerDelta = 0
  function measureDividerDevice(storeFraction: number): { left: number; width: number } {
    // Keyed on the data attribute, not a class: the divider's classes are the shared
    // `.sash` styling ones, and this measurement must not break the next time they
    // change (it did once - a styling rename silently dropped the native strip's
    // divider alignment to the -1 fallback).
    const el = document.querySelector('[data-split-divider]')
    if (el) {
      const rect = el.getBoundingClientRect()
      if (rect.width > 0) {
        lastDividerDelta = (rect.left + rect.width / 2) / window.innerWidth - storeFraction
        const dpr = window.devicePixelRatio
        const left = Math.round(rect.left * dpr)
        return { left, width: Math.max(1, Math.round(rect.right * dpr) - left) }
      }
    }
    lastDividerDelta = 2 / window.innerWidth
    return { left: -1, width: 0 }
  }

  let lastSent = ''
  watch(
    snapshot,
    (snap) => {
      // flush: 'post' — the DOM has re-rendered, so the divider element measures true
      const divider = snap.splitView
        ? measureDividerDevice(snap.splitFraction)
        : { left: -1, width: 0 }
      const payload = {
        ...snap,
        splitDividerLeftPx: divider.left,
        splitDividerWidthPx: divider.width,
      }
      const json = JSON.stringify(payload)
      if (json !== lastSent) {
        lastSent = json
        notifyTabsChanged(payload)
      }
    },
    { immediate: true, flush: 'post' },
  )

  onWebviewEvent((msg) => {
    switch (msg.event) {
      case 'chromeTabActivated':
        activateTabAnyPane(String(msg.tabId))
        break
      case 'chromeTabCloseRequested':
        closeTabAnyPane(String(msg.tabId))
        break
      case 'chromeTabNewRequested':
        openNewTabInPane(msg.pane === 2 ? 2 : 1)
        break
      case 'chromeTabMovedToPane': {
        // Cross-region drag in the split strip — move the tab between Vue panes and focus
        // the pane it landed in.
        const pane = msg.pane === 2 ? 2 : 1
        tabStore.moveTabToPane(String(msg.tabId), pane)
        bookViewStore.setFocusedPane(pane)
        break
      }
      case 'chromeSplitFractionChanged': {
        // Live drag of the native strip divider — resize the split panes in tandem.
        // The strip reports the divider CENTER; convert back to the store fraction
        // using the same center↔fraction delta the outbound snapshot measured,
        // and clamp like the Vue divider's own drag.
        const fraction = Number(msg.fraction) - lastDividerDelta
        if (Number.isFinite(fraction)) {
          bookViewStore.setSplitViewFraction(Math.min(0.85, Math.max(0.15, fraction)))
        }
        break
      }
    }
  })
}
