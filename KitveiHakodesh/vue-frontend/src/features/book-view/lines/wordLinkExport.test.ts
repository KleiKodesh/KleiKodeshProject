/**
 * Covers the string-only half of wordLinkExport. `applyWordLinkExport` itself parses
 * and rewrites real markup (that is why it uses the DOM rather than regexes), and the
 * suite runs on the 'node' environment with no document — so it is verified in the
 * live browser run instead of behind a DOM shim that would not be exercising the same
 * code at all.
 */
import { describe, it, expect } from 'vitest'
import {
  buildWordLinkEndnotesHtml,
  stripWordLinkMarkers,
  type WordLinkEndnote,
} from './wordLinkExport'

const endnote = (over: Partial<WordLinkEndnote> = {}): WordLinkEndnote => ({
  id: 1,
  label: 'A',
  html: 'the target line',
  source: 'Book, Chapter Section',
  link: 'seforimapp://book/5?index=3',
  ...over,
})

describe('buildWordLinkEndnotesHtml', () => {
  it('closes each entry with its source as the app link', () => {
    const html = buildWordLinkEndnotesHtml([endnote()])
    expect(html).toContain('id="wlnote-1"')
    expect(html).toContain('href="#wlref-1"')
    expect(html).toContain('the target line')
    expect(html).toContain('(<a href="seforimapp://book/5?index=3"')
    expect(html).toContain('Book, Chapter Section</a>)')
  })

  it('keeps the reference mark the line displays, enclosure and all', () => {
    expect(buildWordLinkEndnotesHtml([endnote({ label: '[B]' })])).toContain('>[B]</a>')
  })

  it('lists entries in their own sequence, one block per entry', () => {
    const html = buildWordLinkEndnotesHtml([endnote(), endnote({ id: 2, label: 'B' })])
    expect(html.match(/id="wlnote-\d+"/g)).toEqual(['id="wlnote-1"', 'id="wlnote-2"'])
  })

  it('is empty when there is nothing to list', () => {
    expect(buildWordLinkEndnotesHtml([])).toBe('')
  })
})

describe('stripWordLinkMarkers', () => {
  const marker = '<sup class="word-link-marker" data-wl="5:3:88" data-wl-c="1" data-wl-label="A"></sup>'

  it('removes the markers, whatever attributes they carry', () => {
    expect(stripWordLinkMarkers(`a${marker}b`)).toBe('ab')
  })

  it('leaves the words of a range citation untouched — only the marker is app chrome', () => {
    const html = `<span class="word-link" data-wl="7:42:99">cited</span>${marker}`
    const stripped = stripWordLinkMarkers(html)
    expect(stripped).toContain('cited')
    expect(stripped).not.toContain('word-link-marker')
  })
})
