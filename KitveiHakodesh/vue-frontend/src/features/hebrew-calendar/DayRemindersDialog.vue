<script setup lang="ts">
// The picked day's reminders, as a dialog (the bell in the calendar's top strip): one-off
// notifications Windows shows at their time, even with the app closed (Core WindowsReminders).
// Windows' own schedule is the list - nothing stored here. Same look as ZmanimSettingsDialog;
// not teleported, so it opens in the window that holds the calendar. Esc or the backdrop closes.
import { computed, ref, shallowRef, watch } from 'vue'
import { useEventListener } from '@vueuse/core'
import { HDate } from '@hebcal/core'
import { IconAdd20Regular, IconDelete20Regular, IconDismiss20Regular, IconSpeakerMute20Regular } from '@iconify-prerendered/vue-fluent'
import { serviceCall } from '@/webview-host/serviceClient'
import { showToast } from '@/composables/useToast'
import { t } from '@/i18n'

const props = defineProps<{ date: Date }>()
const emit = defineEmits<{ close: [] }>()

const backdrop = ref<HTMLElement | null>(null)
const hostWindow = computed(() => backdrop.value?.ownerDocument.defaultView ?? null)
useEventListener(hostWindow, 'keydown', (event: KeyboardEvent) => {
  if (event.code !== 'Escape') return
  event.preventDefault()
  if (editing.value !== null) editing.value = null
  else emit('close')
})
const dayLabel = computed(() => new HDate(props.date).renderGematriya())

interface Reminder { id: string; title: string; note: string; time: string; sound: string; style: string; snooze: boolean }

/** The Windows sounds offered (Core WindowsReminders.Sounds); the repeating ones only with the alarm style. */
const SOUNDS = ['none', 'default', 'reminder', 'sms', 'mail', 'im', 'alarm', 'alarm2', 'call'] as const
const REPEATING = new Set(['alarm', 'alarm2', 'call'])
const STYLES = ['brief', 'reminder', 'alarm'] as const
interface RemindersResult { reminders: Reminder[]; error?: string | null }

const isoDay = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

const reminders = shallowRef<Reminder[]>([])
/** The reminder being edited ('' = a new one); null = no form open. */
const editing = ref<string | null>(null)
const title = ref('')
const note = ref('')
const time = ref('20:00')
const sound = ref<string>('default')
const style = ref<string>('reminder')
const snooze = ref(false)
// A repeating sound needs the alarm style; leaving the alarm style drops it back to the default.
watch(style, (st) => { if (st !== 'alarm' && REPEATING.has(sound.value)) sound.value = 'default' })
let generation = 0

async function load() {
  const mine = ++generation
  const day = isoDay(props.date)
  const r = await serviceCall<RemindersResult>('remindersList', { from: day, to: day }).catch(() => null)
  if (mine !== generation) return
  reminders.value = r?.reminders ?? []
  // Nothing on this day yet: the form is open at once - adding is what the user came for.
  if (!reminders.value.length && editing.value === null) startNew()
}
watch(() => isoDay(props.date), () => { editing.value = null; void load() }, { immediate: true })

function startNew() {
  editing.value = ''
  title.value = ''
  note.value = ''
  time.value = '20:00'
  sound.value = 'default'
  style.value = 'reminder'
  snooze.value = false
}

function startEdit(r: Reminder) {
  editing.value = r.id
  title.value = r.title
  note.value = r.note
  time.value = r.time.slice(11, 16)
  style.value = r.style
  sound.value = r.sound
  snooze.value = r.snooze
}

function failed(error?: string | null) {
  showToast(t(error === 'past' ? 'hebrewCalendar.reminders.past' : 'hebrewCalendar.reminders.failed'), { variant: 'error' })
}

async function save() {
  const r = await serviceCall<RemindersResult>('reminderSave', {
    id: editing.value ?? '', title: title.value.trim(), note: note.value.trim(), time: `${isoDay(props.date)}T${time.value}`,
    sound: sound.value, style: style.value, snooze: snooze.value,
  }).catch(() => null)
  if (!r || r.error) { failed(r?.error); return }
  editing.value = null
  void load()
}

async function remove(id: string) {
  const r = await serviceCall<RemindersResult>('reminderDelete', { value: id }).catch(() => null)
  if (!r || r.error) { failed(r?.error); return }
  if (editing.value === id) editing.value = null
  void load()
}
</script>

<template>
  <div ref="backdrop" class="rm-backdrop" @click.self="emit('close')">
  <div class="reminders" role="dialog" aria-modal="true" :aria-label="$t('hebrewCalendar.reminders.title')">
    <div class="rm-head">
      <span class="rm-title">{{ $t('hebrewCalendar.reminders.title') }} · {{ dayLabel }}</span>
      <button class="rm-btn" type="button" :title="$t('hebrewCalendar.reminders.cancel')" @click="emit('close')"><IconDismiss20Regular /></button>
    </div>
    <p class="rm-hint">{{ $t('hebrewCalendar.reminders.hint') }}</p>
    <div v-for="r in reminders" :key="r.id" class="rm-row">
      <button class="rm-item" type="button" :title="$t('hebrewCalendar.reminders.edit')" @click="startEdit(r)">
        <span class="rm-time">{{ r.time.slice(11, 16) }}</span>
        <span class="rm-text">{{ r.title }}</span>
        <IconSpeakerMute20Regular v-if="r.sound === 'none'" class="rm-muted" />
      </button>
      <button class="rm-btn" type="button" :title="$t('hebrewCalendar.reminders.delete')" @click="remove(r.id)"><IconDelete20Regular /></button>
    </div>
    <form v-if="editing !== null" class="rm-form" @submit.prevent="save">
      <div class="rm-line">
        <input v-model="title" class="rm-input rm-grow" type="text" :placeholder="$t('hebrewCalendar.reminders.titlePlaceholder')" autofocus />
        <input v-model="time" class="rm-input rm-time-input" type="time" required />
      </div>
      <input v-model="note" class="rm-input" type="text" :placeholder="$t('hebrewCalendar.reminders.notePlaceholder')" />
      <label class="rm-field">
        <span>{{ $t('hebrewCalendar.reminders.style') }}</span>
        <select v-model="style" class="rm-input">
          <option v-for="st in STYLES" :key="st" :value="st">{{ $t(`hebrewCalendar.reminders.styles.${st}`) }}</option>
        </select>
      </label>
      <label class="rm-field">
        <span>{{ $t('hebrewCalendar.reminders.sound') }}</span>
        <select v-model="sound" class="rm-input">
          <option v-for="so in SOUNDS" :key="so" :value="so" :disabled="REPEATING.has(so) && style !== 'alarm'">{{ $t(`hebrewCalendar.reminders.sounds.${so}`) }}</option>
        </select>
      </label>
      <label class="rm-check"><input v-model="snooze" type="checkbox" /> {{ $t('hebrewCalendar.reminders.snooze') }}</label>
      <div class="rm-line rm-buttons">
        <button class="rm-save" type="submit">{{ $t('hebrewCalendar.reminders.save') }}</button>
        <button class="rm-cancel" type="button" @click="editing = null">{{ $t('hebrewCalendar.reminders.cancel') }}</button>
      </div>
    </form>
    <button v-if="editing === null" class="rm-add" type="button" @click="startNew">
      <IconAdd20Regular /><span>{{ $t('hebrewCalendar.reminders.add') }}</span>
    </button>
  </div>
  </div>
</template>

<style scoped>
.rm-backdrop {
  position: fixed; inset: 0; z-index: 9999;
  display: flex; align-items: center; justify-content: center;
  background: rgba(0, 0, 0, 0.5);
}
.reminders {
  display: flex; flex-direction: column; gap: 4px;
  width: 420px; max-width: calc(100vw - 32px); max-height: calc(100vh - 48px); overflow-y: auto; scrollbar-width: thin;
  padding: 16px 16px 14px;
  background: var(--bg-secondary); border: 1px solid var(--border-color); border-radius: 8px;
}
.rm-hint { margin: 0 0 4px; font-size: 12px; color: var(--text-secondary); }
.rm-head { display: flex; align-items: center; gap: 6px; }
.rm-title { flex: 1; min-width: 0; font-size: 14px; font-weight: 600; color: var(--text-primary); }
.rm-btn {
  flex: none; display: inline-flex; align-items: center; justify-content: center;
  width: 26px; height: 26px; padding: 0;
  background: none; border: 1px solid transparent; border-radius: 4px;
  color: var(--text-secondary); cursor: pointer;
}
.rm-btn:hover { color: var(--text-primary); border-color: var(--border-color); }
.rm-row { display: flex; align-items: center; gap: 2px; }
.rm-item {
  flex: 1; min-width: 0; display: flex; align-items: center; gap: 8px;
  padding: 4px 6px; background: none; border: none; border-radius: 4px;
  font-size: 13px; color: var(--text-primary); text-align: start; cursor: pointer;
}
.rm-item:hover { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.rm-time { font-variant-numeric: tabular-nums; color: var(--text-secondary); }
.rm-text { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.rm-muted { color: var(--text-secondary); }
.rm-form { display: flex; flex-direction: column; gap: 6px; margin-top: 6px; }
.rm-line { display: flex; align-items: center; gap: 6px; }
.rm-grow { flex: 1 1 auto; min-width: 0; }
.rm-buttons { justify-content: flex-end; }
.rm-field { display: flex; align-items: center; gap: 8px; font-size: 13px; color: var(--text-primary); }
.rm-field > span { width: 70px; flex: none; }
.rm-field > select { flex: 1; }
.rm-check { display: flex; align-items: center; gap: 6px; font-size: 13px; color: var(--text-primary); }
.rm-input {
  height: 26px; padding: 0 6px; font-size: 13px;
  background: var(--bg-primary); color: var(--text-primary);
  border: 1px solid var(--border-color); border-radius: 4px;
}
.rm-time-input { width: 96px; }
.rm-save, .rm-cancel {
  height: 26px; padding: 0 10px; font-size: 12px; border-radius: 4px; cursor: pointer;
  border: 1px solid var(--border-color); background: none; color: var(--text-primary);
}
.rm-save { background: var(--accent-color, #0078d4); border-color: transparent; color: #fff; }
.rm-add {
  align-self: flex-start; display: inline-flex; align-items: center; gap: 6px;
  margin-top: 6px; height: 30px; padding: 0 12px; font-size: 13px; cursor: pointer;
  border: 1px solid var(--border-color); border-radius: 4px; background: none; color: var(--text-primary);
}
.rm-add:hover { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.rm-empty { margin: 2px 6px; font-size: 12px; color: var(--text-secondary); }
</style>
