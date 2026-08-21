import { describe, it, expect } from 'vitest'
import { applyWordLinkAnchors, buildWordLinkTreatments, parseWordLinkData } from './wordLinkAnchors'
import { stripHtmlForSearch } from '@/utils/hebrewTextProcessing'
import { realAnchorFixtures } from './wordLinkAnchors.fixtures'
import type { WordLinkAnchor } from '@/webview-host/queries.types'
const anchor = (over: Partial<WordLinkAnchor>): WordLinkAnchor => ({
  lineId: 1,
  charStart: 0,
  charEnd: null,
  label: null,
  targetBookId: 7,
  targetLineId: 99,
  targetLineIndex: 42,
  sourceBookId: 5,
  ...over,
})

/** Text wrapped by the first word-link span, tags inside stripped. */
function wrappedText(html: string): string {
  const m = /<span class="word-link"[^>]*>([\s\S]*?)<\/span>/.exec(html)
  return m ? m[1]!.replace(/<[^>]*>/g, '') : ''
}

/** All segments wrapped by word-link spans, concatenated (a range crossing tags is
 * emitted as multiple spans — close before the tag, reopen after). */
function allWrappedText(html: string): string {
  let out = ''
  const re = /<span class="word-link"[^>]*>([\s\S]*?)<\/span>/g
  let m: RegExpExecArray | null
  while ((m = re.exec(html)) !== null) out += m[1]!.replace(/<[^>]*>/g, '')
  return out
}

describe('applyWordLinkAnchors — real v17 data', () => {
  for (const fx of realAnchorFixtures) {
    it(`wraps [${fx.charStart},${fx.charEnd}) → "${fx.expected}"`, () => {
      const result = applyWordLinkAnchors(fx.content, [
        anchor({
          charStart: fx.charStart,
          charEnd: fx.charEnd,
          label: fx.label,
          targetBookId: fx.targetBookId,
          targetLineId: fx.targetLineId,
          targetLineIndex: fx.targetLineIndex,
        }),
      ])
      // The wrapped text is exactly the citation upstream anchored (entities kept raw).
      expect(allWrappedText(result)).toBe(fx.expected)
      // data payload survives round-trip
      expect(result).toContain(`data-wl="${fx.targetBookId}:${fx.targetLineIndex}:${fx.targetLineId}"`)
      // Zero visible chars added in the FRONTEND offset convention too — user
      // highlight/note offsets on the same line must not drift.
      expect(stripHtmlForSearch(result)).toBe(stripHtmlForSearch(fx.content))
    })
  }
})

describe('applyWordLinkAnchors — walker mechanics', () => {
  it('counts HTML tags as zero chars', () => {
    // visible: אבגדה, range [2,4) = גד; ג is inside <b>
    const result = applyWordLinkAnchors('א<b>בג</b>דה', [anchor({ charStart: 2, charEnd: 4 })])
    expect(allWrappedText(result)).toBe('גד')
  })

  it('closes before a tag and reopens after — output stays well-formed', () => {
    const result = applyWordLinkAnchors('א<b>בג</b>דה', [anchor({ charStart: 2, charEnd: 4 })])
    // No span may contain a '<' of a real tag: every wrapped segment is pure text.
    for (const seg of result.match(/<span class="word-link"[^>]*>([\s\S]*?)<\/span>/g) ?? []) {
      const inner = seg.replace(/^<span[^>]*>/, '').replace(/<\/span>$/, '')
      expect(inner).not.toMatch(/<(?!\/?span)/)
    }
    // balanced spans
    const opens = (result.match(/<span class="word-link"/g) ?? []).length
    const closes = (result.match(/<\/span>/g) ?? []).length
    expect(opens).toBe(closes)
  })

  it('counts an entity as one visible char (upstream 10-char window)', () => {
    // visible positions: א(0) &nbsp;(1) ב(2) ג(3)
    const result = applyWordLinkAnchors('א&nbsp;בג', [anchor({ charStart: 1, charEnd: 3 })])
    expect(wrappedText(result)).toBe('&nbsp;ב')
  })

  it('counts diacritics as visible chars (upstream convention, unlike stripHtmlForSearch)', () => {
    // בְּרֵאשִׁית: letter+mark pairs — upstream position of ר is 2 (ב=0, ְּ… each mark counts)
    const content = 'בְּרֵאשית'
    // chars: ב(0) ְ(1) ּ(2) ר(3) ֵ(4) א(5) ש(6) י(7) ת(8)
    const result = applyWordLinkAnchors(content, [anchor({ charStart: 3, charEnd: 6 })])
    expect(wrappedText(result)).toBe('רֵא')
    expect(stripHtmlForSearch(result)).toBe(stripHtmlForSearch(content))
  })

  it('inserts a point marker with CSS-only label (no text content)', () => {
    const result = applyWordLinkAnchors('אבגד', [anchor({ charStart: 2, charEnd: null, label: 'א' })])
    expect(result).toBe('אב<sup class="word-link-marker" data-wl="7:42:99" data-wl-c="7" data-wl-label="א"></sup>גד')
  })

  it('defaults a missing label to § and escapes attribute chars', () => {
    const noLabel = applyWordLinkAnchors('אב', [anchor({ charStart: 1 })])
    expect(noLabel).toContain('data-wl-label="§"')
    const escaped = applyWordLinkAnchors('אב', [anchor({ charStart: 1, label: 'a"<b>&' })])
    expect(escaped).toContain('data-wl-label="a&quot;&lt;b&gt;&amp;"')
  })

  it('skips the color/shape bucket when the label already carries its own sign', () => {
    const decorated = applyWordLinkAnchors('אב', [anchor({ charStart: 1, label: '(א)' })])
    expect(decorated).not.toContain('data-wl-c')
    expect(decorated).toContain('data-wl-label="(א)"')
    const numeric = applyWordLinkAnchors('אב', [anchor({ charStart: 1, label: '12' })])
    expect(numeric).toContain('data-wl-c="7"')
  })

  it('treats a blank label as no label, never as a sign', () => {
    // A blank label counted as "carries its own sign" would render as nothing at
    // all — an invisible, unclickable mark.
    const blank = applyWordLinkAnchors('אב', [anchor({ charStart: 1, label: '   ' })])
    expect(blank).toContain('data-wl-label="§"')
    expect(blank).toContain('data-wl-c=')
  })

  it('renders a loader-assigned slot and inline enclosure glyphs', () => {
    const treated = applyWordLinkAnchors('אב', [
      anchor({ charStart: 1, label: 'א', colorBucket: 4, encOpen: '[', encClose: ']' }),
    ])
    expect(treated).toContain('data-wl-c="4"')
    expect(treated).toContain(`style="--wl-marker-open:'[';--wl-marker-close:']'"`)
  })

  it('emits a point marker at end-of-line', () => {
    const result = applyWordLinkAnchors('אב', [anchor({ charStart: 2 })])
    expect(result).toMatch(/אב<sup[^>]*><\/sup>$/)
  })

  it('closes a range that ends at end-of-line', () => {
    const result = applyWordLinkAnchors('אבג', [anchor({ charStart: 1, charEnd: 3 })])
    expect(result).toMatch(/<\/span>$/)
    expect(wrappedText(result)).toBe('בג')
  })

  it('drops overlapping ranges — first wins', () => {
    const result = applyWordLinkAnchors('אבגדהו', [
      anchor({ charStart: 1, charEnd: 4 }),
      anchor({ charStart: 2, charEnd: 5, targetLineId: 111 }),
    ])
    expect(allWrappedText(result)).toBe('בגד')
    expect(result).not.toContain('111')
  })

  it('keeps adjacent ranges separate', () => {
    const result = applyWordLinkAnchors('אבגד', [
      anchor({ charStart: 0, charEnd: 2 }),
      anchor({ charStart: 2, charEnd: 4, targetLineId: 111 }),
    ])
    expect(allWrappedText(result)).toBe('אבגד')
    expect(result).toContain('data-wl="7:42:111"')
  })

  it('returns content unchanged when there are no anchors', () => {
    expect(applyWordLinkAnchors('א<b>ב</b>', [])).toBe('א<b>ב</b>')
  })

  it('ignores anchors beyond the content (stale data after a DB update)', () => {
    const result = applyWordLinkAnchors('אב', [anchor({ charStart: 50, charEnd: 60 })])
    expect(result).toBe('אב')
  })
})

describe('parseWordLinkData', () => {
  it('round-trips the data attribute', () => {
    expect(parseWordLinkData('7:42:99')).toEqual({ bookId: 7, lineIndex: 42, lineId: 99 })
  })
  it('rejects malformed values', () => {
    expect(parseWordLinkData(null)).toBeNull()
    expect(parseWordLinkData('')).toBeNull()
    expect(parseWordLinkData('1:2')).toBeNull()
    expect(parseWordLinkData('a:b:c')).toBeNull()
  })
})

describe('buildWordLinkTreatments', () => {
  const t = (targetBookId: number, label: string | null) => ({ targetBookId, label })

  it('ranks commentaries by ascending book id, simplest treatment first', () => {
    const map = buildWordLinkTreatments([t(30, 'א'), t(10, 'ב'), t(20, null)])
    expect(map.get(10)!.bucket).toBe(0)
    expect(map.get(20)!.bucket).toBe(1)
    expect(map.get(30)!.bucket).toBe(2)
  })

  it('assigns runtime enclosure glyphs to slots 4+, skipping glyphs the book already uses', () => {
    const six = [t(1, 'א'), t(2, 'א'), t(3, 'א'), t(4, 'א'), t(5, 'א'), t(6, 'א')]
    expect(buildWordLinkTreatments(six).get(5)).toEqual({ bucket: 4, open: '[', close: ']' })
    // One commentary's own labels use brackets → the bracket pair is banned book-wide.
    const banned = buildWordLinkTreatments([...six, t(99, '[א]')])
    expect(banned.get(5)!.open).toBe('‹')
    expect(banned.has(99)).toBe(false)
  })

  it('excludes commentaries whose labels all carry their own sign from the ranking', () => {
    const map = buildWordLinkTreatments([t(5, '(א)'), t(9, 'א')])
    expect(map.has(5)).toBe(false)
    expect(map.get(9)!.bucket).toBe(0)
  })
})
