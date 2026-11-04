/**
 * Text effects (WordArt / Fontwork) of FastDocView blocks, finished with the browser's font metrics - what the library's
 * HTML writer cannot know. The library writes each effect line as SVG text on a path with `data-fontwork-*` attributes
 * (FastDocView Html/WordShapeHtml.cs AppendTextOnPaths); this module lays them out as LibreOffice's Fontwork does
 * (svx/source/customshapes/EnhancedCustomShapeFontWork.cxx, master read 2026-11-04):
 *
 * - single outline (`data-fontwork="single"`): CalculateHorizontalScalingFactor (:197-290) measures every line of the shape at
 *   the line height (shape height / lines) and takes the smallest outline-length / text-width ratio; GetFontWorkOutline
 *   (:520-525) multiplies the line height by it - the text is set at that size, so the widest line just fills its outline.
 *   Placed per the shape's alignment (gtextAlign: stretch / justify spread over the outline, centre, left, right -
 *   filter/source/msfilter/msdffimp.cxx:4466-4478, default centre); textPath turns each glyph along the outline.
 * - paired outlines (`data-fontwork="pair"`): FitTextOutlinesToShapeOutlines (:1020-1080) moves every point of every glyph to
 *   (fx, fy) of the text area -> upper(fx) + (lower(fx) - upper(fx)) * fy, fx and fy taken along each outline's length. The
 *   page has no glyph outlines, but the mapping is linear in fy for a fixed fx, so the text is drawn once into a canvas and
 *   copied column by column (one device pixel wide), each column stretched from upper(fx) to lower(fx): the same mapping,
 *   to the pixel. The bent text masks a rectangle painted with the text's own fill - colour, or the shape's gradient /
 *   pattern (Fontwork fills its outlines with the shape's fill) - and is drawn again when zoom or the device pixel ratio
 *   changes; the text itself stays in the DOM (transparent) for selection.
 *
 * Runs on block HTML already in the page (the directive below); a block that is not shown is never touched.
 */

type Point = { x: number; y: number }

/** Points of an SVG path made of M / L commands (the library writes outlines that way). */
export function parsePolyline(data: string): Point[] {
  const points: Point[] = []
  const pattern = /[ML]\s*(-?[\d.]+)[ ,]+(-?[\d.]+)/g
  let match: RegExpExecArray | null
  while ((match = pattern.exec(data))) points.push({ x: Number(match[1]), y: Number(match[2]) })
  return points
}

/** The point at a fraction of a polyline's length (EnhancedCustomShapeFontWork.cxx GetPoint :728-754). */
export function pointAt(points: Point[], fraction: number): Point {
  const last = points[points.length - 1]
  if (!last) return { x: 0, y: 0 }
  let total = 0
  for (let i = 1; i < points.length; i++) total += Math.hypot(points[i]!.x - points[i - 1]!.x, points[i]!.y - points[i - 1]!.y)
  const target = Math.max(0, Math.min(1, fraction)) * total
  let walked = 0
  for (let i = 1; i < points.length; i++) {
    const a = points[i - 1]!, b = points[i]!
    const step = Math.hypot(b.x - a.x, b.y - a.y)
    if (step > 0 && walked + step >= target) {
      const t = (target - walked) / step
      return { x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t }
    }
    walked += step
  }
  return last
}

/** The single-outline fit factor: the smallest outline-length / text-width over the shape's lines (CalculateHorizontalScalingFactor). */
export function fitFactor(lines: { length: number; width: number }[]): number {
  let factor = 1
  let defined = false
  for (const { length, width } of lines) {
    if (width <= 0) continue
    const scale = length / width
    if (!defined || scale < factor) { factor = scale; defined = true }
  }
  return factor
}

const SVG_NS = 'http://www.w3.org/2000/svg'
const XLINK_NS = 'http://www.w3.org/1999/xlink'

function layoutSingle(group: Element) {
  const texts = [...group.querySelectorAll<SVGTextElement>('text[data-fontwork="single"]')]
  if (texts.length === 0) return
  const measured = texts.map((text) => {
    const height = Number(text.getAttribute('data-fontwork-height'))
    const length = Number(text.getAttribute('data-fontwork-length'))
    const path = text.querySelector('textPath')
    path?.removeAttribute('textLength')
    path?.removeAttribute('lengthAdjust')
    text.setAttribute('font-size', String(height))
    return { text, path, height, length, width: text.getComputedTextLength() }
  })
  const factor = fitFactor(measured)
  for (const { text, path, height, length } of measured) {
    text.setAttribute('font-size', String(height * factor))
    const align = Number(text.getAttribute('data-fontwork-align') ?? '1')
    if (!path) continue
    if (align === 0 || align === 4 || align === 5) {
      // stretch / letter / word justification: spread over the whole outline
      path.setAttribute('textLength', String(length))
      path.setAttribute('lengthAdjust', align === 0 ? 'spacingAndGlyphs' : 'spacing')
      path.setAttribute('startOffset', '0')
      text.setAttribute('text-anchor', 'start')
    } else {
      path.setAttribute('startOffset', align === 2 ? '0' : align === 3 ? '100%' : '50%')
      text.setAttribute('text-anchor', align === 2 ? 'start' : align === 3 ? 'end' : 'middle')
    }
  }
}

/** Device pixels per point of the SVG the text is in (its on-screen scale - zoom included - times the device pixel ratio). */
function pixelsPerPointOf(text: SVGTextElement): number {
  const svg = text.ownerSVGElement
  const box = svg?.getBoundingClientRect()
  const viewBox = svg?.viewBox.baseVal
  return box && viewBox && viewBox.width > 0 && box.width > 0 ? (box.width / viewBox.width) * (window.devicePixelRatio || 1) : 2
}

let maskCounter = 0

function layoutPair(text: SVGTextElement) {
  // a re-layout (zoom / pixel ratio changed) replaces the previous bent text
  const previous = text.previousElementSibling
  if (previous?.classList.contains('fast-doc-view-fontwork-bent')) previous.remove()
  const upper = parsePolyline(text.getAttribute('data-fontwork-upper') ?? '')
  const lower = parsePolyline(text.getAttribute('data-fontwork-lower') ?? '')
  const line = Number(text.getAttribute('data-fontwork-line') ?? '0')
  const lines = Math.max(1, Number(text.getAttribute('data-fontwork-lines') ?? '1'))
  const content = text.textContent ?? ''
  if (upper.length < 2 || lower.length < 2 || content.length === 0) return
  // the line's band of the text area (fy from line / lines to (line + 1) / lines)
  const top = (fx: number) => { const u = pointAt(upper, fx), l = pointAt(lower, fx); const f = line / lines; return { x: u.x + (l.x - u.x) * f, y: u.y + (l.y - u.y) * f } }
  const bottom = (fx: number) => { const u = pointAt(upper, fx), l = pointAt(lower, fx); const f = (line + 1) / lines; return { x: u.x + (l.x - u.x) * f, y: u.y + (l.y - u.y) * f } }

  // 1. the text drawn once, large, cropped to its ink (the text area's bound rectangle in Fontwork), as a white coverage mask
  const fontFamily = text.getAttribute('font-family') ?? getComputedStyle(text).fontFamily
  const sourceSize = 200
  const source = document.createElement('canvas')
  const sourceContext = source.getContext('2d')
  if (!sourceContext) return
  sourceContext.font = `${sourceSize}px ${fontFamily}`
  const metrics = sourceContext.measureText(content)
  const inkLeft = -metrics.actualBoundingBoxLeft, inkWidth = Math.ceil(metrics.actualBoundingBoxLeft + metrics.actualBoundingBoxRight)
  const inkHeight = Math.ceil(metrics.actualBoundingBoxAscent + metrics.actualBoundingBoxDescent)
  if (inkWidth <= 0 || inkHeight <= 0) return
  source.width = inkWidth
  source.height = inkHeight
  sourceContext.font = `${sourceSize}px ${fontFamily}`
  sourceContext.fillStyle = '#ffffff'
  sourceContext.textBaseline = 'alphabetic'
  sourceContext.fillText(content, -inkLeft, metrics.actualBoundingBoxAscent)

  // 2. the target: the band's bounding box in the shape's points, at the device's resolution
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity
  for (let k = 0; k <= 64; k++) {
    for (const p of [top(k / 64), bottom(k / 64)]) { minX = Math.min(minX, p.x); minY = Math.min(minY, p.y); maxX = Math.max(maxX, p.x); maxY = Math.max(maxY, p.y) }
  }
  const pixelsPerPoint = pixelsPerPointOf(text)
  text.setAttribute('data-fontwork-pixels', String(pixelsPerPoint))
  const target = document.createElement('canvas')
  target.width = Math.max(1, Math.ceil((maxX - minX) * pixelsPerPoint))
  target.height = Math.max(1, Math.ceil((maxY - minY) * pixelsPerPoint))
  const context = target.getContext('2d')
  if (!context) return

  // 3. column by column: source column sx (fx = sx / inkWidth) stretched from top(fx) to bottom(fx)
  const columns = Math.max(1, Math.min(inkWidth, Math.ceil(Math.hypot(maxX - minX, maxY - minY) * pixelsPerPoint * 2)))
  const step = inkWidth / columns
  for (let c = 0; c < columns; c++) {
    const sx = c * step
    const fx = sx / inkWidth, fxNext = Math.min(1, (sx + step) / inkWidth)
    const t0 = top(fx), b0 = bottom(fx), t1 = top(fxNext)
    // affine map: source (sx + u, v) -> t0 + (t1 - t0) * (u / step) + (b0 - t0) * (v / inkHeight), in target pixels
    const ax = ((t1.x - t0.x) / step) * pixelsPerPoint, ay = ((t1.y - t0.y) / step) * pixelsPerPoint
    const bx = ((b0.x - t0.x) / inkHeight) * pixelsPerPoint, by = ((b0.y - t0.y) / inkHeight) * pixelsPerPoint
    context.setTransform(ax, ay, bx, by, (t0.x - minX) * pixelsPerPoint, (t0.y - minY) * pixelsPerPoint)
    // a little wider than the column so neighbours meet without seams
    context.drawImage(source, sx, 0, Math.min(step * 1.5, inkWidth - sx), inkHeight, 0, 0, Math.min(step * 1.5, inkWidth - sx), inkHeight)
  }

  // the bent ink's own bounds, in the shape's points (the fill spans them - paintOverTextBounds)
  const pixels = context.getImageData(0, 0, target.width, target.height).data
  let left = target.width, right = -1, upperRow = target.height, lowerRow = -1
  for (let y = 0; y < target.height; y++)
    for (let x = 0; x < target.width; x++)
      if (pixels[(y * target.width + x) * 4 + 3]! > 0) { if (x < left) left = x; if (x > right) right = x; if (y < upperRow) upperRow = y; if (y > lowerRow) lowerRow = y }
  if (right >= left)
    text.setAttribute('data-fontwork-ink', [minX + left / pixelsPerPoint, minY + upperRow / pixelsPerPoint, (right - left + 1) / pixelsPerPoint, (lowerRow - upperRow + 1) / pixelsPerPoint].join(' '))

  // 4. the bent text as a mask over a rectangle painted with the text's own fill - a colour, or the shape's gradient / pattern
  //    paint in the shape's coordinates (Fontwork fills its outlines with the shape's fill: the paint is not flattened to one
  //    colour). The text itself stays for selection and accessibility, transparent.
  const group = document.createElementNS(SVG_NS, 'g')
  group.setAttribute('class', 'fast-doc-view-fontwork-bent')
  group.setAttribute('aria-hidden', 'true')
  const maskId = `fast-doc-view-fontwork-mask-${++maskCounter}`
  const mask = document.createElementNS(SVG_NS, 'mask')
  mask.setAttribute('id', maskId)
  mask.setAttribute('maskUnits', 'userSpaceOnUse')
  for (const [name, value] of [['x', minX], ['y', minY], ['width', maxX - minX], ['height', maxY - minY]] as const) mask.setAttribute(name, String(value))
  const image = document.createElementNS(SVG_NS, 'image')
  for (const [name, value] of [['x', minX], ['y', minY], ['width', maxX - minX], ['height', maxY - minY]] as const) image.setAttribute(name, String(value))
  image.setAttribute('preserveAspectRatio', 'none')
  const url = target.toDataURL('image/png')
  image.setAttribute('href', url)
  image.setAttributeNS(XLINK_NS, 'xlink:href', url)
  mask.appendChild(image)
  const paint = document.createElementNS(SVG_NS, 'rect')
  for (const [name, value] of [['x', minX], ['y', minY], ['width', maxX - minX], ['height', maxY - minY]] as const) paint.setAttribute(name, String(value))
  paint.setAttribute('fill', text.getAttribute('data-fontwork-paint') ?? text.getAttribute('fill') ?? '#000000')
  paint.setAttribute('mask', `url(#${maskId})`)
  group.append(mask, paint)
  text.parentNode?.insertBefore(group, text)
  text.setAttribute('fill-opacity', '0')
}

/**
 * The fill spans the text's own bounds: LibreOffice puts every glyph outline of the effect into ONE path object with the
 * shape's fill attributes (EnhancedCustomShapeFontWork.cxx:1096-1130 CreateSdrObjectFromParagraphOutlines: aPolyPoly of all
 * characters, SetMergedItemSet(the custom shape's set)), and a gradient fills its object's bounds. The library writes the
 * shape's paint over the shape's box; here a copy is made over the union of the bent ink (paired lines) and the text boxes
 * (single lines): a linear gradient keeps its angle and is laid out again over the new box (the box grown to the turned extent,
 * basegfx gradienttools.cxx init1DGradientInfo :52-120), a rectangular shade is mapped onto it, a pattern keeps its tile size.
 */
export function paintOverTextBounds(group: Element) {
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity
  const take = (x: number, y: number, w: number, h: number) => { minX = Math.min(minX, x); minY = Math.min(minY, y); maxX = Math.max(maxX, x + w); maxY = Math.max(maxY, y + h) }
  for (const text of group.querySelectorAll<SVGTextElement>('text[data-fontwork]')) {
    const ink = text.getAttribute('data-fontwork-ink')
    if (ink) { const [x, y, w, h] = ink.split(' ').map(Number); take(x!, y!, w!, h!) }
    else if (text.getAttribute('data-fontwork') === 'single') { const box = text.getBBox(); if (box.width > 0) take(box.x, box.y, box.width, box.height) }
  }
  if (!(maxX > minX && maxY > minY)) return
  const width = maxX - minX, height = maxY - minY
  const painted = [...group.querySelectorAll<SVGElement>('text[data-fontwork="single"], .fast-doc-view-fontwork-bent rect[mask]')]
  for (const element of painted) {
    const original = element.getAttribute('data-fontwork-paint') ?? element.getAttribute('fill') ?? ''
    const reference = /^url\(#([^)]+)\)$/.exec(original)
    if (!reference) continue
    element.setAttribute('data-fontwork-paint', original)
    const server = group.ownerDocument.getElementById(reference[1] ?? '')
    if (!server) continue
    const copy = server.cloneNode(true) as SVGElement
    copy.setAttribute('id', `${server.id}-text-${Math.round(minX * 100)}-${Math.round(minY * 100)}-${Math.round(width * 100)}-${Math.round(height * 100)}`)
    if (copy.tagName.toLowerCase() === 'lineargradient') {
      const x1 = Number(copy.getAttribute('x1')), y1 = Number(copy.getAttribute('y1')), x2 = Number(copy.getAttribute('x2')), y2 = Number(copy.getAttribute('y2'))
      const length = Math.hypot(x2 - x1, y2 - y1)
      if (length <= 0) continue
      const dx = (x2 - x1) / length, dy = (y2 - y1) / length              // (sin a, cos a) of the gradient's turn
      const half = (height * Math.abs(dy) + width * Math.abs(dx)) / 2      // the turned extent of the text box
      const cx = minX + width / 2, cy = minY + height / 2
      copy.setAttribute('x1', String(cx - dx * half)); copy.setAttribute('y1', String(cy - dy * half))
      copy.setAttribute('x2', String(cx + dx * half)); copy.setAttribute('y2', String(cy + dy * half))
    } else if (copy.tagName.toLowerCase() === 'pattern') {
      const tileWidth = Number(copy.getAttribute('width')), tileHeight = Number(copy.getAttribute('height'))
      const isShade = server.id.startsWith('word-officeart-shade-')
      copy.setAttribute('patternTransform', isShade && tileWidth > 0 && tileHeight > 0
        ? `translate(${minX} ${minY}) scale(${width / tileWidth} ${height / tileHeight})`
        : `translate(${minX} ${minY})`)
    } else continue
    const defs = group.ownerDocument.createElementNS(SVG_NS, 'defs')
    defs.setAttribute('class', 'fast-doc-view-fontwork-paint')
    defs.appendChild(copy)
    group.insertBefore(defs, group.firstChild)
    element.setAttribute('fill', `url(#${copy.id})`)
  }
}

/** Lays out every text effect inside `root` once (marked done, so re-runs on the same element are free). */
export function layoutFontwork(root: Element) {
  for (const group of root.querySelectorAll('g[data-fontwork-shape]')) {
    if (group.hasAttribute('data-fontwork-done')) continue
    group.setAttribute('data-fontwork-done', '')
    layoutSingle(group)
    for (const text of group.querySelectorAll<SVGTextElement>('text[data-fontwork="pair"]')) layoutPair(text)
    paintOverTextBounds(group)
  }
}

/** Re-bends paired text whose device resolution changed (zoom, window moved to another screen) so it stays sharp. */
export function relayoutChangedResolution(root: Element) {
  for (const text of root.querySelectorAll<SVGTextElement>('text[data-fontwork="pair"][data-fontwork-pixels]')) {
    const drawnAt = Number(text.getAttribute('data-fontwork-pixels'))
    const now = pixelsPerPointOf(text)
    if (Math.abs(now - drawnAt) / drawnAt > 0.01) {
      layoutPair(text)
      const group = text.closest('g[data-fontwork-shape]')
      if (group) { for (const old of group.querySelectorAll('defs.fast-doc-view-fontwork-paint')) old.remove(); paintOverTextBounds(group) }
    }
  }
}

/** Per element: the size observer and the pixel-ratio query that re-bend on zoom / resolution changes. */
const watchers = new WeakMap<HTMLElement, () => void>()

/** `v-fast-doc-view-fontwork` on the element that holds block HTML: lays out its text effects after each render, once fonts are ready, and again at a new resolution. */
export const vFastDocViewFontwork = {
  mounted(element: HTMLElement) { schedule(element); watch(element) },
  updated(element: HTMLElement) { schedule(element) },
  unmounted(element: HTMLElement) { watchers.get(element)?.(); watchers.delete(element) },
}

function schedule(element: HTMLElement) {
  if (!element.querySelector('g[data-fontwork-shape]:not([data-fontwork-done])')) return
  const run = () => { if (element.isConnected) layoutFontwork(element) }
  if (document.fonts?.ready) document.fonts.ready.then(run)
  else run()
}

function watch(element: HTMLElement) {
  const relayout = () => { if (element.isConnected && element.querySelector('text[data-fontwork="pair"]')) relayoutChangedResolution(element) }
  // zoom changes the block's laid-out size; a pixel-ratio change (browser zoom, another screen) fires the media query
  const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(relayout)
  observer?.observe(element)
  let query: MediaQueryList | undefined
  const listenRatio = () => {
    query?.removeEventListener('change', onRatio)
    query = typeof matchMedia === 'undefined' ? undefined : matchMedia(`(resolution: ${window.devicePixelRatio || 1}dppx)`)
    query?.addEventListener('change', onRatio)
  }
  function onRatio() { relayout(); listenRatio() }
  listenRatio()
  watchers.set(element, () => { observer?.disconnect(); query?.removeEventListener('change', onRatio) })
}
