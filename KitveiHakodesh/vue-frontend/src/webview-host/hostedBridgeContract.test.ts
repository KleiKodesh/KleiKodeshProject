import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'

/**
 * The hosted (WebView2) side of the typed seforim and dictionary bridges.
 *
 * Both of these were broken in review, in ways that compile cleanly and fail only at runtime
 * in the packaged app — which is the one place neither a build nor the rest of this suite
 * looks. They are cheap to pin down here, so they are pinned down here.
 *
 *   1. ARGUMENTS MUST BE NESTED. JsBridge posts the message as
 *      Object.assign({ id }, payload), where `id` is the bridge's own correlation id. An
 *      argument spread onto the root called `id` therefore OVERWRITES it, the reply can never
 *      be routed back, and the call hangs forever with the throw swallowed in C#'s message
 *      loop. getBookById takes exactly such an argument.
 *
 *   2. REPLIES MUST BE CAMEL-CASED. C# serializes Core's models with no naming policy, so rows
 *      arrive PascalCase while the row types here are camelCase. Without a transform every
 *      field reads back `undefined` — total breakage of the catalog, book text, TOC and
 *      commentary, with nothing throwing anywhere.
 */

declare global {
  interface Window {
    __webviewAction?: (action: string, args?: object) => Promise<unknown>
  }
}

/** The last message the code under test handed to the bridge. */
let lastAction: { action: string; args: Record<string, unknown> } | null = null

/** What the fake C# side replies with. Set per test. */
let reply: unknown = { rows: [] }

beforeEach(() => {
  lastAction = null
  reply = { rows: [] }
  ;(globalThis as unknown as { window: Window }).window =
    globalThis as unknown as Window
  window.__webviewAction = (action: string, args?: object) => {
    lastAction = { action, args: (args ?? {}) as Record<string, unknown> }
    return Promise.resolve(reply)
  }
})

afterEach(() => {
  delete window.__webviewAction
  vi.resetModules()
})

describe('seforim bridge — argument envelope', () => {
  it('nests arguments under params instead of spreading them onto the message', async () => {
    const { getAllTocEntries } = await import('./seforimApi')
    reply = { rows: [] }

    await getAllTocEntries(42)

    expect(lastAction?.action).toBe('seforimQuery')
    expect(lastAction?.args).toEqual({ op: 'getAllTocEntries', params: { bookId: 42 } })
  })

  it('keeps an argument named id OUT of the message root', async () => {
    // The regression this exists for: `id` at the root is the bridge's correlation id, so a
    // spread argument of that name silently hijacks it and the promise never settles.
    const { getBookById } = await import('./seforimApi')
    reply = { book: null }

    await getBookById(7)

    expect(lastAction?.args.id).toBeUndefined()
    expect(lastAction?.args.params).toEqual({ id: 7 })
  })
})

describe('seforim bridge — reply decoding', () => {
  it('camel-cases the PascalCase property names C# sends', async () => {
    const { getLinesPaged } = await import('./seforimApi')
    reply = { rows: [{ Id: 11, LineIndex: 3, Content: 'text' }] }

    const rows = await getLinesPaged(1, 10, 0)

    expect(rows).toEqual([{ id: 11, lineIndex: 3, content: 'text' }])
  })

  it('decodes nested objects and arrays, not just the top level', async () => {
    const { getWordLinkAnchorsForLines } = await import('./seforimApi')
    reply = {
      supported: true,
      rows: [{ LineId: 5, CharStart: 0, TargetBookId: 9 }],
    }

    const anchors = await getWordLinkAnchorsForLines([5])

    expect(anchors).toEqual([{ lineId: 5, charStart: 0, targetBookId: 9 }])
  })

  it('turns an error reply into a rejection rather than a row set', async () => {
    const { getAllBooks } = await import('./seforimApi')
    reply = { error: 'No database loaded' }

    await expect(getAllBooks()).rejects.toThrow('No database loaded')
  })
})

describe('dictionary bridge', () => {
  it('nests its arguments the same way', async () => {
    const { dictLinks } = await import('./dictionaryDb')
    reply = { links: [] }

    await dictLinks('word')

    expect(lastAction?.action).toBe('dictQuery')
    expect(lastAction?.args).toEqual({ op: 'dictLinks', params: { term: 'word' } })
  })

  it('leaves source_id alone — it is snake_case on this wire by [JsonPropertyName]', async () => {
    // C# projects sense rows by hand precisely so this one key survives; a blanket
    // camel-casing of the reply would rename it to sourceId and the senses would lose
    // their source attribution.
    const { dictAbbrevSenses } = await import('./dictionaryDb')
    reply = {
      matched: 'w',
      rows: [{ headword: 'w', nikud: null, text: 't', source: 's', source_id: 4 }],
    }

    const result = await dictAbbrevSenses(['w'])

    expect(result?.rows[0]?.source_id).toBe(4)
  })
})
