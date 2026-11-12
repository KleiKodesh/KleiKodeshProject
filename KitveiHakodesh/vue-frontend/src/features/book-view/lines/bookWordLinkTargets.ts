/**
 * A book's word-link point-anchor targets (getWordLinkTargetsForBook), asked ONCE per book
 * view: the lines view's marker treatments, the toolbar's marker dropdown and the
 * Ctrl+Shift+U shortcut all want the same list on open, and the query scans every link of
 * the book (tens of ms on a big one).
 *
 * NOT a cache that outlives anything: BookViewPage provides one per mounted view and it is
 * dropped with it. The shortcut, which lives outside the view, finds a live view's answer
 * through the registry below (removed on dispose) and otherwise asks directly.
 */
import { inject, onScopeDispose, provide, type InjectionKey } from 'vue'
import { getWordLinkTargetsForBook } from '@/webview-host/seforimApi'
import type { WordLinkTargetRow } from '@/webview-host/queries.types'

type Targets = WordLinkTargetRow[] | null

export interface BookWordLinkTargets {
  /** [] = none; null = could not ask (not held, the next call asks again). */
  targetsFor(bookId: number): Promise<Targets>
}

const KEY: InjectionKey<BookWordLinkTargets> = Symbol('bookWordLinkTargets')
const liveViews = new Set<Map<number, Promise<Targets>>>()

function ask(bookId: number): Promise<Targets> {
  // A transport error answers like a null result: "could not ask".
  return getWordLinkTargetsForBook(bookId).catch(() => null)
}

/** One per book view (BookViewPage): its children share it through inject. */
export function provideBookWordLinkTargets(): BookWordLinkTargets {
  const byBook = new Map<number, Promise<Targets>>()
  liveViews.add(byBook)
  onScopeDispose(() => liveViews.delete(byBook))
  const shared: BookWordLinkTargets = {
    targetsFor(bookId) {
      const held = byBook.get(bookId)
      if (held) return held
      const asked = ask(bookId).then((targets) => {
        if (targets == null && byBook.get(bookId) === asked) byBook.delete(bookId)
        return targets
      })
      byBook.set(bookId, asked)
      return asked
    },
  }
  provide(KEY, shared)
  return shared
}

const UNSHARED: BookWordLinkTargets = { targetsFor: ask }

/** The view's shared loader; outside a book view, a plain request each time. */
export function injectBookWordLinkTargets(): BookWordLinkTargets {
  return inject(KEY, UNSHARED)
}

/** For callers outside the view (the shortcut): a live view's answer, else a request. */
export function wordLinkTargetsForOpenBook(bookId: number): Promise<Targets> {
  for (const byBook of liveViews) {
    const held = byBook.get(bookId)
    if (held) return held
  }
  return ask(bookId)
}
