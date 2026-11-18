import { cachedWebsiteIconSource } from '@/features/websites/cachedWebsiteIcon'
import { faviconUrl, type Website } from '@/features/websites/websitesList'

/**
 * Each website's own icon as a PNG for the native tab strip, keyed "site:<id>" (siteTabIconKey).
 * Drawn at the strip's exact pixel size, like rasterizeTabIcons. Remote icons come through the
 * service's icon cache (an object URL hosted, so the canvas stays readable); shipped ones are
 * same-origin. An icon that fails is left out - that tab keeps the globe.
 */
const ICON_LOGICAL_PX = 16

export const siteTabIconKey = (site: Website): string => `site:${site.id}`

function loadImage(src: string): Promise<HTMLImageElement | null> {
  return new Promise((resolve) => {
    const img = new Image()
    img.onload = () => resolve(img)
    img.onerror = () => resolve(null)
    img.src = src
  })
}

export async function rasterizeSiteTabIcons(sites: Website[]): Promise<{ key: string; png: string }[]> {
  const devicePx = Math.max(16, Math.round(ICON_LOGICAL_PX * (window.devicePixelRatio || 1)))
  const out: { key: string; png: string }[] = []
  for (const site of sites) {
    const icon = faviconUrl(site.url)
    const src = /^https?:/i.test(icon) ? await cachedWebsiteIconSource(icon) : icon
    if (!src) continue
    const img = await loadImage(src)
    if (!img) continue
    try {
      const canvas = document.createElement('canvas')
      canvas.width = devicePx
      canvas.height = devicePx
      const ctx = canvas.getContext('2d')
      if (!ctx) continue
      ctx.imageSmoothingQuality = 'high'
      ctx.drawImage(img, 0, 0, devicePx, devicePx)
      out.push({ key: siteTabIconKey(site), png: canvas.toDataURL('image/png') })
    } catch {
      // A cross-origin image taints the canvas: that tab keeps the globe.
    }
  }
  return out
}
