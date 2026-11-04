/**
 * Drawing-shape text of OpenDocument blocks finished with the browser's text metrics - what the library's HTML writer cannot
 * know. Two parts, both laid out as LibreOffice lays them out (svx, master read 2026-11-04):
 *
 * - Shapes that grow with their text (`data-autogrow` on a `.word-shape` frame, FastDocView Odt/OdtBlockHtmlRenderer.Drawing.cs
 *   AutoGrowAttributes): SdrObjCustomShape::AdjustTextFrameWidthAndHeight (svdoashp.cxx:2260-2433) - the text area takes the
 *   text's size (at least the minimum frame size) plus the text distances, growing or shrinking; ImpCalculateTextFrame
 *   (:2436-2464) changes the shape by that difference times shape / text-area size; top-aligned text grows down, bottom up,
 *   centre / justify both ways (left grows right, right grows left, else both ways). The outline is stretched to the new size.
 * - Dimension lines (`.fast-doc-view-measure`, Odt/OdtMeasureShape.cs Layout): SdrMeasurePrimitive2D::create2DDecomposition
 *   (svx/source/sdr/primitive2d/sdrmeasureprimitive2d.cxx:93-391) laid out again with the label's measured size - which side
 *   the label goes, whether the arrows go outside, the dimension line's pieces and the label's place all depend on it.
 *
 * Runs on block HTML already in the page (the directive below); a block that is not shown is never touched.
 */

const SVG_SHAPES = 'path, rect, ellipse, circle, line, polyline, polygon'

/** CSS pixels per point inside `frame` (its laid-out width over its width in points - holds under the block's CSS zoom). */
function pixelsPerPoint(frame: HTMLElement, widthPt: number): number {
  return widthPt > 0 && frame.offsetWidth > 0 ? frame.offsetWidth / widthPt : 96 / 72
}

const numbers = (value: string | undefined) => (value ?? '').split(' ').map(Number)

// -- shapes that grow with their text --------------------------------------------------------------------------------------

/** New length of one side of the shape and how far its start moves, for a text-area side of `textLength` that needs `needed`. */
export function growSide(shapeLength: number, textLength: number, needed: number, align: 'start' | 'end' | 'both') {
  if (!(textLength > 0) || !(shapeLength > 0)) return { length: shapeLength, shift: 0 }
  const change = (needed - textLength) * (shapeLength / textLength)
  return { length: shapeLength + change, shift: align === 'start' ? 0 : align === 'end' ? -change : -change / 2 }
}

export function growShape(frame: HTMLElement) {
  const mode = frame.dataset.autogrow ?? ''
  const [textX = 0, textY = 0, textWidth = 0, textHeight = 0] = numbers(frame.dataset.autogrowText)
  const [shapeWidth = 0, shapeHeight = 0] = numbers(frame.dataset.autogrowShape)
  const [padLeft = 0, padTop = 0, padRight = 0, padBottom = 0] = numbers(frame.dataset.autogrowPad)
  const [minWidth = 0, minHeight = 0] = numbers(frame.dataset.autogrowMin)
  const [vertical = 'top', horizontal = 'justify'] = (frame.dataset.autogrowAlign ?? '').split(' ')
  const box = frame.querySelector<HTMLElement>(':scope > .word-shape-text')
  const content = box?.firstElementChild as HTMLElement | null
  if (!box || !content) return
  // fractional sizes on both sides (the frame is unturned here), so the block's zoom cancels out
  const frameWidth = frame.getBoundingClientRect().width
  const scale = shapeWidth > 0 && frameWidth > 0 ? frameWidth / shapeWidth : 96 / 72
  const isVertical = getComputedStyle(box).writingMode.startsWith('vertical')

  // the text's own size: a side that grows is not bounded by the area (CalcTextSize with the paper as large as allowed)
  const saved = { width: content.style.width, height: content.style.height, maxWidth: content.style.maxWidth, maxHeight: content.style.maxHeight }
  if (mode.includes('w') && !isVertical) { content.style.width = 'max-content'; content.style.maxWidth = 'none' }
  if (mode.includes('h') && isVertical) { content.style.height = 'max-content'; content.style.maxHeight = 'none' }
  const contentBox = content.getBoundingClientRect()
  const contentWidth = contentBox.width / scale, contentHeight = contentBox.height / scale
  Object.assign(content.style, saved)

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
  const kx = width / shapeWidth, ky = height / shapeHeight
  frame.style.width = `${width}pt`
  frame.style.height = `${height}pt`
  if (shiftX || shiftY) frame.style.translate = `${shiftX}pt ${shiftY}pt`
  // the outline stretched to the new box, its lines kept at their width
  for (const svg of frame.querySelectorAll<SVGSVGElement>(':scope > svg.word-shape-geometry')) {
    svg.setAttribute('preserveAspectRatio', 'none')
    for (const shape of svg.querySelectorAll(SVG_SHAPES)) shape.setAttribute('vector-effect', 'non-scaling-stroke')
  }
  box.style.width = `${textWidth * kx}pt`
  box.style.height = `${textHeight * ky}pt`
  box.style.transform = `translate(${textX * kx}pt, ${textY * ky}pt)`
}

// -- dimension lines -------------------------------------------------------------------------------------------------------

const enum Position { Automatic, Negative, Centered, Positive }

/** The dimension line's pieces and the label's centre, in the line's frame (x along the line from its start, y across). */
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
  const pieces: [number, number][] = []
  if (arrowsOutside) {
    let lenLeft = outsideLength, lenRight = outsideLength
    if (!splitted) {
      if (horizontal === Position.Negative) lenLeft = startH + rangeWidth
      else if (horizontal === Position.Positive) lenRight = endH + rangeWidth
    }
    pieces.push([-lenLeft, 0], [length, length + lenRight])
    if (!splitted || horizontal !== Position.Centered) pieces.push([0, length])
  } else if (splitted) {
    const half = (length - (rangeWidth + (startH + endH) * 0.25)) * 0.5
    pieces.push([0, half], [length - half, length])
  } else pieces.push([0, length])

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

  const cos = Math.cos(angle), sin = Math.sin(angle)
  const toAnchor = (x: number, y: number) => ({ x: sx + x * cos - y * sin, y: sy + x * sin + y * cos })
  return {
    lines: pieces.map(([from, to]) => [toAnchor(from, mainY), toAnchor(to, mainY)] as const),
    center: toAnchor(textX + rangeWidth / 2, textY + rangeHeight / 2),
  }
}

export function layoutMeasure(container: HTMLElement) {
  const frame = container.parentElement
  const label = container.querySelector<HTMLElement>(':scope > .word-shape-text')
  if (!frame || !label) return
  const scale = pixelsPerPoint(frame, Number.parseFloat(frame.style.width))
  const empty = (label.textContent ?? '') === ''
  const { lines, center } = layoutMeasureLine(numbers(container.dataset.measure), empty ? 0 : label.offsetWidth / scale, empty ? 0 : label.offsetHeight / scale)
  const path = container.querySelector('svg path')
  path?.setAttribute('d', lines.map(([a, b]) => `M${a.x} ${a.y}L${b.x} ${b.y}`).join(''))
  const [a = 1, b = 0, c = 0, d = 1, e = 0, f = 0] = numbers(container.dataset.measureMatrix)
  label.style.left = `${a * center.x + c * center.y + e}pt`
  label.style.top = `${b * center.x + d * center.y + f}pt`
}

/** Lays out every growing shape and dimension line inside `root` once (marked done, so re-runs on the same element are free). */
export function layoutShapeText(root: Element) {
  for (const frame of root.querySelectorAll<HTMLElement>('.word-shape[data-autogrow]:not([data-autogrow-done])')) {
    frame.setAttribute('data-autogrow-done', '')
    growShape(frame)
  }
  for (const measure of root.querySelectorAll<HTMLElement>('.fast-doc-view-measure:not([data-measure-done])')) {
    measure.setAttribute('data-measure-done', '')
    layoutMeasure(measure)
  }
}

/** `v-fast-doc-view-shape-text` on the element that holds block HTML: lays out its shape texts after each render, once fonts are ready. */
export const vFastDocViewShapeText = {
  mounted(element: HTMLElement) { schedule(element) },
  updated(element: HTMLElement) { schedule(element) },
}

function schedule(element: HTMLElement) {
  if (!element.querySelector('.word-shape[data-autogrow]:not([data-autogrow-done]), .fast-doc-view-measure:not([data-measure-done])')) return
  const run = () => { if (element.isConnected) layoutShapeText(element) }
  if (document.fonts?.ready) document.fonts.ready.then(run)
  else run()
}
