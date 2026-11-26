import { defineComponent, h, type Component } from 'vue'

/**
 * An icon that draws a few characters instead of a picture, for options best shown by the
 * marks themselves (punctuation removed, where the source goes). Same box as a Fluent 20 icon
 * (1.2em square, currentColor), so it drops into any icon slot. The text is drawn exactly as
 * given, left to right, in both languages.
 */
export function textGlyphIcon(text: string, fontSize = 12, fontFamily = 'Segoe UI, Arial, sans-serif', y = 14.5): Component {
  return defineComponent({
    name: 'TextGlyphIcon',
    setup: () => () =>
      h('svg', { viewBox: '0 0 20 20', width: '1.2em', height: '1.2em', 'aria-hidden': 'true' }, [
        h('text', {
          x: 10, y, 'text-anchor': 'middle', fill: 'currentColor', direction: 'ltr',
          'font-family': fontFamily, 'font-size': fontSize, 'font-weight': 600,
        }, text),
      ]),
  })
}

/**
 * "Copy without teamim": two prominent cantillation marks drawn on their own, no letter -
 * a shalshelet (zigzag, above) and an etnachta (wedge, below).
 */
export const CantillationMarksIcon: Component = defineComponent({
  name: 'CantillationMarksIcon',
  setup: () => () =>
    h('svg', { viewBox: '0 0 24 24', width: '1.2em', height: '1.2em', 'aria-hidden': 'true' }, [
      h('g', { fill: 'none', stroke: 'currentColor', 'stroke-width': 1.6, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }, [
        // shalshelet: a rising zigzag
        h('path', { d: 'M5 12l2.5-3l2.5 2l2.5-3l2.5 2l2.5-3l1.5 1.2' }),
        // etnachta: an upward wedge
        h('path', { d: 'M8.5 20.5l3.5-5l3.5 5' }),
      ]),
    ]),
})
/** "Remove modern punctuation": the marks it removes. */
export const PunctuationMarksIcon = textGlyphIcon('?!;,', 13)
/**
 * Source position icons: a round bracket (the source) beside three dots (the text).
 * RTL reading order: "start" puts the bracket on the right, "end" on the left.
 */
function sourcePositionIcon(bracketOnRight: boolean): Component {
  const bracket = bracketOnRight
    ? 'M16 3c2.5 2.5 3.5 5.5 3.5 9s-1 6.5-3.5 9'
    : 'M8 3c-2.5 2.5-3.5 5.5-3.5 9s1 6.5 3.5 9'
  const dotsX = bracketOnRight ? [4.5, 8.5, 12.5] : [11.5, 15.5, 19.5]
  return defineComponent({
    name: 'SourcePositionIcon',
    setup: () => () =>
      h('svg', { viewBox: '0 0 24 24', width: '1.2em', height: '1.2em', 'aria-hidden': 'true' }, [
        h('path', { d: bracket, fill: 'none', stroke: 'currentColor', 'stroke-width': 1.5, 'stroke-linecap': 'round' }),
        ...dotsX.map((cx) => h('circle', { cx, cy: 12, r: 1, fill: 'currentColor' })),
      ]),
  })
}

/** "Source at start". */
export const SourceAtStartIcon = sourcePositionIcon(true)
/** "Source at end". */
export const SourceAtEndIcon = sourcePositionIcon(false)
