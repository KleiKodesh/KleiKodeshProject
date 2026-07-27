import { describe, it, expect } from 'vitest'
import { wrapRtlHtml } from './useLineCopy'

/**
 * Guards the inline-vs-block clipboard shape decision.
 *
 * Word decides "inline run" vs "whole-document import" from the CF_HTML fragment
 * markers Chromium wraps around whatever we hand setData('text/html', …). A payload
 * wrapped in <!DOCTYPE html> gets fragment-marked as a document, and Word terminates
 * the final paragraph — the trailing break "העתק כרצף" used to produce.
 *
 * Verified against real Word (COM, PasteAndFormat(20)) pasting between sentinels:
 *   <span> in a document wrapper → 2 paragraphs      <span> bare/fragment-marked → 1
 *   <div>  in a document wrapper → 2 paragraphs      <div>  fragment-marked      → 2
 * So an inline run must be emitted BARE, and block content must stay wrapped.
 */
describe('wrapRtlHtml', () => {
  describe('single inline run — emitted bare so Word keeps the paragraph', () => {
    it('leaves a lone span unwrapped and adds dir="rtl"', () => {
      const out = wrapRtlHtml('<span>אחד שתים</span>')
      expect(out).toBe('<span dir="rtl">אחד שתים</span>')
      expect(out).not.toContain('DOCTYPE')
    })

    it('preserves an existing dir attribute without duplicating it', () => {
      const out = wrapRtlHtml('<span dir="rtl">ציטוט (מקור)</span>')
      expect(out).toBe('<span dir="rtl">ציטוט (מקור)</span>')
      expect(out.match(/dir=/g)).toHaveLength(1)
    })

    it('treats nested inline markup as still a single run', () => {
      const out = wrapRtlHtml('<span>א <b>ב</b> <a href="#">ג</a></span>')
      expect(out).not.toContain('DOCTYPE')
      expect(out.startsWith('<span dir="rtl">')).toBe(true)
    })

    it('trims surrounding whitespace before deciding', () => {
      expect(wrapRtlHtml('\n  <span>א</span>  \n')).toBe('<span dir="rtl">א</span>')
    })
  })

  describe('block content — kept wrapped so it pastes as paragraphs', () => {
    it('wraps per-line divs (copyJoinLines OFF keeps one paragraph per line)', () => {
      const out = wrapRtlHtml('<div class="line">א</div><div class="line">ב</div>')
      expect(out).toContain('<!DOCTYPE html>')
      expect(out).toContain('<body><div class="line">א</div>')
    })

    it('wraps a run followed by an endnotes block, not just the leading span', () => {
      const out = wrapRtlHtml('<span>גוף</span><hr/><div id="note-1">הערה</div>')
      expect(out).toContain('<!DOCTYPE html>')
    })

    it('wraps sibling spans (txt-view runs split by an h2 header)', () => {
      const out = wrapRtlHtml('<span>רץ א</span><h2>כותרת</h2><span>רץ ב</span>')
      expect(out).toContain('<!DOCTYPE html>')
    })

    it('wraps an h2 source heading followed by the body', () => {
      expect(wrapRtlHtml('<h2 dir="rtl">ספר</h2><span>גוף</span>')).toContain('<!DOCTYPE html>')
    })

    it('leaves no whitespace text node beside the payload', () => {
      const out = wrapRtlHtml('  <div>א</div>  ')
      expect(out).toContain('<body><div>א</div></body>')
    })
  })
})
