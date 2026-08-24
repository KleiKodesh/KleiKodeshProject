<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useEventListener } from '@vueuse/core'
import { IconFolderAdd20Regular, IconDelete20Regular } from '@iconify-prerendered/vue-fluent'
import { getExcludedFolders, setExcludedFolders, pickFolder } from '@/webview-host/bridge'

// Dev-mode counterpart of the hosted app's WinForms ExcludedFoldersForm. Same behaviour —
// list, add via the native folder dialog, remove the selected row, confirm to persist — and
// the same storage format (excluded_folders.json beside the file-search index directory) via the
// service — resolved from the dev service's own bin folder, so it is a dev-local list.
// Cancel discards: nothing is written until אישור.

const emit = defineEmits<{
  close: []
}>()

const folders = ref<string[]>([])
const selectedFolder = ref<string | null>(null)
const isLoading = ref(true)
const isSaving = ref(false)
const errorMessage = ref('')

onMounted(async () => {
  folders.value = await getExcludedFolders()
  isLoading.value = false
})

useEventListener('keydown', (event: KeyboardEvent) => {
  if (event.code === 'Escape') {
    event.preventDefault()
    emit('close')
  }
  // AcceptButton parity with the WinForms form (and ConfirmDialog): Enter = אישור.
  if (event.code === 'Enter' && !isSaving.value) {
    event.preventDefault()
    void saveAndClose()
  }
})

async function addFolder() {
  const picked = await pickFolder('בחר תיקייה להחרגה מחיפוש הקבצים')
  if (!picked) return

  // Already listed — select it and scroll it into view instead of adding a duplicate.
  const existing = folders.value.find((f) => f.toLowerCase() === picked.toLowerCase())
  if (existing) {
    selectedFolder.value = existing
    return
  }

  folders.value.push(picked)
  selectedFolder.value = picked
}

function removeSelectedFolder() {
  if (!selectedFolder.value) return
  folders.value = folders.value.filter((f) => f !== selectedFolder.value)
  selectedFolder.value = null
}

async function saveAndClose() {
  isSaving.value = true
  errorMessage.value = ''
  try {
    await setExcludedFolders(folders.value)
    emit('close')
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : 'שמירת התיקיות נכשלה'
  } finally {
    isSaving.value = false
  }
}
</script>

<template>
  <Teleport to="body">
    <div class="excluded-backdrop" @click.self="emit('close')">
      <div class="excluded-dialog" role="dialog" aria-label="תיקיות מוחרגות מחיפוש קבצים">
        <p class="excluded-title">תיקיות מוחרגות מחיפוש קבצים</p>
        <p class="excluded-description">
          תיקיות אלה יוחרגו מתוצאות חיפוש הקבצים. השינויים נכנסים לתוקף מיד.
        </p>

        <div class="excluded-list">
          <p v-if="isLoading" class="excluded-empty">טוען…</p>
          <p v-else-if="folders.length === 0" class="excluded-empty">לא הוגדרו תיקיות מוחרגות</p>
          <div
            v-for="folder in folders"
            :key="folder"
            class="excluded-item"
            :class="{ selected: folder === selectedFolder }"
            role="option"
            :aria-selected="folder === selectedFolder"
            @click="selectedFolder = folder"
          >
            {{ folder }}
          </div>
        </div>

        <p v-if="errorMessage" class="excluded-error">{{ errorMessage }}</p>

        <div class="excluded-actions">
          <button class="excluded-ok-button" :disabled="isSaving" @click="saveAndClose">
            אישור
          </button>
          <button class="excluded-cancel-button" @click="emit('close')">ביטול</button>
          <div class="excluded-actions-spacer" />
          <button class="excluded-add-button" @click="addFolder">
            <IconFolderAdd20Regular />
            <span>הוסף תיקייה</span>
          </button>
          <button
            class="excluded-remove-button"
            :disabled="!selectedFolder"
            @click="removeSelectedFolder"
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
.excluded-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}

.excluded-dialog {
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

.excluded-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}

.excluded-description {
  margin: 0;
  font-size: 12px;
  color: var(--text-secondary);
  line-height: 1.6;
  text-align: justify;
}

.excluded-list {
  height: 200px;
  overflow-y: auto;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  padding: 2px;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.excluded-empty {
  margin: 0;
  padding: 8px 6px;
  font-size: 12px;
  color: var(--text-secondary);
}

.excluded-item {
  height: 26px;
  display: flex;
  align-items: center;
  padding: 0 6px;
  font-size: 12px;
  line-height: 1;
  color: var(--text-primary);
  border-radius: 3px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  direction: ltr;
  text-align: left;
}

.excluded-item:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.excluded-item.selected {
  background: color-mix(in srgb, var(--accent-color) 22%, transparent);
}

.excluded-error {
  margin: 0;
  font-size: 11px;
  color: var(--status-danger);
}

.excluded-actions {
  display: flex;
  /* row-reverse in the RTL dialog = physical left-to-right DOM order. This reproduces
     the WinForms ExcludedFoldersForm footer, whose RightToLeftLayout mirroring renders
     אישור at the far physical LEFT (ביטול to its right) and הסר at the far physical
     RIGHT (הוסף תיקייה to its left) — same convention as ConfirmDialog's action row. */
  flex-direction: row-reverse;
  align-items: center;
  gap: 8px;
  padding-top: 10px;
  border-top: 1px solid var(--border-color);
}

.excluded-actions-spacer {
  flex: 1;
}

.excluded-actions button {
  height: 30px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 0 14px;
  font-size: 12px;
  border-radius: 4px;
}

/* theme.css pins `svg { color: var(--text-secondary) }`, which outranks plain
   inheritance and would render every footer icon muted grey regardless of its
   button. Re-point the icons at the button's own colour. */
.excluded-actions button svg {
  width: 16px;
  height: 16px;
  flex: none;
  color: inherit;
}

.excluded-actions button:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

/* The dialog's single accent-filled default. הוסף תיקייה is a neutral secondary so the
   footer has exactly one primary — two filled buttons left it with no visual default. */
.excluded-ok-button {
  border: 1px solid transparent;
  background: var(--accent-color);
  color: #fff;
}
.excluded-ok-button:hover:not(:disabled) {
  background: color-mix(in srgb, var(--accent-color) 85%, #000);
}

.excluded-cancel-button,
.excluded-add-button {
  border: 1px solid var(--border-color);
  background: var(--control-bg);
  color: var(--text-primary);
}
.excluded-cancel-button:hover,
.excluded-add-button:hover {
  background: var(--control-bg-hover);
}

.excluded-remove-button {
  border: 1px solid color-mix(in srgb, var(--status-danger) 40%, transparent);
  background: color-mix(in srgb, var(--status-danger) 8%, transparent);
  color: var(--status-danger);
}
.excluded-remove-button:hover:not(:disabled) {
  background: color-mix(in srgb, var(--status-danger) 16%, transparent);
}
</style>
