/**
 * Equivalence guard for the PDF.js outline-search port.
 *
 * `public/pdfjs/web/outline-search.js` contains a hand-written, dependency-free
 * copy of SegmentSearchTree so that public/pdfjs/ stays decoupled from the Vue
 * build output (see CUSTOMIZATIONS.md). That duplication is only safe if the two
 * implementations rank identically — this test drives both over the same corpus
 * and asserts the result orders match exactly.
 *
 * If this fails after editing segmentSearchTree.ts, port the same change into
 * outline-search.js (or vice versa).
 */

import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { SegmentSearchTree, tokenizeSegmentText, type SearchableNode } from './segmentSearchTree'

// ─── Load the port ────────────────────────────────────────────────────────────
// outline-search.js is a browser IIFE that self-initialises against the DOM. Pull
// out just the two constructs under test by evaluating it with a stubbed document
// (so initOutlineSearch() bails immediately) and a hook that captures them.

function loadPort(): {
  SegmentSearchTree: new (nodes: { id: number; parentId: number | null; text: string }[]) => {
    search: (
      nodes: { id: number }[],
      query: string,
      limit?: number,
    ) => { id: number }[]
    displayPaths: Map<number, string>
  }
  tokenizeSegmentText: (text: string) => string[]
} {
  const path = fileURLToPath(
    new URL('../../public/pdfjs/web/outline-search.js', import.meta.url),
  )
  const source = readFileSync(path, 'utf8')

  // Expose the internals: append an assignment inside the IIFE's scope by
  // replacing its final closing line with a capture + close.
  const marker = '  if (document.readyState === '
  const idx = source.indexOf(marker)
  if (idx === -1) {
    throw new Error('outline-search.js structure changed — capture marker not found')
  }
  const body = source.slice(0, idx)
  const wrapped = `${body}
  globalThis.__outlineSearchPort = {
    SegmentSearchTree: SegmentSearchTree,
    tokenizeSegmentText: tokenizeSegmentText,
  };
})();`

  // The IIFE only touches `document` inside initOutlineSearch(), which we drop
  // above, so no DOM stub is needed for the captured portion.
  // eslint-disable-next-line @typescript-eslint/no-implied-eval, no-new-func
  new Function(wrapped)()
  const port = (globalThis as Record<string, unknown>).__outlineSearchPort
  if (!port) {
    throw new Error('failed to capture outline-search.js internals')
  }
  return port as ReturnType<typeof loadPort>
}

const port = loadPort()

// ─── Corpus ───────────────────────────────────────────────────────────────────
// A nested TOC shaped like the Hebrew PDFs this feature targets: multi-level
// nesting, repeated numbering across branches, and Talmud page references.

function node(
  id: number,
  parentId: number | null,
  text: string,
  hasChildren = false,
): SearchableNode {
  return { id, parentId, text, hasChildren }
}

const NODES: SearchableNode[] = [
  node(1, null, 'בראשית', true),
  node(2, 1, 'פרק א', true),
  node(3, 2, 'פסוק א'),
  node(4, 2, 'פסוק ב'),
  node(5, 2, 'פסוק ד'),
  node(6, 1, 'פרק ב', true),
  node(7, 6, 'פסוק א'),
  node(8, 6, 'פסוק ד'),
  node(9, 1, 'פרק ד', true),
  node(10, 9, 'פסוק א'),
  node(11, 1, 'פרק ל', true),
  node(12, 11, 'פסוק א'),
  node(13, 1, 'פרק לא', true),
  node(14, 13, 'פסוק א'),
  node(15, null, 'מסכת פסחים', true),
  node(16, 15, 'דף ד.', false),
  node(17, 15, 'דף ד:', false),
  node(18, 15, 'דף ה.', false),
  node(19, null, 'מקורות', true),
  node(20, 19, 'פרק ד'),
  node(21, null, 'הקדמה'),
  node(22, null, 'שער הספר — מהדורת תשע"ה'),
  node(23, null, 'Introduction', true),
  node(24, 23, 'Chapter 1'),
  node(25, 23, 'Chapter 12'),
]

const QUERIES = [
  'פרק',
  'פרק א',
  'פרק ד',
  'פרק ל',
  'פרק לא',
  'פסוק',
  'פסוק ד',
  'בראשית פרק ד',
  'בראשית פסוק א',
  'פרק א פסוק ד',
  'דף ד',
  'דף ד.',
  'דף ד:',
  'פסחים דף ד',
  'פסחים ה',
  'מקורות פרק ד',
  'הקדמה',
  'שער',
  'תשע',
  'chapter',
  'chapter 1',
  'introduction chapter 12',
  'CHAPTER 1',
  'לא קיים',
  '',
  '   ',
  'פרק    ד',
]

describe('outline-search.js port equivalence', () => {
  const original = new SegmentSearchTree(NODES)
  const ported = new port.SegmentSearchTree(NODES)

  it('tokenizes identically', () => {
    const samples = [
      'פרק א',
      'דף ד.',
      'דף ד:',
      'שער הספר — מהדורת תשע"ה',
      'Chapter 12',
      'CHAPTER 12',
      'a.b:c',
      '.leading',
      'trailing.',
      '',
      '   ',
      '—',
      'מסכת   פסחים',
      '😀 emoji',
      '😀.dot',
      'x😀y',
    ]
    for (const s of samples) {
      expect(port.tokenizeSegmentText(s), `tokenize(${JSON.stringify(s)})`).toEqual(
        tokenizeSegmentText(s),
      )
    }
  })

  it('builds identical display paths', () => {
    for (const n of NODES) {
      expect(ported.displayPaths.get(n.id), `displayPath(${n.id})`).toBe(
        original.displayPaths.get(n.id),
      )
    }
  })

  it.each(QUERIES)('ranks identically for %o', (query) => {
    const expected = original.search(NODES, query, 100).map((n) => n.id)
    const actual = ported.search(NODES, query, 100).map((n) => n.id)
    expect(actual).toEqual(expected)
  })

  it('honours the result limit identically', () => {
    for (const limit of [0, 1, 2, 5]) {
      const expected = original.search(NODES, 'פסוק', limit).map((n) => n.id)
      const actual = ported.search(NODES, 'פסוק', limit).map((n) => n.id)
      expect(actual, `limit=${limit}`).toEqual(expected)
    }
  })

  it('matches on a leaf-only candidate set (flat-list usage)', () => {
    const leaves = NODES.filter((n) => !n.hasChildren)
    for (const query of QUERIES) {
      const expected = original.search(leaves, query, 100).map((n) => n.id)
      const actual = ported.search(leaves, query, 100).map((n) => n.id)
      expect(actual, `leaves: ${JSON.stringify(query)}`).toEqual(expected)
    }
  })
})
