import { catalogBookTitle } from '@/stores/catalogBookTitle'
import { computed, ref, watch } from 'vue'
import { useTabStore } from '@/stores/tabStore'
import { notifyTabsChanged, notifyTabIcons, hasNativeChromeTabs } from './bridge'
import { rasterizeTabIcons } from './rasterizeTabIcons'
import { addinTabIconKey, rasterizeAddinTabIcons, rasterizeSiteTabIcons, siteTabIconKey } from './rasterizeSiteTabIcons'
import { loadWebsites, websiteForUrl, websitesRef } from '@/features/websites/websitesList'
import { iconKeyForEntry } from '@/utils/documentIcons'
import { onWebviewEvent } from './seforimDb'
import {
  activateStripTab,
  closeStripTab,
  openNewStripTab,
} from '@/composables/stripTabActions'
import type { Tab } from '@/stores/tabStore'
import type { MirroredTab } from './bridge'

/**
 * Mirrors the Vue tab store to the native chrome tab strip (FluentChromeTabsForm
 * hosted by C#) and applies native strip gestures back onto the store.
 *
 * Vue is the source of truth:
 *  - Outbound: any change to tab membership, titles, or the active tab sends a
 *    full snapshot via the 'tabsChanged' bridge action.
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
    [...new Set(tabStore.tabs
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

  // Sorted by id so the strip keeps a stable visual order regardless of store order —
  // it only needs membership, titles and the active tab.
  const snapshot = computed(() => ({
    tabs: [...tabStore.tabs]
      .sort((a, b) => Number(a.id) - Number(b.id))
      .map((t): MirroredTab => ({
        id: t.id,
        title: catalogBookTitle(t),
        // Full breadcrumb for the native tab-list dropdown and tab tooltip; the
        // strip caption itself is always the plain title.
        listTitle: listTitleFor(t),
        // Favicon key into the set pushed by sendTabIcons — same shared mapping
        // the in-page lists use, so the strip shows the identical glyph.
        iconKey: siteIconKeyFor(t) ?? iconKeyForEntry(t),
      })),
    activeTabId: tabStore.activeTabId,
  }))

  let lastSent = ''
  watch(
    snapshot,
    (snap) => {
      const json = JSON.stringify(snap)
      if (json !== lastSent) {
        lastSent = json
        notifyTabsChanged(snap)
      }
    },
    { immediate: true, flush: 'post' },
  )

  onWebviewEvent((msg) => {
    switch (msg.event) {
      case 'chromeTabActivated':
        activateStripTab(String(msg.tabId))
        break
      case 'chromeTabCloseRequested':
        closeStripTab(String(msg.tabId))
        break
      case 'chromeTabNewRequested':
        openNewStripTab()
        break
    }
  })
}
