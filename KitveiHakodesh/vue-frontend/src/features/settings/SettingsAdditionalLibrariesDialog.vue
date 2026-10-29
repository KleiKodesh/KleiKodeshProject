<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useEventListener } from '@vueuse/core'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import SettingRow from './SettingRow.vue'
import ToggleGroup from './ToggleGroup.vue'
import { IconFolderAdd20Regular, IconDelete20Regular } from '@iconify-prerendered/vue-fluent'
import {
  getAdditionalSeforimLibraries,
  pickAdditionalSeforimLibrary,
  removeAdditionalSeforimLibrary,
  pingServiceInstance,
  startServiceNow,
  type AdditionalLibraryInfo,
} from '@/webview-host/bridge'
import {
  additionalLibraryErrorText,
  additionalLibraryReasonText,
  describeFailure,
  restoreServiceAfterChange,
} from './additionalLibrariesFlow'

// Visual twin of SettingsExcludedFoldersDialog (same list-box + footer layout, same RTL
// button mirroring), but NOT the same persistence model: excluded folders are batched and
// committed on אישור, while every add/remove here goes straight to the service — same as the
// primary DB path row right above it in SettingsPageAdvancedSection — because the service
// resolves its attached libraries once, at construction, and restarts itself to apply a
// change (RestartSoon in Dispatcher.cs). So there is no אישור/ביטול: the list IS the live
// state, and the only thing to close is the dialog itself.

const emit = defineEmits<{
  close: []
}>()

// A display preference, not a library change: saved on the spot, no restart, and the catalog
// cards update live (they read the same store ref).
const { showLibraryBadge } = storeToRefs(useSettingsStore())

const libraries = ref<AdditionalLibraryInfo[]>([])
const selectedPath = ref<string | null>(null)
const isLoading = ref(true)
const isBusy = ref(false)
const isRestarting = ref(false)
const errorMessage = ref('')
/** What the restart wait is really doing right now (shown instead of a bare "restarting"). */
const restartStatusText = ref('')
/** True once the wait gave up: the dialog offers "try starting the service again". */
const restartFailed = ref(false)
/** The data host that answered before the last change (see pingServiceInstance). */
let instanceBeforeChange: string | undefined

/** Set when the LIST could not be loaded (as opposed to "no libraries configured"). */
const loadError = ref('')

async function loadLibraries() {
  isLoading.value = true
  loadError.value = ''
  try {
    libraries.value = await getAdditionalSeforimLibraries()
  } catch (err) {
    loadError.value = describeFailure(err)
  } finally {
    // Always cleared: the dialog used to stay on "טוען…" forever when the call failed.
    isLoading.value = false
  }
}

async function rememberInstance() {
  try { instanceBeforeChange = await pingServiceInstance() } catch { instanceBeforeChange = undefined }
}

onMounted(() => {
  void loadLibraries()
  void rememberInstance()
})

useEventListener('keydown', (event: KeyboardEvent) => {
  if (event.code === 'Escape' && !isRestarting.value) {
    event.preventDefault()
    emit('close')
  }
})

/** The service resolves additional libraries when its data host is built, so a change makes it
 *  rebuild that host (inside the running service - it normally never goes down), then the page
 *  reloads. We wait until a REBUILT host answers. If nothing answers for a few seconds the
 *  service is started from here (no prompt), again and again, and only when that also fails
 *  does the dialog say so - and offer to try once more. */
async function reloadAfterRestart() {
  isRestarting.value = true
  restartFailed.value = false
  errorMessage.value = ''
  restartStatusText.value = 'טוען מחדש את הספריות…'
  const result = await restoreServiceAfterChange({
    ping: pingServiceInstance,
    startService: startServiceNow,
    previousInstance: instanceBeforeChange,
    onStatus: (status, attempt) => {
      restartStatusText.value =
        status === 'starting'
          ? `שירות הנתונים אינו עונה — מפעיל אותו (ניסיון ${attempt})…`
          : 'טוען מחדש את הספריות…'
    },
  })
  if (result.ok) {
    window.location.reload()
    return
  }
  isRestarting.value = false
  restartFailed.value = true
  restartStatusText.value = ''
  errorMessage.value =
    'שירות הנתונים אינו פועל ולא הצלחנו להפעיל אותו אוטומטית' +
    (result.startAttempts > 0 ? ` (${result.startAttempts} ניסיונות)` : '') +
    '. אפשר ללחוץ "הפעל את השירות שוב", או לסגור ולפתוח מחדש את התוכנה.'
}

async function retryStartService() {
  if (isRestarting.value) return
  instanceBeforeChange = undefined // whatever answers now is the rebuilt service
  await reloadAfterRestart()
}

async function addLibrary() {
  if (isBusy.value || isRestarting.value) return
  isBusy.value = true
  errorMessage.value = ''
  try {
    const result = await pickAdditionalSeforimLibrary()
    if (!result) return // the user cancelled the native dialog
    libraries.value = result.libraries
    if (result.error) {
      errorMessage.value = additionalLibraryErrorText(result.error)
    } else if (result.restarting) {
      await reloadAfterRestart()
    }
  } catch (err) {
    errorMessage.value = describeFailure(err)
  } finally {
    isBusy.value = false
  }
}

async function removeSelected() {
  if (!selectedPath.value || isBusy.value || isRestarting.value) return
  isBusy.value = true
  errorMessage.value = ''
  try {
    const result = await removeAdditionalSeforimLibrary(selectedPath.value)
    libraries.value = result.libraries
    if (result.error) {
      errorMessage.value = additionalLibraryErrorText(result.error)
    } else {
      selectedPath.value = null
      if (result.restarting) await reloadAfterRestart()
    }
  } catch (err) {
    errorMessage.value = describeFailure(err)
  } finally {
    isBusy.value = false
  }
}

/** Per-row status text — null when the library is simply working. */
function statusText(library: AdditionalLibraryInfo): string | null {
  if (!library.exists) return 'הקובץ לא נמצא'
  if (!library.attached)
    return additionalLibraryReasonText(library.reason) ?? 'הספרייה לא נטענה (סיבה לא ידועה)'
  return null
}
</script>

<template>
  <Teleport to="body">
    <div class="additional-backdrop" @click.self="!isRestarting && emit('close')">
      <div class="additional-dialog" role="dialog" aria-label="ספריות נוספות">
        <p class="additional-title">ספריות נוספות</p>
        <p class="additional-description">
          ספריות seforim.db נוספות (לדוגמה: אוסף אישי) שייטענו לצד מסד הנתונים הראשי, לעיון
          וקריאה בלבד. כל שינוי כאן טוען מחדש את הספריות בשירות הנתונים.
        </p>

        <div class="additional-list">
          <p v-if="isLoading" class="additional-empty">טוען…</p>
          <div v-else-if="loadError" class="additional-empty">
            <p class="additional-error">{{ loadError }}</p>
            <button class="additional-add-button" @click="loadLibraries">נסה שוב</button>
          </div>
          <p v-else-if="libraries.length === 0" class="additional-empty">
            לא הוגדרו ספריות נוספות
          </p>
          <div
            v-for="library in libraries"
            :key="library.path"
            class="additional-item"
            :class="{ selected: library.path === selectedPath }"
            role="option"
            :aria-selected="library.path === selectedPath"
            @click="selectedPath = library.path"
          >
            <span class="additional-item-path">{{ library.path }}</span>
            <span v-if="statusText(library)" class="additional-item-warning">
              {{ statusText(library) }}
            </span>
          </div>
        </div>

        <SettingRow
          class="additional-badge-row"
          label="הצג תווית ספרייה בכרטיסים"
          hint="מציג על כל ספר או תיקייה מספרייה נוספת תווית קטנה עם שם הספרייה. כשהתווית מוסתרת, שם הספרייה עדיין מופיע בחלונית המידע בעת מעבר עם העכבר"
        >
          <ToggleGroup
            v-model="showLibraryBadge"
            :options="[
              { label: 'כן', value: true },
              { label: 'לא', value: false },
            ]"
          />
        </SettingRow>

        <p v-if="errorMessage" class="additional-error">{{ errorMessage }}</p>
        <p v-if="isRestarting" class="additional-restarting">{{ restartStatusText }}</p>
        <button v-if="restartFailed && !isRestarting" class="additional-add-button" @click="retryStartService">
          הפעל את השירות שוב
        </button>

        <div class="additional-actions">
          <button class="additional-close-button" :disabled="isRestarting" @click="emit('close')">
            סגור
          </button>
          <div class="additional-actions-spacer" />
          <button class="additional-add-button" :disabled="isBusy || isRestarting" @click="addLibrary">
            <IconFolderAdd20Regular />
            <span>הוסף ספרייה</span>
          </button>
          <button
            class="additional-remove-button"
            :disabled="!selectedPath || isBusy || isRestarting"
            @click="removeSelected"
          >
            <IconDelete20Regular />
            <span>הסר</span>
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.additional-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}

.additional-dialog {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 20px 20px 14px;
  width: 460px;
  max-width: calc(100vw - 32px);
  direction: rtl;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.additional-badge-row {
  margin-bottom: 0;
}

.additional-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}

.additional-description {
  margin: 0;
  font-size: 12px;
  color: var(--text-secondary);
  line-height: 1.6;
  text-align: justify;
}

.additional-list {
  height: 200px;
  overflow-y: auto;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  padding: 2px;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.additional-empty {
  margin: 0;
  padding: 8px 6px;
  font-size: 12px;
  color: var(--text-secondary);
}

.additional-item {
  min-height: 26px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  padding: 3px 6px;
  font-size: 12px;
  line-height: 1.3;
  color: var(--text-primary);
  border-radius: 3px;
  direction: ltr;
  text-align: left;
}

.additional-item-path {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.additional-item-warning {
  color: var(--status-danger);
  font-size: 11px;
}

.additional-item:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.additional-item.selected {
  background: color-mix(in srgb, var(--accent-color) 22%, transparent);
}

.additional-error {
  margin: 0;
  font-size: 11px;
  color: var(--status-danger);
}

.additional-restarting {
  margin: 0;
  font-size: 11px;
  color: var(--text-secondary);
}

.additional-actions {
  display: flex;
  /* row-reverse in the RTL dialog = physical left-to-right DOM order — same convention as
     SettingsExcludedFoldersDialog/ConfirmDialog: סגור at the far physical LEFT, הסר at the
     far physical RIGHT with הוסף ספרייה to its left. */
  flex-direction: row-reverse;
  align-items: center;
  gap: 8px;
  padding-top: 10px;
  border-top: 1px solid var(--border-color);
}

.additional-actions-spacer {
  flex: 1;
}

.additional-actions button {
  height: 30px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 0 14px;
  font-size: 12px;
  border-radius: 4px;
}

.additional-actions button svg {
  width: 16px;
  height: 16px;
  flex: none;
  color: inherit;
}

.additional-actions button:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.additional-close-button {
  border: 1px solid transparent;
  background: var(--accent-color);
  color: #fff;
}
.additional-close-button:hover:not(:disabled) {
  background: color-mix(in srgb, var(--accent-color) 85%, #000);
}

.additional-add-button {
  border: 1px solid var(--border-color);
  background: var(--control-bg);
  color: var(--text-primary);
}
.additional-add-button:hover:not(:disabled) {
  background: var(--control-bg-hover);
}

.additional-remove-button {
  border: 1px solid color-mix(in srgb, var(--status-danger) 40%, transparent);
  background: color-mix(in srgb, var(--status-danger) 8%, transparent);
  color: var(--status-danger);
}
.additional-remove-button:hover:not(:disabled) {
  background: color-mix(in srgb, var(--status-danger) 16%, transparent);
}
</style>
