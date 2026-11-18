import { serviceCall, serviceGetUrl } from '@/webview-host/serviceClient'

/**
 * A remote site icon served from the service's disk cache (Core WebsiteIconCache: refetched
 * weekly, stale copy kept when a refetch fails), instead of the browser asking the site each time.
 * Dev: the service's GET /website-icon URL (the browser caches it a day too). Hosted: the relay
 * is POST-only, so the bytes come through the `websiteIcon` op and become an object URL.
 */

/** What `websiteIcon` returns: one icon's bytes. */
interface WebsiteIconReply {
  contentType: string
  bytes: Uint8Array
}

/** Icon address → its source; one lookup per icon per session (object URLs live as long as the page). */
const sources = new Map<string, Promise<string | null>>()

/** The <img> source for remote icon `iconUrl`; null when it cannot be had (the caller shows the globe). */
export function cachedWebsiteIconSource(iconUrl: string): Promise<string | null> {
  let source = sources.get(iconUrl)
  if (!source) {
    source = lookUp(iconUrl)
    sources.set(iconUrl, source)
  }
  return source
}

async function lookUp(iconUrl: string): Promise<string | null> {
  try {
    const url = await serviceGetUrl(`/website-icon?url=${encodeURIComponent(iconUrl)}`)
    if (url) return url
    const reply = await serviceCall<WebsiteIconReply | undefined>('websiteIcon', { url: iconUrl })
    if (!reply?.bytes?.length) return null
    return URL.createObjectURL(new Blob([reply.bytes as BlobPart], { type: reply.contentType }))
  } catch {
    // The service is not reachable: load it from the site, as before the cache existed.
    sources.delete(iconUrl)
    return iconUrl
  }
}
