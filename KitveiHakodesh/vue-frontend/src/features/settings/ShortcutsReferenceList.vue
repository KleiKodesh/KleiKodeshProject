<template>
  <div class="shortcuts-list">
    <template v-for="group in groups" :key="group.id">
      <div class="shortcuts-group-label">{{ $t('settings.shortcutsList.' + group.label) }}</div>
      <div v-for="row in group.rows" :key="row.def.id" class="shortcut-block">
        <div class="shortcut-row" :class="{ capturing: capturingId === row.def.id }">
          <div class="shortcut-keys">
            <template v-if="capturingId === row.def.id">
              <span class="capture-prompt">{{ $t('settings.shortcutsEdit.pressCombo') }}</span>
            </template>
            <template v-else-if="row.combos.length === 0">
              <span class="no-shortcut">{{ $t('settings.shortcutsEdit.none') }}</span>
            </template>
            <template v-else>
              <template v-for="(combo, ci) in row.combos" :key="combo">
                <span v-if="ci > 0" class="kbd-plus">/</span>
                <template v-for="(key, ki) in keysOf(combo)" :key="ki">
                  <span v-if="ki > 0" class="kbd-plus">+</span><kbd>{{ key }}</kbd>
                </template>
              </template>
            </template>
          </div>
          <span class="shortcut-description">{{ $t('settings.shortcutsList.' + row.def.label) }}</span>
          <div v-if="editable" class="shortcut-actions">
            <span v-if="row.def.fixed" class="fixed-tag" :title="$t('settings.shortcutsEdit.fixedTip')">{{ $t('settings.shortcutsEdit.fixed') }}</span>
            <template v-else>
              <button v-if="capturingId === row.def.id" type="button" class="row-btn" @click="cancelCapture">{{ $t('settings.shortcutsEdit.cancel') }}</button>
              <button v-else type="button" class="row-btn" @click="beginCapture(row.def.id)">{{ $t('settings.shortcutsEdit.change') }}</button>
              <button v-if="store.isOverridden(row.def.id) && capturingId !== row.def.id" type="button" class="row-btn" @click="restore(row.def.id)">{{ $t('settings.shortcutsEdit.restore') }}</button>
            </template>
          </div>
        </div>
        <div v-if="editable && capturingId === row.def.id" class="row-note">{{ $t('settings.shortcutsEdit.captureHint') }}</div>
        <div v-if="row.unreachable" class="row-note warn">{{ $t('settings.shortcutsEdit.unreachableInWord') }}</div>
        <div v-if="editable && notice?.id === row.def.id" class="row-note" :class="notice.kind">
          <span>{{ notice.text }}</span>
          <template v-if="notice.pending">
            <button type="button" class="row-btn" @click="confirmReplace">{{ $t('settings.shortcutsEdit.replace') }}</button>
            <button type="button" class="row-btn" @click="notice = null">{{ $t('settings.shortcutsEdit.cancel') }}</button>
          </template>
        </div>
      </div>
    </template>
    <div v-if="editable" class="shortcuts-footer">
      <div class="shortcuts-hint">{{ $t('settings.shortcutsEdit.hint') }}</div>
      <button type="button" class="settings-btn" :disabled="!store.hasOverrides" @click="restoreAll">{{ $t('settings.shortcutsEdit.restoreAll') }}</button>
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * The keyboard shortcuts list, drawn from `src/shortcuts/shortcutRegistry.ts` with the user's
 * own combinations from the shortcuts store.
 *
 * Read-only in the setup wizard; `editable` in settings, where each changeable row gets
 * "change" (press the new combination - recorded by `shortcuts/shortcutCapture.ts`, which sees
 * the press before any shortcut does) and, once changed, "restore". A combination already in
 * use where this one acts asks before taking it over; a fixed one, a reserved one, or one
 * without Ctrl/Alt (other than F1-F12) is refused with the reason.
 */
import { computed, onBeforeUnmount, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useWindowSize } from '@vueuse/core'
import { isVstoEnvironment } from '@/webview-host/bridge'
import { useShortcutsStore, type ShortcutConflict } from '@/shortcuts/shortcutsStore'
import { SHORTCUTS, SHORTCUT_GROUPS, SHORTCUTS_BY_ID } from '@/shortcuts/shortcutRegistry'
import { comboKeys, comboProblem, comboText, UNREACHABLE_IN_WORD, type Combo } from '@/shortcuts/shortcutCombo'
import { capturingId, startCapture, stopCapture } from '@/shortcuts/shortcutCapture'

const props = defineProps<{ editable?: boolean }>()

const { t } = useI18n()
const store = useShortcutsStore()

const arrows = computed(() => ({
  left: t('settings.shortcutsList.leftArrow'),
  right: t('settings.shortcutsList.rightArrow'),
  up: t('settings.shortcutsList.upArrow'),
  down: t('settings.shortcutsList.downArrow'),
}))
const keysOf = (combo: Combo) => comboKeys(combo, arrows.value)
const textOf = (combo: Combo) => comboText(combo, arrows.value)
const nameOf = (id: string) => {
  const def = SHORTCUTS_BY_ID.get(id)
  return def ? t('settings.shortcutsList.' + def.label) : id
}

// Re-evaluate the width-dependent rows (side commentary) when the window is resized.
const { width } = useWindowSize()

const groups = computed(() => {
  void width.value
  return SHORTCUT_GROUPS.filter((g) => !g.shown || g.shown())
    .map((g) => ({
      ...g,
      rows: SHORTCUTS.filter((d) => d.group === g.id && d.available !== false && (!d.shown || d.shown())).map((def) => {
        const combos = store.combosFor(def.id)
        return { def, combos, unreachable: isVstoEnvironment && combos.some((c) => UNREACHABLE_IN_WORD.has(c)) }
      }),
    }))
    .filter((g) => g.rows.length > 0)
})

interface Notice {
  id: string
  kind: 'error' | 'info'
  text: string
  /** A combination waiting for "replace". */
  pending?: Combo
}
const notice = ref<Notice | null>(null)

function beginCapture(id: string) {
  notice.value = null
  startCapture(id, {
    onCombo: (combo) => tryAssign(id, combo),
    onClear: () => store.assign(id, null),
    onCancel: () => {},
  })
}

function cancelCapture() {
  stopCapture()
}

function conflictText(combo: Combo, conflicts: ShortcutConflict[]) {
  const names = conflicts.map((c) => '"' + nameOf(c.def.id) + '"').join(', ')
  return t('settings.shortcutsEdit.conflict', { combo: textOf(combo), action: names })
}

function tryAssign(id: string, combo: Combo) {
  const problem = comboProblem(combo)
  if (problem) {
    notice.value = { id, kind: 'error', text: t('settings.shortcutsEdit.problem.' + problem, { combo: textOf(combo) }) }
    return
  }
  if (store.combosFor(id).length === 1 && store.combosFor(id)[0] === combo) return
  const conflicts = store.conflictsFor(id, combo)
  const fixed = conflicts.find((c) => c.def.fixed)
  if (fixed) {
    notice.value = { id, kind: 'error', text: t('settings.shortcutsEdit.conflictFixed', { combo: textOf(combo), action: '"' + nameOf(fixed.def.id) + '"' }) }
    return
  }
  if (conflicts.length > 0) {
    notice.value = { id, kind: 'error', text: conflictText(combo, conflicts) + ' ' + t('settings.shortcutsEdit.replaceQuestion'), pending: combo }
    return
  }
  store.assign(id, combo)
}

function confirmReplace() {
  const n = notice.value
  if (!n?.pending) return
  store.assign(n.id, n.pending)
  notice.value = null
}

function restore(id: string) {
  const taken = store.reset(id)
  notice.value = taken.length > 0
    ? { id, kind: 'info', text: t('settings.shortcutsEdit.restoreTaken', { combo: textOf(taken[0].combo), action: '"' + nameOf(taken[0].def.id) + '"' }) }
    : null
}

function restoreAll() {
  stopCapture()
  notice.value = null
  store.resetAll()
}

onBeforeUnmount(() => {
  if (props.editable) stopCapture()
})
</script>

<style scoped>
.shortcuts-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.shortcuts-group-label {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 10px 0 4px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 60%, transparent);
  margin-bottom: 4px;
}

.shortcuts-group-label:first-child {
  padding-top: 0;
}

.shortcut-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  min-height: 30px;
  padding: 0 4px;
  border-radius: 4px;
}

.shortcut-row.capturing {
  background: color-mix(in srgb, var(--accent-color, var(--text-primary)) 12%, transparent);
}

.shortcut-keys {
  display: flex;
  align-items: center;
  gap: 3px;
  flex-shrink: 0;
  flex-wrap: wrap;
}

.shortcut-description {
  font-size: 13px;
  color: var(--text-primary);
  text-align: start;
  flex: 1;
  min-width: 0;
}

.shortcut-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}

.row-btn {
  height: 24px;
  padding: 0 8px;
  font-size: 12px;
  color: var(--text-primary);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  white-space: nowrap;
  cursor: pointer;
}

.row-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.fixed-tag {
  font-size: 11px;
  color: var(--text-secondary);
  padding: 0 6px;
}

.capture-prompt {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
}

.no-shortcut {
  font-size: 12px;
  color: var(--text-secondary);
}

.row-note {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
  font-size: 12px;
  color: var(--text-secondary);
  padding: 0 4px 6px;
}

.row-note.error,
.row-note.warn {
  color: var(--error-color, #c0392b);
}

.shortcuts-footer {
  padding-top: 12px;
}

.shortcuts-hint {
  font-size: 12px;
  color: var(--text-secondary);
}

kbd {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 26px;
  height: 22px;
  padding: 0 6px;
  font-family: 'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif;
  font-size: 11px;
  font-weight: 600;
  color: var(--text-primary);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 1px 0 var(--border-color);
  white-space: nowrap;
  direction: ltr;
}

.kbd-plus {
  font-size: 11px;
  color: var(--text-secondary);
  line-height: 1;
}
</style>
