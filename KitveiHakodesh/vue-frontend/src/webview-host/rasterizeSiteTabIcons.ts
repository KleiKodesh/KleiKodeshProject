import { cachedWebsiteIconSource } from '@/features/websites/cachedWebsiteIcon'
import { faviconUrl, type Website } from '@/features/websites/websitesList'
import { otzariaAddinIconSource } from '@/features/websites/otzariaAddinsList'

/**
 * Each website's (and Otzaria addin's) own icon as a PNG for the native tab strip, keyed "site:<id>" (siteTabIconKey).
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

/** One image drawn at the strip's pixel size as a PNG data URL; null when it fails to load or taints the canvas. */
async function rasterizeIcon(src: string): Promise<string | null> {
  const devicePx = Math.max(16, Math.round(ICON_LOGICAL_PX * (window.devicePixelRatio || 1)))
  const img = await loadImage(src)
  if (!img) return null
  try {
    const canvas = document.createElement('canvas')
    canvas.width = devicePx
    canvas.height = devicePx
    const ctx = canvas.getContext('2d')
    if (!ctx) return null
    ctx.imageSmoothingQuality = 'high'
    ctx.drawImage(img, 0, 0, devicePx, devicePx)
    return canvas.toDataURL('image/png')
  } catch {
    // A cross-origin image taints the canvas: that tab keeps its route icon.
    return null
  }
}

export async function rasterizeSiteTabIcons(sites: Website[]): Promise<{ key: string; png: string }[]> {
  const out: { key: string; png: string }[] = []
  for (const site of sites) {
    const icon = faviconUrl(site.url)
    const src = /^https?:/i.test(icon) ? await cachedWebsiteIconSource(icon) : icon
    if (!src) continue
    const png = await rasterizeIcon(src)
    if (png) out.push({ key: siteTabIconKey(site), png })
  }
  return out
}

/** An Otzaria addin tab's icon key: its entry point, case-folded. */
export const addinTabIconKey = (entryPath: string): string => `addin:${entryPath.toLowerCase()}`

/** Each addin's own manifest icon for the native tab strip; an addin without one keeps the puzzle piece. */
export async function rasterizeAddinTabIcons(entryPaths: string[]): Promise<{ key: string; png: string }[]> {
  const out: { key: string; png: string }[] = []
  for (const path of entryPaths) {
    const src = await otzariaAddinIconSource(path)
    if (!src) continue
    const png = await rasterizeIcon(src)
    if (png) out.push({ key: addinTabIconKey(path), png })
  }
  return out
}
