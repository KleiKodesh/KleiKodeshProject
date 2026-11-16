/**
 * The user's own key combinations: changes to the shortcuts in `shortcutRegistry.ts`, and
 * the personal shortcuts the user creates (today: "open this book").
 *
 * Only the changes are stored: `shortcuts.bindings` in localStorage holds
 * `{ [id]: Combo[] }` for every registry shortcut whose combinations differ from its
 * defaults (an empty array = the user removed it). Everything else follows the registry, so
 * a default changed in a later version reaches every user who never touched that row.
 * Personal shortcuts are a list of their own, `shortcuts.personal`.
 *
 * Same storage as every other setting (`lsGet`/`lsSet`, see settingsStore). Each host has its
 * own WebView2 profile, so Word's task pane and the standalone app keep separate shortcuts,
 * like every other setting - which is also right on the merits: a combination Word takes
 * first is unusable in the pane but fine in the standalone window.
 *
 * "Reset settings" (settingsStore.reset) clears both keys with the other preferences - they
 * are not under a preserved prefix - and the page reloads after it, which re-reads them here.
 */
import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import { lsGet, lsSet } from '@/utils/persistence'
import { comboFromEvent, isWellFormedCombo, type Combo } from './shortcutCombo'
import { SHORTCUTS, SHORTCUTS_BY_ID, scopesOverlap, type ShortcutDef } from './shortcutRegistry'

const STORAGE_KEY = 'shortcuts.bindings'
const PERSONAL_KEY = 'shortcuts.personal'

type Overrides = Record<string, Combo[]>

/**
 * A shortcut the user made. Acts in the focused pane, like the quick-navigation keys.
 * `title` is kept so the row can be drawn before the catalog has loaded.
 */
export interface PersonalShortcut {
  id: string
  kind: 'openBook'
  bookId: number
  title: string
  combo: Combo | null
}

export const PERSONAL_PREFIX = 'personal.'
export const isPersonalId = (id: string) => id.startsWith(PERSONAL_PREFIX)

/** Shortcuts that exist in this host. */
const ACTIVE: readonly ShortcutDef[] = SHORTCUTS.filter((d) => d.available !== false)

function sameCombos(a: readonly Combo[], b: readonly Combo[]): boolean {
  return a.length === b.length && a.every((c, i) => c === b[i])
}

/** Drops unknown ids, fixed rows, malformed combinations and entries equal to the default. */
function sanitize(raw: unknown): Overrides {
  const out: Overrides = {}
  if (!raw || typeof raw !== 'object') return out
  for (const [id, combos] of Object.entries(raw as Record<string, unknown>)) {
    const def = SHORTCUTS_BY_ID.get(id)
    if (!def || def.fixed || !Array.isArray(combos)) continue
    const clean = [...new Set(combos.filter(isWellFormedCombo))]
    if (!sameCombos(clean, def.defaults)) out[id] = clean
  }
  return out
}

function sanitizePersonal(raw: unknown): PersonalShortcut[] {
  if (!Array.isArray(raw)) return []
  const seen = new Set<string>()
  const out: PersonalShortcut[] = []
  for (const p of raw as Partial<PersonalShortcut>[]) {
    if (!p || typeof p.id !== 'string' || !isPersonalId(p.id) || seen.has(p.id)) continue
    if (p.kind !== 'openBook' || typeof p.bookId !== 'number' || typeof p.title !== 'string') continue
    seen.add(p.id)
    out.push({ id: p.id, kind: 'openBook', bookId: p.bookId, title: p.title, combo: isWellFormedCombo(p.combo) ? p.combo : null })
  }
  return out
}

/** A personal shortcut in the registry's shape, for conflict reports. */
function personalDef(p: PersonalShortcut): ShortcutDef {
  return { id: p.id, group: 'personal', scope: 'pane', defaults: [], label: '' }
}

export interface ShortcutConflict {
  def: ShortcutDef
  combo: Combo
}

export const useShortcutsStore = defineStore('shortcuts', () => {
  const overrides = ref<Overrides>(sanitize(lsGet(STORAGE_KEY)))
  watch(overrides, (value) => lsSet(STORAGE_KEY, value), { deep: true })

  const personal = ref<PersonalShortcut[]>(sanitizePersonal(lsGet(PERSONAL_KEY)))
  watch(personal, (value) => lsSet(PERSONAL_KEY, value), { deep: true })

  function personalById(id: string): PersonalShortcut | undefined {
    return personal.value.find((p) => p.id === id)
  }

  function combosFor(id: string): readonly Combo[] {
    if (isPersonalId(id)) {
      const combo = personalById(id)?.combo
      return combo ? [combo] : []
    }
    return overrides.value[id] ?? SHORTCUTS_BY_ID.get(id)?.defaults ?? []
  }

  /** combination → ids bound to it (several when they live in pages that never overlap). */
  const index = computed(() => {
    const map = new Map<Combo, string[]>()
    const add = (combo: Combo, id: string) => {
      const ids = map.get(combo)
      if (ids) ids.push(id)
      else map.set(combo, [id])
    }
    for (const def of ACTIVE) {
      if (def.fixed) continue
      for (const combo of combosFor(def.id)) add(combo, def.id)
    }
    for (const p of personal.value) if (p.combo) add(p.combo, p.id)
    return map
  })

  const NONE: readonly string[] = []

  /** The changeable shortcuts this keydown is bound to (usually zero or one). */
  function idsFor(e: KeyboardEvent): readonly string[] {
    const combo = comboFromEvent(e)
    return (combo && index.value.get(combo)) || NONE
  }

  function isOverridden(id: string): boolean {
    return id in overrides.value
  }

  function scopeOf(id: string) {
    return isPersonalId(id) ? 'pane' : SHORTCUTS_BY_ID.get(id)?.scope
  }

  /**
   * The shortcuts that `combo` would collide with if given to `id`: same combination,
   * scopes that can be active together. Fixed rows count; shortcuts this host lacks don't.
   */
  function conflictsFor(id: string, combo: Combo): ShortcutConflict[] {
    const scope = scopeOf(id)
    if (!scope) return []
    const found: ShortcutConflict[] = []
    for (const other of ACTIVE) {
      if (other.id === id || !scopesOverlap(scope, other.scope)) continue
      if (combosFor(other.id).includes(combo)) found.push({ def: other, combo })
    }
    // Personal shortcuts act pane-wide, so they collide with everything.
    for (const p of personal.value) {
      if (p.id !== id && p.combo === combo) found.push({ def: personalDef(p), combo })
    }
    return found
  }

  function write(id: string, combos: Combo[]): void {
    if (isPersonalId(id)) {
      const p = personalById(id)
      if (p) p.combo = combos[0] ?? null
      return
    }
    const def = SHORTCUTS_BY_ID.get(id)
    if (!def || def.fixed) return
    const next = { ...overrides.value }
    if (sameCombos(combos, def.defaults)) delete next[id]
    else next[id] = combos
    overrides.value = next
  }

  /**
   * Give `id` exactly this one combination (null = no shortcut). Every changeable shortcut it
   * collided with loses that combination; the caller has already asked the user. A fixed
   * collision is never resolved here - the grid refuses it before calling.
   */
  function assign(id: string, combo: Combo | null): void {
    if (combo) {
      for (const { def } of conflictsFor(id, combo)) {
        if (!def.fixed) write(def.id, combosFor(def.id).filter((c) => c !== combo))
      }
    }
    write(id, combo ? [combo] : [])
  }

  /**
   * Back to the default combinations - except any that another shortcut has been given
   * since, which stay with that shortcut (taking them back silently would break the row the
   * user changed on purpose). Returns those, so the grid can say why the row is not fully
   * back to default.
   */
  function reset(id: string): ShortcutConflict[] {
    const def = SHORTCUTS_BY_ID.get(id)
    if (!def || !isOverridden(id)) return []
    const taken: ShortcutConflict[] = []
    const kept = def.defaults.filter((combo) => {
      const hit = conflictsFor(id, combo)
      taken.push(...hit)
      return hit.length === 0
    })
    write(id, kept)
    return taken
  }

  /**
   * Every app shortcut back to its defaults. Personal shortcuts stay, but one that sits on a
   * key a default now needs again loses that key (the user asked for the defaults back, and
   * two actions on one key would make both unreliable). Returns those personal shortcuts.
   */
  function resetAll(): PersonalShortcut[] {
    overrides.value = {}
    const cleared: PersonalShortcut[] = []
    for (const p of personal.value) {
      if (!p.combo) continue
      const combo = p.combo
      if (ACTIVE.some((d) => combosFor(d.id).includes(combo))) {
        p.combo = null
        cleared.push(p)
      }
    }
    return cleared
  }

  const hasOverrides = computed(() => Object.keys(overrides.value).length > 0)

  /** A new "open this book" shortcut, with no keys yet. Returns its id. */
  function addPersonalBook(bookId: number, title: string): string {
    const id = PERSONAL_PREFIX + Date.now().toString(36) + Math.random().toString(36).slice(2, 6)
    personal.value = [...personal.value, { id, kind: 'openBook', bookId, title, combo: null }]
    return id
  }

  function removePersonal(id: string): void {
    personal.value = personal.value.filter((p) => p.id !== id)
  }

  return {
    overrides, personal, personalById, combosFor, idsFor, isOverridden, conflictsFor,
    assign, reset, resetAll, hasOverrides, addPersonalBook, removePersonal,
  }
})
