<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useEventListener } from '@vueuse/core'
import { IconFolderAdd20Regular, IconDelete20Regular } from '@iconify-prerendered/vue-fluent'
import {
  getAdditionalSeforimLibraries,
  pickAdditionalSeforimLibrary,
  removeAdditionalSeforimLibrary,
  type AdditionalLibraryInfo,
} from '@/webview-host/bridge'

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

const libraries = ref<AdditionalLibraryInfo[]>([])
const selectedPath = ref<string | null>(null)
const isLoading = ref(true)
const isBusy = ref(false)
const isRestarting = ref(false)
const errorMessage = ref('')

onMounted(async () => {
  libraries.value = await getAdditionalSeforimLibraries()
  isLoading.value = false
})

useEventListener('keydown', (event: KeyboardEvent) => {
  if (event.code === 'Escape' && !isRestarting.value) {
    event.preventDefault()
    emit('close')
  }
})

/** The service resolves additional libraries once, at construction, exactly like the primary
 *  DB path — so a change needs the same restart-then-reload the primary path field does
 *  (onDbPathChanged in webview-host/seforimDb.ts). 800ms matches that delay. */
function scheduleReloadAfterRestart() {
  isRestarting.value = true
  setTimeout(() => window.location.reload(), 800)
}

function additionalLibraryErrorText(code: string): string {
  switch (code) {
    case 'empty path':
      return 'לא נבחר נתיב'
    case "path contains ';' or '|'":
      return "הנתיב מכיל תו שאינו נתמך (';' או '|')"
    case 'already configured':
      return 'הספרייה הזו כבר מוגדרת'
    case 'no more library slots available':
      return 'הגעת למספר המרבי של ספריות נוספות שניתן להוסיף בהתקנה זו'
    default:
      return 'לא ניתן היה להוסיף את הספרייה'
  }
}

async function addLibrary() {
  if (isBusy.value || isRestarting.value) return
  isBusy.value = true
  errorMessage.value = ''
  try {
    const result = await pickAdditionalSeforimLibrary()
    if (!result) return // cancelled, or the native dialog failed outright
    libraries.value = result.libraries
    if (result.error) {
      errorMessage.value = additionalLibraryErrorText(result.error)
    } else if (result.restarting) {
      scheduleReloadAfterRestart()
    }
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
    selectedPath.value = null
    if (result.restarting) scheduleReloadAfterRestart()
  } finally {
    isBusy.value = false
  }
}

/** Per-row status text — null when the library is simply working. */
function statusText(library: AdditionalLibraryInfo): string | null {
  if (!library.exists) return 'הקובץ לא נמצא'
  if (!library.attached) return 'לא תואמת לסכמת seforim.db, או שה־id-ים שלה חורגים מהמותר'
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
          וקריאה בלבד. כל שינוי כאן מפעיל מחדש את שירות הנתונים.
        </p>

        <div class="additional-list">
          <p v-if="isLoading" class="additional-empty">טוען…</p>
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

        <p v-if="errorMessage" class="additional-error">{{ errorMessage }}</p>
        <p v-if="isRestarting" class="additional-restarting">מפעיל מחדש את השירות…</p>

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
