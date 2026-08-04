import { createApp, h, type Component } from 'vue'
import { allDocumentIcons, type DocumentIconKey } from '@/utils/documentIcons'

/**
 * Rasterizes the shared document icons (utils/documentIcons) to PNGs for the native
 * chrome tab strip, so a tab's favicon there is the very same glyph the in-page
 * lists draw.
 *
 * The icons are Vue components, so each is mounted offscreen and its SVG read back
 * out of the DOM rather than having its markup reconstructed here. Anything an icon
 * expresses in the markup — including the RTL book's mirror transform — comes along
 * for free; anything it expresses only in CSS would not, which is why the mirrored
 * icons carry an SVG transform instead (see IconBookRtl20).
 *
 * Sizing follows what the strip actually draws: Dpi(16) device pixels. Rendering at
 * that exact size (rather than one fixed size, scaled later) is what keeps the glyph
 * crisp at 125%/150%/200%, and is why the set is re-sent when the ratio changes.
 */

/** Logical size the strip draws the icon at; the painter uses Dpi(16). */
const ICON_LOGICAL_PX = 16

export interface RasterizedIcon {
  key: DocumentIconKey
  /** PNG data URI at the current device pixel ratio. */
  png: string
}

/** Renders a Vue icon component offscreen and returns its live SVG element. */
function renderIconSvg(component: Component, color: string): { svg: SVGElement; dispose: () => void } | null {
  const host = document.createElement('div')
  // Off-screen but still laid out — display:none would leave computed styles unusable.
  host.style.cssText = 'position:fixed;left:-9999px;top:0;width:0;height:0;overflow:hidden'
  document.body.appendChild(host)

  const app = createApp({ render: () => h(component) })
  app.mount(host)

  const svg = host.querySelector('svg')
  if (!svg) {
    app.unmount()
    host.remove()
    return null
  }

  // The color MUST go on the svg element itself, not on the host: theme.css has a
  // global `svg { color: var(--text-secondary) }`, and that rule beats a colour
  // merely inherited from an ancestor — every icon would rasterize grey. An inline
  // style on the element outranks it. Empty means "no brand colour", so fall back
  // to a neutral that reads on both light and dark strips, since a PNG holds one.
  ;(svg as SVGElement & { style: CSSStyleDeclaration }).style.color = color || '#605e5c'
  return {
    svg,
    dispose: () => {
      app.unmount()
      host.remove()
    },
  }
}

/** Serializes a live SVG to a standalone string, baking in computed color and transform. */
function serializeSvg(svg: SVGElement, sizePx: number): string {
  const clone = svg.cloneNode(true) as SVGElement
  const computed = getComputedStyle(svg)

  clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg')
  clone.setAttribute('width', String(sizePx))
  clone.setAttribute('height', String(sizePx))
  if (!clone.getAttribute('viewBox')) {
    clone.setAttribute('viewBox', `0 0 ${ICON_LOGICAL_PX} ${ICON_LOGICAL_PX}`)
  }

  // currentColor resolves against the element's own color, which does not survive
  // serialization — bake it in.
  clone.setAttribute('color', computed.color)
  clone.setAttribute('fill', computed.fill && computed.fill !== 'none' ? computed.fill : computed.color)

  // No transform handling needed: the RTL book icons carry their mirror as an SVG
  // transform in the markup (see IconBookRtl20), so it serializes with everything
  // else. Anything relying on a CSS transform would silently lose it here.
  return new XMLSerializer().serializeToString(clone)
}

/** Draws an SVG string into a canvas of the given device size and returns a PNG data URI. */
function svgToPng(svgText: string, devicePx: number): Promise<string | null> {
  return new Promise((resolve) => {
    const blob = new Blob([svgText], { type: 'image/svg+xml;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const img = new Image()
    img.onload = () => {
      try {
        const canvas = document.createElement('canvas')
        canvas.width = devicePx
        canvas.height = devicePx
        const ctx = canvas.getContext('2d')
        if (!ctx) {
          resolve(null)
          return
        }
        ctx.drawImage(img, 0, 0, devicePx, devicePx)
        resolve(canvas.toDataURL('image/png'))
      } catch {
        resolve(null)
      } finally {
        URL.revokeObjectURL(url)
      }
    }
    img.onerror = () => {
      URL.revokeObjectURL(url)
      resolve(null)
    }
    img.src = url
  })
}

/**
 * Rasterize every shared icon at the current device pixel ratio.
 * Returns only the ones that rendered — a failure just means that tab shows no icon.
 */
export async function rasterizeTabIcons(): Promise<RasterizedIcon[]> {
  const devicePx = Math.max(16, Math.round(ICON_LOGICAL_PX * (window.devicePixelRatio || 1)))
  const out: RasterizedIcon[] = []

  for (const icon of allDocumentIcons()) {
    const rendered = renderIconSvg(icon.icon20, icon.color)
    if (!rendered) continue
    let svgText: string
    try {
      svgText = serializeSvg(rendered.svg, devicePx)
    } finally {
      rendered.dispose()
    }
    const png = await svgToPng(svgText, devicePx)
    if (png) out.push({ key: icon.key, png })
  }

  return out
}
