import { catalogBookTitle } from '@/stores/catalogBookTitle'
import { computed, ref, watch } from 'vue'
import { useTabStore } from '@/stores/tabStore'
import { notifyTabsChanged, notifyTabIcons, hasNativeChromeTabs } from './bridge'
import { rasterizeTabIcons, rasterizeWidgetTabIcons, widgetTabIconKey } from './rasterizeTabIcons'
import { WIDGET_ITEMS, HIDDEN_WIDGET_ITEMS, findWidgetItem } from '@/features/widgets/widgetItems'
import type { WidgetId } from '@/features/widgets/widgetWindowStore'
import { addinTabIconKey, rasterizeAddinTabIcons, rasterizeSiteTabIcons, siteTabIconKey } from './rasterizeSiteTabIcons'
import { loadWebsites, websiteForUrl, websitesRef } from '@/features/websites/websitesList'
import { iconKeyForEntry, isDocumentIconKey } from '@/utils/documentIcons'
import { onWebviewEvent } from './seforimDb'
import { openWidgetTab } from '@/features/widgets/openWidgetTab'
import { useWidgetWindowStore } from '@/features/widgets/widgetWindowStore'
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
    void Promise.all([rasterizeTabIcons(), rasterizeSiteTabIcons(sites), rasterizeAddinTabIcons(addinPaths), rasterizeWidgetTabIcons([...WIDGET_ITEMS, ...HIDDEN_WIDGET_ITEMS].filter((w) => !isDocumentIconKey(w.id)))]).then(([icons, siteIcons, addinIcons, widgetIcons]) => {
      siteIconKeys.value = new Set([...siteIcons, ...addinIcons].map((icon) => icon.key))
      const all = [...icons, ...siteIcons, ...addinIcons, ...widgetIcons]
      if (all.length) notifyTabIcons(all)
    })
  }
  sendTabIcons()
  void loadWebsites()
  watch(websitesRef, sendTabIcons)
  watch(openAddinPaths, sendTabIcons)
  // devicePixelRatio changes fire as a resize, not as their own event.
  window.addEventListener('resize', sendTabIcons)

  /**
   * A widget tab's icon: the widget's document icon (dictionary, calendar, units, a custom
   * page's file), else its own (clock, zmanim, learning).
   */
  function widgetIconKeyFor(t: Tab): string | undefined {
    if (t.route !== '/widget' || !t.widgetId) return undefined
    if (isDocumentIconKey(t.widgetId)) return t.widgetId
    const item = findWidgetItem(t.widgetId as WidgetId)
    if (item?.custom) return 'html'
    return widgetTabIconKey(t.widgetId)
  }

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

  // One snapshot per app window ('' = main, else an extra window this page draws), each
  // with its own tabs and active tab; the host routes it by windowKey to that window's strip.
  // Sorted by id so the strip keeps a stable visual order regardless of store order —
  // it only needs membership, titles and the active tab.
  const snapshotOf = (windowKey: string) => ({
    windowKey,
    tabs: tabStore.tabsIn(windowKey)
      .sort((a, b) => Number(a.id) - Number(b.id))
      .map((t): MirroredTab => ({
        id: t.id,
        title: catalogBookTitle(t),
        // Full breadcrumb for the native tab-list dropdown and tab tooltip; the
        // strip caption itself is always the plain title.
        listTitle: listTitleFor(t),
        // Favicon key into the set pushed by sendTabIcons — same shared mapping
        // the in-page lists use, so the strip shows the identical glyph.
        iconKey: widgetIconKeyFor(t) ?? siteIconKeyFor(t) ?? iconKeyForEntry(t),
      })),
    activeTabId: tabStore.activeIdIn(windowKey),
  })
  const snapshots = computed(() => ['', ...tabStore.extraWindowKeys].map(snapshotOf))

  const lastSent = new Map<string, string>()
  watch(
    snapshots,
    (snaps) => {
      for (const snap of snaps) {
        const json = JSON.stringify(snap)
        if (json === lastSent.get(snap.windowKey)) continue
        lastSent.set(snap.windowKey, json)
        notifyTabsChanged(snap)
      }
      for (const key of [...lastSent.keys()]) if (!snaps.some((s) => s.windowKey === key)) lastSent.delete(key)
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
        openNewStripTab(typeof msg.windowKey === 'string' ? msg.windowKey : '')
        break
      case 'moveTabToAppWindow':
        // A tab dragged to another window of this page, or out to a new one (null) - live.
        tabStore.moveTabToWindow(
          String(msg.tabId),
          typeof msg.windowKey === 'string' ? msg.windowKey : null,
          typeof msg.position === 'number' ? msg.position : -1,
        )
        break
      case 'closeAppWindow':
        // The reader closed a window this page draws (its X): its tabs close with it.
        if (typeof msg.windowKey === 'string') tabStore.closeWindow(msg.windowKey)
        break
      case 'openWindowForWaitingTabs':
        // No app window on screen (the calendar alone) and the app was launched: a window for
        // the waiting tabs (AppViewer.OpenWindowForWaitingTabs).
        tabStore.openWindowForWaitingTabs()
        break
      case 'openNewAppWindow':
        tabStore.openNewWindow()
        break
      case 'openCalendarAppWindow':
        // The installer's calendar shortcut (--calendar): the calendar widget
        // (opened, or brought to the front when it is open).
        useWidgetWindowStore().open('calendar', true)
        break
      case 'openWidgetAsTab':
        // A widget window dropped on this window's tab strip (AppViewer.OpenWidgetAsTab).
        if (typeof msg.widgetId === 'string') openWidgetTab(msg.widgetId)
        break
    }
  })
}
