import { reactive, ref } from 'vue'
import { showToast } from '@/composables/useToast'
import { t } from '@/i18n'
import { measureSiteFrameMemory } from '@/webview-host/bridge'
import { onWebviewEvent } from '@/webview-host/seforimDb'
import { webOrigin, websiteForUrl, websiteName } from './websitesList'
import { websitesSettings } from './websitesSettings'

/**
 * The live website iframes, one per web-view TAB - not per page component.
 *
 * An iframe reloads whenever it is moved in the DOM, so (like VS Code's webviews) each site's
 * iframe lives in one app-level layer (SiteFrameLayer.vue) for as long as its tab, and is only
 * positioned over the tab's placeholder (WebViewPage.vue) while that tab is shown. Switching tabs
 * hides it (visibility), so the site keeps everything: scroll, typed text, logins.
 *
 * Memory (measured: most sites 20-80 MB, a few 400-550 MB): a hidden site stays alive until
 *  - the reader unloads it (leave prompt / remembered choice / Settings), or
 *  - Windows reports low memory: the host pushes 'lowMemory' and the least recently used hidden
 *    site is unloaded, one per signal, like Firefox's tab unloader.
 * Never unloaded automatically: the shown site, one hidden less than PROTECT_RECENT_MS ago, one
 * still loading, one the reader typed in. An unloaded site reloads at its last page when shown.
 * A site is not loaded at all until its tab is first shown (startup costs nothing).
 */
export interface SiteFrame {
  tabId: string
  /** The iframe's name; the host's events name the frame by it. */
  name: string
  /** CSS anchor the layer positions the iframe on (set on WebViewPage's placeholder). */
  anchor: string
  paneId: 1 | 2
  /** The address the iframe was loaded with; changing it reloads the frame. */
  startUrl: string
  /** Where the reader is now (follows navigation inside the site; hosted only). */
  currentUrl: string
  /** The iframe exists. False = unloaded: it reloads at currentUrl when shown again. */
  live: boolean
  shown: boolean
  owner: symbol | null
  hiddenAt: number
  loading: boolean
  /** The reader typed in the site since it last loaded a page. */
  typed: boolean
  reloadNonce: number
}

const PROTECT_RECENT_MS = 3 * 60 * 1000

export const siteFrames = reactive(new Map<string, SiteFrame>())

/** The leave prompt currently shown (SiteLeavePrompt.vue), if any. */
export const leavePrompt = ref<{ tabId: string; siteName: string; origin: string; mb: number; typed: boolean } | null>(null)

/** Dev / tests only: memory per frame name when there is no host to measure it. */
export const devMemoryOverride: Record<string, number> = {}

function isProtected(frame: SiteFrame): boolean {
  return frame.shown || frame.loading || frame.typed || Date.now() - frame.hiddenAt < PROTECT_RECENT_MS
}

/** Shows the tab's site: creates (or reloads) its iframe and puts it over the placeholder. */
export function claimSiteFrame(tabId: string, owner: symbol, url: string, paneId: 1 | 2): SiteFrame {
  let frame = siteFrames.get(tabId)
  if (!frame) {
    frame = {
      tabId, name: `site-${tabId}`, anchor: `--site-${tabId}`, paneId, startUrl: url, currentUrl: url,
      live: true, shown: true, owner, hiddenAt: 0, loading: true, typed: false, reloadNonce: 0,
    }
    siteFrames.set(tabId, frame)
    return siteFrames.get(tabId)!
  }
  frame.owner = owner
  frame.paneId = paneId
  frame.shown = true
  if (leavePrompt.value?.tabId === tabId) leavePrompt.value = null
  if (!frame.live) {
    // Unloaded: back where the reader was.
    frame.startUrl = frame.currentUrl
    frame.live = true
    frame.loading = true
    frame.typed = false
  } else if (url !== frame.currentUrl) {
    // A new address from outside the site (Back/Forward, history, a pasted address).
    navigateSiteFrame(frame, url)
  }
  return frame
}

/** Hides the tab's site (tab switched away). Only the current owner can release it. */
export function releaseSiteFrame(tabId: string, owner: symbol): void {
  const frame = siteFrames.get(tabId)
  if (!frame || frame.owner !== owner) return
  frame.owner = null
  frame.shown = false
  frame.hiddenAt = Date.now()
  if (frame.live) void decideOnLeave(frame)
}

export function navigateSiteFrame(frame: SiteFrame, url: string): void {
  frame.startUrl = url
  frame.currentUrl = url
  frame.loading = true
  frame.typed = false
}

export function reloadSiteFrame(frame: SiteFrame): void {
  frame.reloadNonce++
  frame.loading = true
  frame.typed = false
}

export function unloadSiteFrame(tabId: string): void {
  const frame = siteFrames.get(tabId)
  if (!frame || frame.shown) return
  frame.live = false
  frame.loading = false
  if (leavePrompt.value?.tabId === tabId) leavePrompt.value = null
}

/** The tab closed (or no longer shows a site): its iframe goes. */
export function forgetSiteFrame(tabId: string): void {
  siteFrames.delete(tabId)
  if (leavePrompt.value?.tabId === tabId) leavePrompt.value = null
}

async function memoryByFrame(): Promise<Record<string, number>> {
  const measured = (await measureSiteFrameMemory()) ?? {}
  return { ...measured, ...devMemoryOverride }
}

/** On leaving a heavy site: the remembered choice, the Settings choice, or ask. */
async function decideOnLeave(frame: SiteFrame): Promise<void> {
  const origin = webOrigin(frame.currentUrl)
  const remembered = websitesSettings.remembered[origin]
  if (remembered === 'keep' || websitesSettings.leaveAction === 'keep' && !remembered) return
  const mb = (await memoryByFrame())[frame.name] ?? 0
  if (frame.shown || !frame.live) return // came back meanwhile
  if (remembered === 'unload' || websitesSettings.leaveAction === 'unload') {
    if (mb >= websitesSettings.heavyMb && !frame.typed && !frame.loading) unloadSiteFrame(frame.tabId)
    return
  }
  if (mb < websitesSettings.heavyMb) return
  const site = websiteForUrl(frame.currentUrl)
  leavePrompt.value = { tabId: frame.tabId, siteName: site ? websiteName(site) : origin, origin, mb: Math.round(mb), typed: frame.typed }
}

/** The leave prompt's answer. */
export function answerLeavePrompt(choice: 'keep' | 'unload', remember: boolean): void {
  const prompt = leavePrompt.value
  if (!prompt) return
  leavePrompt.value = null
  if (remember) websitesSettings.remembered[prompt.origin] = choice
  if (choice === 'unload') unloadSiteFrame(prompt.tabId)
}

/** Windows is low on memory: unload the least recently used unprotected hidden site, biggest first among equals. */
export async function unloadOneForLowMemory(): Promise<string | null> {
  const candidates = [...siteFrames.values()].filter((f) => f.live && !isProtected(f))
  if (!candidates.length) return null
  const memory = await memoryByFrame()
  candidates.sort((a, b) => a.hiddenAt - b.hiddenAt || (memory[b.name] ?? 0) - (memory[a.name] ?? 0))
  const victim = candidates[0]!
  if (isProtected(victim)) return null // shown again while measuring
  unloadSiteFrame(victim.tabId)
  const site = websiteForUrl(victim.currentUrl)
  showToast(t('websites.unloadedForMemory', { site: site ? websiteName(site) : webOrigin(victim.currentUrl) }), { duration: 6000 })
  return victim.tabId
}

onWebviewEvent((msg) => {
  if (msg.event === 'lowMemory') void unloadOneForLowMemory()
})
