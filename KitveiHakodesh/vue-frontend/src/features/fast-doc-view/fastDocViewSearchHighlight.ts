import { highlightSearchMatches, SEARCH_MATCH_CONTINUED_CLASS } from '@/utils/searchMatchHighlight'

/**
 * The class FastDocView gives the span holding a phonetic guide's rest after its comma, on the base line
 * (FastDocView's Fields.WordFieldRuby.GuideRestClass, the same in .docx / .doc / RTF).
 */
const GUIDE_REST_CLASS = 'ruby-guide-rest'

/** Placeholders while the matches are found; tags, so they add no searched text. Never reach the page. */
const SPLIT = '<x-fdv-ruby-split>'
const COMMA_OPEN = '<x-fdv-ruby-comma>'
const COMMA_CLOSE = '</x-fdv-ruby-comma>'
const HIDDEN = (index: number) => `<x-fdv-ruby-hidden-${index}>`

const RUBY = /<ruby>([\s\S]*?)(<rt\b[^>]*>[\s\S]*?<\/rt>)<\/ruby>/g
const HEAD_MARK = '<mark class="search-match">'

/**
 * A FastDocView block's HTML with the search query's matches marked, as the library's search counts them.
 *
 * A phonetic guide (HTML `<ruby>base<rt>guide</rt></ruby>`) is drawn base first, but Word's Find - and the library's
 * search - reads the guide first: "gdbase" is one match across guide and base. A guide with a comma is drawn split (its
 * head over the base line, its rest at the start of that line, then a comma and the base) and read as written: head,
 * comma, rest - the base after it is not searched (FastDocView DocxRubyText.SearchText / WordFieldRuby.ToHtml, checked
 * with Word 16's Find). So each ruby is laid out in reading order while the matches are found, then put back.
 *
 * A match never crosses a tag: one that runs over several runs (each a span), a ruby's parts or a note mark is split into
 * its first mark (counted, `search-match`) and continuation marks (`search-match-continued`) sharing its
 * `data-search-part` number - otherwise the page's parser closes the mark at the first end tag and leaves the rest
 * unmarked (and with a ruby, closes the ruby early). The first marks are numbered in reading order, which setCurrentMark
 * follows, since the page order of guide and base differs.
 */
export function highlightFastDocViewSearchMatches(html: string, query: string): string {
  const hidden: string[] = []
  const reading = !html.includes('<ruby') ? html : html.replace(RUBY, (whole, base: string, guide: string) => {
    if (base.includes('<ruby')) return whole
    const rest = guideRestOf(base)
    if (!rest) return `<ruby>${guide}${SPLIT}${base}</ruby>`
    hidden.push(base.slice(rest.length))
    return `<ruby>${guide}${SPLIT}${COMMA_OPEN},${COMMA_CLOSE}${rest}${HIDDEN(hidden.length - 1)}</ruby>`
  })
  const marked = highlightSearchMatches(reading, query, { ignoreCase: true })
  if (marked === reading) return html

  return splitMarksAtTags(marked)
    .replace(/<ruby>([\s\S]*?)<x-fdv-ruby-split>([\s\S]*?)<\/ruby>/g, (_whole, guide: string, base: string) => `<ruby>${base}${guide}</ruby>`)
    .replace(/<x-fdv-ruby-comma>((?:<[^>]*>)*),((?:<[^>]*>)*)<\/x-fdv-ruby-comma>/g, '$1$2')
    .replace(/<x-fdv-ruby-hidden-(\d+)>/g, (_whole, index: string) => hidden[Number(index)] ?? '')
    .replace(new RegExp(`<mark class="${SEARCH_MATCH_CONTINUED_CLASS}" data-search-part="\\d+"></mark>`, 'g'), '')
}

/** The leading `<span class="ruby-guide-rest" ...>...</span>` of a ruby's base (its nested spans included), or null. */
function guideRestOf(base: string): string | null {
  if (!base.startsWith(`<span class="${GUIDE_REST_CLASS}"`)) return null
  const tag = /<(\/?)span\b[^>]*>/g
  let depth = 0
  let found: RegExpExecArray | null
  while ((found = tag.exec(base))) {
    depth += found[1] ? -1 : 1
    if (depth === 0) return base.slice(0, tag.lastIndex)
  }
  return null
}

/**
 * Numbers every first mark in reading order and, where a mark is open at a tag (a run's span, a ruby's parts, the reading
 * placeholders but the comma's), closes it before the tag and continues it after, so no mark holds a tag: the ruby parts
 * can be moved back and the page's parser never sees a mark crossing an element.
 */
function splitMarksAtTags(html: string): string {
  const out: string[] = []
  const tag = /<[^>]*>/g
  let last = 0
  let part = -1
  let open = false
  let found: RegExpExecArray | null
  while ((found = tag.exec(html))) {
    out.push(html.slice(last, found.index))
    last = tag.lastIndex
    const token = found[0]
    if (token === HEAD_MARK) {
      part++
      open = true
      out.push(`<mark class="search-match" data-search-part="${part}">`)
    } else if (token === '</mark>') {
      open = false
      out.push(token)
    } else if (open && token !== COMMA_OPEN && token !== COMMA_CLOSE) {
      out.push('</mark>', token, `<mark class="${SEARCH_MATCH_CONTINUED_CLASS}" data-search-part="${part}">`)
    } else {
      out.push(token)
    }
  }
  out.push(html.slice(last))
  return out.join('')
}
