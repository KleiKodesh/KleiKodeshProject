/**
 * Drawing-shape text of OpenDocument blocks finished with the browser's text metrics - what the library's HTML writer cannot
 * know. Laid out as LibreOffice lays it out (svx, master read 2026-11-04); the library marks the frames that need it
 * (`data-shape-page` on a `.word-shape`, FastDocView Odt/OdtBlockHtmlRenderer.Drawing.cs AppendShape):
 *
 * - Shapes that grow with their text (`data-autogrow-*` on a shape's text box, AutoGrowAttributes there):
 *   SdrObjCustomShape::AdjustTextFrameWidthAndHeight (svdoashp.cxx:2260-2433) - the text area takes the text's size (at least the
 *   minimum frame size) plus the text distances, growing or shrinking; ImpCalculateTextFrame (:2436-2464) changes the shape by
 *   that difference times shape / text-area size; top-aligned text grows down, bottom up, centre / justify both ways (left
 *   grows right, right grows left, else both ways). All of it in the shape's own unrotated rectangle, which a rotated shape then
 *   moves by the turned change of its top left (:2420-2428): here the change is applied in the shape's own coordinates, before
 *   its transform, and mirroring is redone about the new box's centre (EnhancedCustomShapeEngine.cxx:286-298). The outline is
 *   stretched to the new size, its lines keep their width.
 * - Dimension lines (`.fast-doc-view-measure`, Odt/OdtMeasureShape.cs Layout): SdrMeasurePrimitive2D::create2DDecomposition
 *   (svx/source/sdr/primitive2d/sdrmeasureprimitive2d.cxx:93-391) laid out again with the label's measured size - which side
 *   the label goes, whether the arrows go outside, the dimension line's pieces with their markers, and the label's place.
 * - Then the frame takes the new bounds of everything in it (the snap rectangle, so the text flows around the real size), and
 *   a contour wrap (style:wrap-contour) is swept again from the new outlines, as the library's ContourCss sweeps it.
 *
 * Runs on block HTML already in the page (the directive below); a block that is not shown is never touched.
 */

type Matrix = [number, number, number, number, number, number]
type Point = { x: number; y: number }

const SVG_SHAPES = 'path, rect, ellipse, circle, line, polyline, polygon'
const numbers = (value: string | undefined) => (value ?? '').trim().split(/\s+/).map(Number)

/** `first` applied, then `then` (the library's WordAffine.Then). */
export function then(first: Matrix, second: Matrix): Matrix {
  const [a, b, c, d, e, f] = first, [A, B, C, D, E, F] = second
  return [A * a + C * b, B * a + D * b, A * c + C * d, B * c + D * d, A * e + C * f + E, B * e + D * f + F]
}
const apply = (m: Matrix, x: number, y: number): Point => ({ x: m[0] * x + m[2] * y + m[4], y: m[1] * x + m[3] * y + m[5] })
const translate = (x: number, y: number): Matrix => [1, 0, 0, 1, x, y]
const scale = (x: number, y: number): Matrix => [x, 0, 0, y, 0, 0]
const rotate = (radians: number): Matrix => { const c = Math.cos(radians), s = Math.sin(radians); return [c, s, -s, c, 0, 0] }
const svgMatrix = (m: Matrix) => `matrix(${m.join(' ')})`
/** CSS matrix: the translation in px (1 pt = 4/3 px), as the library's WordAffine.Css. */
const cssMatrix = (m: Matrix) => `matrix(${m[0]},${m[1]},${m[2]},${m[3]},${m[4] * 4 / 3}px,${m[5] * 4 / 3}px)`
function matrixOf(element: Element): Matrix {
  const match = /matrix\(([^)]*)\)/.exec(element.getAttribute('transform') ?? '')
  const values = match ? match[1]!.split(/[\s,]+/).map(Number) : [1, 0, 0, 1, 0, 0]
  return values.length === 6 && values.every(Number.isFinite) ? values as Matrix : [1, 0, 0, 1, 0, 0]
}

// -- shapes that grow with their text --------------------------------------------------------------------------------------

/** New length of one side of the shape and how far its start moves, for a text-area side of `textLength` that needs `needed`. */
export function growSide(shapeLength: number, textLength: number, needed: number, align: 'start' | 'end' | 'both') {
  if (!(textLength > 0) || !(shapeLength > 0)) return { length: shapeLength, shift: 0 }
  const change = (needed - textLength) * (shapeLength / textLength)
  return { length: shapeLength + change, shift: align === 'start' ? 0 : align === 'end' ? -change : -change / 2 }
}

/** The grown shape's matrices: its own box (scaled, shifted) through its transform, mirrored about the new box's centre. */
export function grownMatrices(full: Matrix, shapeWidth: number, shapeHeight: number, width: number, height: number, shiftX: number, shiftY: number, mirrorX: boolean, mirrorY: boolean) {
  const kx = shapeWidth > 0 ? width / shapeWidth : 1, ky = shapeHeight > 0 ? height / shapeHeight : 1
  const placed = then(then(scale(kx, ky), translate(shiftX, shiftY)), full)
  let geometry = placed
  if (mirrorX || mirrorY) {
    const centre = apply(placed, shapeWidth / 2, shapeHeight / 2)
    geometry = then(placed, then(then(translate(-centre.x, -centre.y), scale(mirrorX ? -1 : 1, mirrorY ? -1 : 1)), translate(centre.x, centre.y)))
  }
  return { kx, ky, geometry, origin: apply(full, shiftX, shiftY) }
}

function growShape(frame: HTMLElement, box: HTMLElement, pixelsPerPoint: number) {
  const id = box.dataset.autogrowId
  const mode = box.dataset.autogrow ?? ''
  const [textX = 0, textY = 0, textWidth = 0, textHeight = 0] = numbers(box.dataset.autogrowText)
  const [shapeWidth = 0, shapeHeight = 0] = numbers(box.dataset.autogrowShape)
  const [padLeft = 0, padTop = 0, padRight = 0, padBottom = 0] = numbers(box.dataset.autogrowPad)
  const [minWidth = 0, minHeight = 0] = numbers(box.dataset.autogrowMin)
  const [vertical = 'top', horizontal = 'justify'] = (box.dataset.autogrowAlign ?? '').split(' ')
  const full = numbers(box.dataset.autogrowMatrix) as Matrix
  const [mirrorX = 0, mirrorY = 0] = numbers(box.dataset.autogrowMirror)
  const [turn = 0] = numbers(box.dataset.autogrowTurn)
  const [textRotate = 0] = numbers(box.dataset.autogrowTextRotate)
  const content = box.firstElementChild as HTMLElement | null
  if (!content || full.length !== 6) return

  // the text's own size, measured unturned: a side that grows is not bounded by the area (CalcTextSize with the paper as
  // large as allowed)
  const isVertical = getComputedStyle(box).writingMode.startsWith('vertical')
  const saved = { transform: box.style.transform, width: content.style.width, height: content.style.height, maxWidth: content.style.maxWidth, maxHeight: content.style.maxHeight }
  box.style.transform = 'none'
  if (mode.includes('w') && !isVertical) { content.style.width = 'max-content'; content.style.maxWidth = 'none' }
  if (mode.includes('h') && isVertical) { content.style.height = 'max-content'; content.style.maxHeight = 'none' }
  const measured = content.getBoundingClientRect()
  const contentWidth = measured.width / pixelsPerPoint, contentHeight = measured.height / pixelsPerPoint
  box.style.transform = saved.transform
  content.style.width = saved.width; content.style.height = saved.height; content.style.maxWidth = saved.maxWidth; content.style.maxHeight = saved.maxHeight

  let width = shapeWidth, height = shapeHeight, shiftX = 0, shiftY = 0
  if (mode.includes('w')) {
    const side = growSide(shapeWidth, textWidth, Math.max(contentWidth, minWidth) + padLeft + padRight,
      horizontal === 'left' ? 'start' : horizontal === 'right' ? 'end' : 'both')
    width = side.length; shiftX = side.shift
  }
  if (mode.includes('h')) {
    const side = growSide(shapeHeight, textHeight, Math.max(contentHeight, minHeight) + padTop + padBottom,
      vertical === 'top' ? 'start' : vertical === 'bottom' ? 'end' : 'both')
    height = side.length; shiftY = side.shift
  }
  if (Math.abs(width - shapeWidth) < 0.01 && Math.abs(height - shapeHeight) < 0.01) return
  const { kx, ky, geometry, origin } = grownMatrices(full, shapeWidth, shapeHeight, width, height, shiftX, shiftY, mirrorX === 1, mirrorY === 1)

  // the outline: its <g> through the grown matrix, its lines kept at their width
  const group = frame.querySelector(`g[data-autogrow-id="${id}"]`)
  if (group) {
    group.setAttribute('transform', svgMatrix(geometry))
    for (const shape of group.querySelectorAll(SVG_SHAPES)) shape.setAttribute('vector-effect', 'non-scaling-stroke')
  }
  // the text box: the grown area, turned by draw:text-rotate-angle about its centre, then with the shape (rotation only)
  const w = textWidth * kx, h = textHeight * ky
  let text = translate(textX * kx, textY * ky)
  if (textRotate) text = then(then(then(translate(-w / 2, -h / 2), rotate(-textRotate * Math.PI / 180)), translate(w / 2, h / 2)), text)
  text = then(then(text, rotate(turn)), translate(origin.x, origin.y))
  box.style.width = `${w}pt`
  box.style.height = `${h}pt`
  box.style.transformOrigin = '0 0'
  box.style.transform = cssMatrix(text)
}

// -- dimension lines -------------------------------------------------------------------------------------------------------

const enum Position { Automatic, Negative, Centered, Positive }

export type MeasurePiece = { from: Point; to: Point; startMarker: boolean; endMarker: boolean }

/** The dimension line's pieces (anchor points, with which ends carry a marker) and the label's centre. */
export function layoutMeasureLine(values: number[], textWidth: number, textHeight: number) {
  const [sx = 0, sy = 0, ex = 0, ey = 0, distance = 0, belowFlag = 0, rotatedFlag = 0, horizontalIn = 0, verticalIn = 0,
    startW = 0, startH = 0, endW = 0, endH = 0, lineWidth = 0, padLeft = 0, padRight = 0, padTop = 0, padBottom = 0] = values
  const below = belowFlag === 1, textRotation = rotatedFlag === 1
  let horizontal = horizontalIn as Position, vertical = verticalIn as Position
  const length = Math.hypot(ex - sx, ey - sy)
  const angle = Math.atan2(ey - sy, ex - sx)

  // text block range after its own rotation, and the automatic upside-down turn (:93-127)
  const hasText = textWidth > 0 || textHeight > 0
  const rangeWidth = textRotation ? textHeight : textWidth, rangeHeight = textRotation ? textWidth : textHeight
  let autoUpsideDown = false
  if (hasText) {
    let testAngle = angle
    if (textRotation) { testAngle -= Math.PI / 2; if (testAngle < -Math.PI) testAngle += 2 * Math.PI }
    if (testAngle > Math.PI / 4 || testAngle < -Math.PI * 0.75) autoUpsideDown = true
  }

  // :137-224
  const spaceNeeded = startH + endH + (startW + endW) * 0.5
  const outsideLength = (startH + endH + startW + endW) * 0.5
  const halfLineWidth = lineWidth * 0.5
  let arrowsOutside = spaceNeeded > length, splitted = false
  if (vertical === Position.Automatic) vertical = Position.Negative
  if (vertical === Position.Centered) splitted = true
  if (horizontal === Position.Automatic) {
    horizontal = rangeWidth > length ? Position.Negative : Position.Centered
    if (splitted) { if (rangeWidth + spaceNeeded > length) arrowsOutside = true }
    else if (rangeWidth + startH + endH + (startW + endW) * 0.125 > length) arrowsOutside = true
  }
  if (horizontal !== Position.Centered) arrowsOutside = true
  if (below || (autoUpsideDown && !textRotation)) {
    if (vertical === Position.Negative) vertical = Position.Positive
    else if (vertical === Position.Positive) vertical = Position.Negative
  }

  // main line (:226-271)
  const mainY = below ? distance : -distance
  const cos = Math.cos(angle), sin = Math.sin(angle)
  const toAnchor = (x: number, y: number) => ({ x: sx + x * cos - y * sin, y: sy + x * sin + y * cos })
  const pieces: MeasurePiece[] = []
  const piece = (from: number, to: number, left: boolean, right: boolean) =>
    pieces.push({ from: toAnchor(from, mainY), to: toAnchor(to, mainY), startMarker: left && startW > 0, endMarker: right && endW > 0 })
  if (arrowsOutside) {
    let lenLeft = outsideLength, lenRight = outsideLength
    if (!splitted) {
      if (horizontal === Position.Negative) lenLeft = startH + rangeWidth
      else if (horizontal === Position.Positive) lenRight = endH + rangeWidth
    }
    piece(-lenLeft, 0, false, true)
    piece(length, length + lenRight, true, false)
    if (!splitted || horizontal !== Position.Centered) piece(0, length, false, false)
  } else if (splitted) {
    const half = (length - (rangeWidth + (startH + endH) * 0.25)) * 0.5
    piece(0, half, true, false)
    piece(length - half, length, false, true)
  } else piece(0, length, true, true)

  // text position (:290-367)
  let textX: number, textY: number
  if (horizontal === Position.Negative) {
    textX = -(startH + rangeWidth + outsideLength * 0.18 + halfLineWidth)
    if (splitted) textX -= outsideLength - startH
    textX -= padRight
  } else if (horizontal === Position.Positive) {
    textX = length + endH + outsideLength * 0.18 + halfLineWidth
    if (splitted) textX += outsideLength - endH
    textX += padLeft
  } else textX = (length - rangeWidth) * 0.5 + (padLeft - padRight) / 2
  if (vertical === Position.Negative) textY = mainY - (rangeHeight + outsideLength * 0.10 + halfLineWidth) - padBottom
  else if (vertical === Position.Positive) textY = mainY + outsideLength * 0.10 + halfLineWidth + padTop
  else textY = mainY - rangeHeight * 0.5 + (padTop - padBottom) / 2

  return { pieces, center: toAnchor(textX + rangeWidth / 2, textY + rangeHeight / 2) }
}

type Marker = { width: number; consumed: number; polygons: Point[][] }

/** The library's marker data ("width consumed;x,y x,y;..."), or null. */
export function parseMarker(data: string): Marker | null {
  const [head, ...polygons] = data.split(';')
  if (!head) return null
  const [width = 0, consumed = 0] = numbers(head)
  return { width, consumed, polygons: polygons.map(p => p.trim().split(/\s+/).map(pair => { const [x = 0, y = 0] = pair.split(',').map(Number); return { x, y } })) }
}

/** A piece's outline and marker paths, as the library draws a line end (WordShapeHtml.AppendWithLineEnds; straight line). */
export function measurePiecePaths(piece: MeasurePiece, start: Marker | null, end: Marker | null) {
  const dx = piece.to.x - piece.from.x, dy = piece.to.y - piece.from.y, length = Math.hypot(dx, dy)
  const markers: string[] = []
  if (length <= 0) return { line: '', markers }
  const ux = dx / length, uy = dy / length
  const at = (s: number) => ({ x: piece.from.x + ux * s, y: piece.from.y + uy * s })
  const place = (marker: Marker, tip: Point, toward: Point) => {
    const r = Math.atan2(tip.y - toward.y, tip.x - toward.x) + Math.PI / 2, c = Math.cos(r), s = Math.sin(r)
    markers.push(marker.polygons.map(polygon => polygon.map((p, i) => `${i ? 'L' : 'M'}${tip.x + p.x * c - p.y * s} ${tip.y + p.x * s + p.y * c}`).join('') + 'Z').join(''))
  }
  let from = 0, to = length
  if (piece.startMarker && start) { place(start, piece.from, at(Math.min(start.consumed, length))); from = Math.max(0, start.consumed - start.width / 15) }
  if (piece.endMarker && end) { place(end, piece.to, at(Math.max(0, length - end.consumed))); to = Math.min(length, length - end.consumed + end.width / 15) }
  const a = at(from), b = at(to)
  return { line: to > from ? `M${a.x} ${a.y}L${b.x} ${b.y}` : '', markers }
}

/** Lays one dimension line out again; returns its pieces' ends in frame points (for the frame's bounds). */
function layoutMeasure(container: HTMLElement, pixelsPerPoint: number): Point[] {
  const label = container.querySelector<HTMLElement>(':scope > .word-shape-text')
  const svg = container.querySelector('svg')
  if (!label || !svg) return []
  const empty = (label.textContent ?? '') === ''
  const { pieces, center } = layoutMeasureLine(numbers(container.dataset.measure), empty ? 0 : label.offsetWidth / pixelsPerPoint, empty ? 0 : label.offsetHeight / pixelsPerPoint)
  const matrix = numbers(container.dataset.measureMatrix) as Matrix
  const [startData = '', endData = ''] = (container.dataset.measureMarkers ?? '').split('|')
  const start = parseMarker(startData), end = parseMarker(endData)
  // the line's look from what the library drew (stroke, width, dash, cap, opacity), the markers in the line's colour
  const drawn = svg.querySelector('path[stroke]:not([stroke="none"])')
  const strokeAttributes = drawn ? [...drawn.attributes].filter(a => a.name !== 'd') : []
  const colour = drawn?.getAttribute('stroke') ?? '#000000'
  const opacity = drawn?.getAttribute('stroke-opacity')
  const group = document.createElementNS('http://www.w3.org/2000/svg', 'g')
  group.setAttribute('transform', svgMatrix(matrix))
  for (const piece of pieces) {
    const { line, markers } = measurePiecePaths(piece, start, end)
    if (line) {
      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path')
      for (const a of strokeAttributes) path.setAttribute(a.name, a.value)
      path.setAttribute('d', line)
      group.appendChild(path)
    }
    for (const d of markers) {
      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path')
      path.setAttribute('d', d)
      path.setAttribute('fill', colour)
      if (opacity) path.setAttribute('fill-opacity', opacity)
      path.setAttribute('stroke', 'none')
      group.appendChild(path)
    }
  }
  svg.replaceChildren(group)
  const labelCentre = apply(matrix, center.x, center.y)
  label.style.left = `${labelCentre.x}pt`
  label.style.top = `${labelCentre.y}pt`
  return pieces.flatMap(p => [apply(matrix, p.from.x, p.from.y), apply(matrix, p.to.x, p.to.y)])
}

// -- the frame: new bounds and contour --------------------------------------------------------------------------------------

/** Points sampled along every outline in `group` (frame points), about one per point of length. */
function sampledOutlines(group: SVGGElement): Point[][] {
  const matrix = matrixOf(group)
  const outlines: Point[][] = []
  for (const shape of group.querySelectorAll<SVGGeometryElement>(SVG_SHAPES)) {
    if (shape.closest('defs, mask, clipPath, pattern')) continue
    let length = 0
    try { length = shape.getTotalLength() } catch { continue }
    if (!(length > 0)) continue
    const steps = Math.max(8, Math.min(2000, Math.ceil(length)))
    const points: Point[] = []
    // the element's own transforms inside the group too (nested <g> or a transform on the element)
    const groupCtm = group.getCTM(), shapeCtm = shape.getCTM()
    const local = groupCtm && shapeCtm ? groupCtm.inverse().multiply(shapeCtm) : null
    const inner: Matrix = local ? [local.a, local.b, local.c, local.d, local.e, local.f] : [1, 0, 0, 1, 0, 0]
    const toFrame = then(inner, matrix)
    for (let i = 0; i <= steps; i++) { const p = shape.getPointAtLength((length * i) / steps); points.push(apply(toFrame, p.x, p.y)) }
    outlines.push(points)
  }
  return outlines
}

/** The contour polygon as the library's ContourCss sweeps it: the outline's extreme on the text side every point of height. */
export function contourPolygon(outlines: Point[][], width: number, height: number, textOnRight: boolean): string | null {
  const step = Math.max(1, height / 2000)
  const polygon: Point[] = []
  for (let y = 0; ; y = Math.min(height, y + step)) {
    let extreme: number | null = null
    for (const outline of outlines)
      for (let i = 0; i < outline.length; i++) {
        const p1 = outline[i]!, p2 = outline[(i + 1) % outline.length]!
        if (y < Math.min(p1.y, p2.y) || y > Math.max(p1.y, p2.y)) continue
        const x = p1.y === p2.y ? (textOnRight ? Math.max(p1.x, p2.x) : Math.min(p1.x, p2.x)) : p1.x + ((y - p1.y) / (p2.y - p1.y)) * (p2.x - p1.x)
        extreme = extreme === null ? x : textOnRight ? Math.max(extreme, x) : Math.min(extreme, x)
      }
    if (extreme !== null) polygon.push({ x: Math.max(0, Math.min(width, extreme)), y })
    if (y >= height) break
  }
  if (polygon.length < 2) return null
  const edge = textOnRight ? 0 : width
  const points = [{ x: edge, y: polygon[0]!.y }, ...polygon, { x: edge, y: polygon[polygon.length - 1]!.y }]
  return `polygon(${points.map(p => `${+p.x.toFixed(2)}pt ${+p.y.toFixed(2)}pt`).join(',')}) border-box`
}

function layoutFrame(frame: HTMLElement) {
  const [oldWidth = 0, oldHeight = 0] = numbers(frame.dataset.shapePage)
  const frameBox = frame.getBoundingClientRect()
  const pixelsPerPoint = oldWidth > 0 && frameBox.width > 0 ? frameBox.width / oldWidth : 96 / 72

  for (const box of frame.querySelectorAll<HTMLElement>('.word-shape-text[data-autogrow-id]')) growShape(frame, box, pixelsPerPoint)
  // a label's size in its own (unturned) layout: offset sizes in CSS px, scaled like the frame
  const layoutScale = oldWidth > 0 && frame.offsetWidth > 0 ? frameBox.width / frame.offsetWidth : 1
  const ends = [...frame.querySelectorAll<HTMLElement>('.fast-doc-view-measure')].flatMap(m => layoutMeasure(m, pixelsPerPoint / layoutScale))

  // the new bounds: every shape's box through its (grown) matrix, and the dimension lines
  let left = Infinity, top = Infinity, right = -Infinity, bottom = -Infinity
  const take = (p: Point) => { left = Math.min(left, p.x); top = Math.min(top, p.y); right = Math.max(right, p.x); bottom = Math.max(bottom, p.y) }
  for (const group of frame.querySelectorAll<SVGGElement>('g[data-shape-box]')) {
    const [w = 0, h = 0] = numbers(group.getAttribute('data-shape-box') ?? '')
    const m = matrixOf(group)
    for (const [x, y] of [[0, 0], [w, 0], [0, h], [w, h]] as const) take(apply(m, x, y))
  }
  ends.forEach(take)
  if (!Number.isFinite(left) || (Math.abs(left) < 0.01 && Math.abs(top) < 0.01 && Math.abs(right - oldWidth) < 0.01 && Math.abs(bottom - oldHeight) < 0.01)) return
  const width = right - left, height = bottom - top

  // the frame moves to the new top left and takes the new size; its content keeps its place
  const style = getComputedStyle(frame)
  if (style.position === 'absolute') {
    frame.style.left = `calc(${style.left} + ${left}pt)`
    frame.style.top = `calc(${style.top} + ${top}pt)`
  } else {
    if (style.float === 'right') frame.style.marginRight = `calc(${style.marginRight} - ${right - oldWidth}pt)`
    else frame.style.marginLeft = `calc(${style.marginLeft} + ${left}pt)`
    frame.style.marginTop = `calc(${style.marginTop} + ${top}pt)`
  }
  frame.style.width = `${width}pt`
  frame.style.height = `${height}pt`
  for (const svg of frame.querySelectorAll('svg.word-shape-geometry')) {
    svg.setAttribute('viewBox', `${left} ${top} ${width} ${height}`)
    svg.setAttribute('width', `${width}pt`)
    svg.setAttribute('height', `${height}pt`)
  }
  for (const text of frame.querySelectorAll<HTMLElement>('.word-shape-text')) {
    text.style.marginLeft = `${-left}pt`
    text.style.marginTop = `${-top}pt`
  }
  // the contour wrap, swept again from the new outlines (in the frame's new coordinates)
  if (frame.style.shapeOutside.startsWith('polygon')) {
    const textOnRight = /^polygon\(\s*0(pt|px)?\s/.test(frame.style.shapeOutside)
    const outlines = [...frame.querySelectorAll<SVGGElement>(':scope > svg.word-shape-geometry > g[data-shape-box]')]
      .flatMap(sampledOutlines).map(outline => outline.map(p => ({ x: p.x - left, y: p.y - top })))
    const polygon = contourPolygon(outlines, width, height, textOnRight)
    if (polygon) frame.style.shapeOutside = polygon
  }
}

/** Lays out every marked frame inside `root` once (marked done, so re-runs on the same element are free). */
export function layoutShapeText(root: Element) {
  for (const frame of root.querySelectorAll<HTMLElement>('.word-shape[data-shape-page]:not([data-shape-page-done])')) {
    frame.setAttribute('data-shape-page-done', '')
    layoutFrame(frame)
  }
}

/** `v-fast-doc-view-shape-text` on the element that holds block HTML: lays out its shape texts after each render, once fonts are ready. */
export const vFastDocViewShapeText = {
  mounted(element: HTMLElement) { schedule(element) },
  updated(element: HTMLElement) { schedule(element) },
}

function schedule(element: HTMLElement) {
  if (!element.querySelector('.word-shape[data-shape-page]:not([data-shape-page-done])')) return
  const run = () => { if (element.isConnected) layoutShapeText(element) }
  if (document.fonts?.ready) document.fonts.ready.then(run)
  else run()
}
