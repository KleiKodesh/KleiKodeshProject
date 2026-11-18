import { shallowRef } from 'vue'
import { uiLanguage } from '@/i18n'

/**
 * The Torah websites the app opens in its own tabs: public/websites.json, served beside
 * index.html. The host reads the same file (WebSitesFrameHeaderStripper.cs) to let every
 * listed domain load inside the web-view iframe, so this file is the one whitelist.
 */
export interface Website {
  id: string
  name: string
  /** English name, only for a site that has one. */
  nameEn?: string
  /** Where the site opens. */
  url: string
  /** Every address under these domains (and their subdomains) belongs to the site. */
  domains: string[]
  /** The site's icon when it is not at /favicon.ico. */
  icon?: string
}

const websites = shallowRef<Website[]>([])
let loading: Promise<Website[]> | null = null

/** The list, loaded once. Reactive: callers render `websitesRef.value` and it fills in. */
export function loadWebsites(): Promise<Website[]> {
  loading ??= fetch(`${import.meta.env.BASE_URL}websites.json`)
    .then((r) => (r.ok ? r.json() : { sites: [] }))
    .then((data: { sites?: Website[] }) => (websites.value = data.sites ?? []))
    .catch(() => (websites.value = []))
  return loading
}

export const websitesRef = websites

/** The name in the UI language: English when the UI is English and the site has an English name. */
export function websiteName(site: Website): string {
  return uiLanguage === 'en' && site.nameEn ? site.nameEn : site.name
}

/** The list in alphabetical order of the names shown. */
export function sortedWebsites(list: Website[]): Website[] {
  return [...list].sort((a, b) => websiteName(a).localeCompare(websiteName(b), uiLanguage))
}

/** The whitelisted site an address belongs to, or undefined (not allowed). */
export function websiteForUrl(url: string, list: Website[] = websites.value): Website | undefined {
  let host: string
  try {
    const parsed = new URL(url)
    if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') return undefined
    host = parsed.hostname.toLowerCase()
  } catch {
    return undefined
  }
  const matches = list.filter((site) => site.domains.some((d) => host === d || host.endsWith(`.${d}`)))
  // Two entries can share a domain (otzar.org and tablet.otzar.org): the one whose own address is this host names it.
  return matches.find((site) => new URL(site.url).hostname.toLowerCase() === host) ?? matches[0]
}

/** Turns typed text into an address when it looks like one ("otzar.org/x" → "https://otzar.org/x"). */
export function asWebAddress(text: string): string | undefined {
  const trimmed = text.trim()
  if (!trimmed || /\s/.test(trimmed)) return undefined
  if (/^https?:\/\//i.test(trimmed)) return trimmed
  if (/^[a-z0-9-]+(\.[a-z0-9-]+)+(\/.*)?$/i.test(trimmed)) return `https://${trimmed}`
  return undefined
}

/** The site's icon: its listed `icon`, else /favicon.ico (WebsiteFavicon falls back to the globe). */
export function faviconUrl(url: string): string {
  const listed = websiteForUrl(url)?.icon
  // A bare path is a file shipped beside index.html (public/websites-icons/).
  if (listed) return /^https?:/i.test(listed) ? listed : `${import.meta.env.BASE_URL}${listed}`
  try {
    return `${new URL(url).origin}/favicon.ico`
  } catch {
    return ''
  }
}

/** Websites whose name (Hebrew or English) or address contains the typed text - the address-bar rows. */
export function matchWebsites(query: string, list: Website[] = websites.value): Website[] {
  const q = query.trim().toLowerCase()
  if (q.length < 2) return []
  return sortedWebsites(list.filter((site) =>
    site.name.toLowerCase().includes(q) || (site.nameEn ?? '').toLowerCase().includes(q) || displayHost(site.url).includes(q)))
}

/** The site part of an address (scheme + host), for one history entry per site. */
export function webOrigin(url: string): string {
  try {
    return new URL(url).origin
  } catch {
    return url
  }
}

/** Short address shown under a site's name: host without "www.". */
export function displayHost(url: string): string {
  try {
    return new URL(url).hostname.replace(/^www\./, '')
  } catch {
    return url
  }
}
