/**
 * The user's own key combinations for the shortcuts in `shortcutRegistry.ts`.
 *
 * Only the changes are stored: `shortcuts.bindings` in localStorage holds
 * `{ [id]: Combo[] }` for every shortcut whose combinations differ from its defaults
 * (an empty array = the user removed it). Everything else follows the registry, so a
 * default changed in a later version reaches every user who never touched that row.
 *
 * Same storage as every other setting (`lsGet`/`lsSet`, see settingsStore). Each host has its
 * own WebView2 profile, so Word's task pane and the standalone app keep separate shortcuts,
 * like every other setting - which is also right on the merits: a combination Word takes
 * first is unusable in the pane but fine in the standalone window.
 *
 * "Reset settings" (settingsStore.reset) clears the key with the other preferences - it is
 * not under a preserved prefix - and the page reloads after it, which re-reads it here.
 */
import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import { lsGet, lsSet } from '@/utils/persistence'
import { comboFromEvent, isWellFormedCombo, type Combo } from './shortcutCombo'
import { SHORTCUTS, SHORTCUTS_BY_ID, scopesOverlap, type ShortcutDef } from './shortcutRegistry'

const STORAGE_KEY = 'shortcuts.bindings'

type Overrides = Record<string, Combo[]>

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

export interface ShortcutConflict {
  def: ShortcutDef
  combo: Combo
}

export const useShortcutsStore = defineStore('shortcuts', () => {
  const overrides = ref<Overrides>(sanitize(lsGet(STORAGE_KEY)))
  watch(overrides, (value) => lsSet(STORAGE_KEY, value), { deep: true })

  function combosFor(id: string): readonly Combo[] {
    return overrides.value[id] ?? SHORTCUTS_BY_ID.get(id)?.defaults ?? []
  }

  /** combination → ids bound to it (several when they live in pages that never overlap). */
  const index = computed(() => {
    const map = new Map<Combo, string[]>()
    for (const def of ACTIVE) {
      if (def.fixed) continue
      for (const combo of combosFor(def.id)) {
        const ids = map.get(combo)
        if (ids) ids.push(def.id)
        else map.set(combo, [def.id])
      }
    }
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

  /**
   * The shortcuts that `combo` would collide with if given to `id`: same combination,
   * scopes that can be active together. Fixed rows count; shortcuts this host lacks don't.
   */
  function conflictsFor(id: string, combo: Combo): ShortcutConflict[] {
    const def = SHORTCUTS_BY_ID.get(id)
    if (!def) return []
    const found: ShortcutConflict[] = []
    for (const other of ACTIVE) {
      if (other.id === id || !scopesOverlap(def.scope, other.scope)) continue
      if (combosFor(other.id).includes(combo)) found.push({ def: other, combo })
    }
    return found
  }

  function write(id: string, combos: Combo[]): void {
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

  function resetAll(): void {
    overrides.value = {}
  }

  const hasOverrides = computed(() => Object.keys(overrides.value).length > 0)

  return { overrides, combosFor, idsFor, isOverridden, conflictsFor, assign, reset, resetAll, hasOverrides }
})
