<script setup lang="ts">
import { ref, nextTick, onMounted } from 'vue'
import {
  IconFolderOpen20Regular,
  IconChevronDown20Regular,
  IconChevronUp20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useSettings } from './useSettings'
import ConfirmDialog from '@/components/common/ConfirmDialog.vue'
import SettingRow from './SettingRow.vue'
import { resetting } from '@/utils/appResetState'
import { isHosted, onDbReady } from '@/host/seforimDb'
import { useZmanim, CITIES } from '@/components/hebrew-calendar/useZmanim'

const { resetSettings, resetSearchIndex, resetAll } = useSettings()

const dbPath = ref(window.__webviewDbPath ?? '')
const editingPath = ref(false)
const pathInputRef = ref<HTMLInputElement | null>(null)

function pickDbPath() {
  window.__webviewPickDbPath?.()
}

function startEditing() {
  editingPath.value = true
  nextTick(() => pathInputRef.value?.focus())
}

async function commitPath() {
  editingPath.value = false
  if (!window.__webviewSetDbPath) return
  try {
    await window.__webviewSetDbPath(dbPath.value)
    onDbReady(dbPath.value)
  } catch {
    dbPath.value = window.__webviewDbPath ?? ''
  }
}

type ConfirmAction = { label: string; desc: string; action: () => Promise<void> | void }
const pendingConfirm = ref<ConfirmAction | null>(null)

function confirmAction(action: ConfirmAction) {
  pendingConfirm.value = action
}

async function runConfirmed() {
  if (!pendingConfirm.value) return
  const action = pendingConfirm.value
  pendingConfirm.value = null
  await action.action()
}

function cancelConfirm() {
  pendingConfirm.value = null
}

async function resetSettingsAndReload() {
  resetting.value = true
  await resetSettings()
  window.location.reload()
}

function confirmResetAll() {
  confirmAction({
    label: 'איפוס האפליקציה',
    desc: 'פעולה זו תמחק את כל נתוני האפליקציה ואינדקס החיפוש ותטען אותה מחדש. לא ניתן לבטל פעולה זו.',
    action: async () => {
      resetting.value = true
      await resetAll()
    },
  })
}

function confirmResetSettings() {
  confirmAction({
    label: 'איפוס ההגדרות',
    desc: 'פעולה זו תאפס את הגדרות התצוגה והקריאה לברירות המחדל. מסד הנתונים והיסטוריית הקריאה לא יושפעו.',
    action: resetSettingsAndReload,
  })
}
function confirmResetSearchIndex() {
  confirmAction({
    label: 'איפוס אינדקס החיפוש',
    desc: 'פעולה זו תמחק את אינדקס החיפוש ומטמון תוצאות החיפוש ותבנה את האינדקס מחדש. שאר נתוני האפליקציה לא יושפעו.',
    action: resetSearchIndex,
  })
}
const { activeCity, setCity, init: initZmanim } = useZmanim()
onMounted(() => initZmanim())

const cityBoxRef = ref<HTMLElement | null>(null)
const cityDropdownRef = ref<HTMLElement | null>(null)
const cityOpen = ref(false)
const cityDropdownStyle = ref<Record<string, string>>({})

useDropdownClose(
  cityDropdownRef,
  (e) => {
    if (cityBoxRef.value?.contains((e as MouseEvent).target as Node)) return
    cityOpen.value = false
  },
  { ignore: [cityBoxRef] },
)

async function toggleCityDropdown() {
  if (cityOpen.value) {
    cityOpen.value = false
    return
  }
  cityOpen.value = true
  await nextTick()
  if (!cityBoxRef.value || !cityDropdownRef.value) return
  const rect = cityBoxRef.value.getBoundingClientRect()
  const spaceBelow = window.innerHeight - rect.bottom - 8
  const spaceAbove = rect.top - 8
  const goUp = spaceAbove > spaceBelow
  const maxH = Math.min(240, goUp ? spaceAbove : spaceBelow)
  cityDropdownRef.value.style.maxHeight = maxH + 'px'
  cityDropdownStyle.value = {
    position: 'fixed',
    left: rect.left + 'px',
    width: rect.width + 'px',
    zIndex: '10000',
    ...(goUp
      ? { bottom: window.innerHeight - rect.top + 4 + 'px', top: 'auto' }
      : { top: rect.bottom + 4 + 'px', bottom: 'auto' }),
  }
}

function pickCity(name: string) {
  setCity(CITIES.find((c) => c.name === name) ?? null)
  cityOpen.value = false
}
</script>

<template>
  <div class="advanced-pane">
    <div class="section-label">לוח שנה</div>

    <SettingRow label="עיר לזמני היום" hint="העיר שלפיה יחושבו זמני היום בלוח השנה">
      <div ref="cityBoxRef" class="select-box" tabindex="0" @click="toggleCityDropdown">
        <span class="select-display">{{ activeCity.name }}</span>
        <component
          :is="cityOpen ? IconChevronUp20Regular : IconChevronDown20Regular"
          class="select-chevron"
        />
      </div>
      <Teleport to="body">
        <div
          v-if="cityOpen"
          ref="cityDropdownRef"
          class="city-dropdown"
          :style="cityDropdownStyle"
          @click.stop
        >
          <div
            v-for="c in CITIES"
            :key="c.name"
            class="city-option"
            :class="{ selected: activeCity.name === c.name }"
            @click="pickCity(c.name)"
          >
            {{ c.name }}
          </div>
        </div>
      </Teleport>
    </SettingRow>

    <div class="section-label">מסד נתונים</div>

    <template v-if="isHosted">
      <div class="db-path-row">
        <span class="db-path-label">נתיב מסד הנתונים</span>
        <div class="db-path-field" :class="{ editing: editingPath }">
          <button class="folder-btn" @click="pickDbPath" title="בחר קובץ">
            <IconFolderOpen20Regular />
          </button>
          <input
            v-if="editingPath"
            ref="pathInputRef"
            v-model="dbPath"
            name="db-path"
            class="db-path-input"
            dir="ltr"
            @blur="commitPath"
            @keydown.enter="commitPath"
            @keydown.escape="editingPath = false"
          />
          <span v-else class="db-path-text" :class="{ placeholder: !dbPath }" @click="startEditing">
            {{ dbPath || 'לא נבחר נתיב' }}
          </span>
        </div>
      </div>
    </template>

    <div class="section-label">איפוס</div>

    <p class="reset-desc">
      מאפס רק את הגדרות התצוגה והקריאה לברירות המחדל. מסד הנתונים, היסטוריית הקריאה, והטאבים הפתוחים
      נשמרים.
    </p>
    <button class="reset-all-btn" @click="confirmResetSettings">איפוס ההגדרות</button>
    <p class="reset-desc">
      מוחק את אינדקס החיפוש ובונה אותו מחדש. שאר נתוני האפליקציה לא יושפעו.
    </p>
    <button class="reset-all-btn" @click="confirmResetSearchIndex">איפוס אינדקס החיפוש</button>
    <p class="reset-desc">
      מוחק את כל נתוני האפליקציה — הגדרות, היסטוריית קריאה, מיקומי גלילה, טאבים פתוחים, ואינדקס
      החיפוש. לא ניתן לבטל פעולה זו.
    </p>
    <button class="reset-all-btn" @click="confirmResetAll">איפוס האפליקציה</button>

    <ConfirmDialog
      v-if="pendingConfirm"
      :title="pendingConfirm.label"
      :desc="pendingConfirm.desc"
      @confirm="runConfirmed"
      @cancel="cancelConfirm"
    />
  </div>
</template>

<style scoped>
.advanced-pane {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  width: 100%;
}

.reset-desc {
  font-size: 12px;
  color: var(--text-secondary);
  line-height: 1.5;
  margin: 0 0 8px;
}
.reset-all-btn {
  width: 140px;
  align-self: flex-start;
  height: 32px;
  font-size: 13px;
  color: #e53e3e;
  border: 1px solid color-mix(in srgb, #e53e3e 40%, transparent);
  background: color-mix(in srgb, #e53e3e 8%, transparent);
  margin-bottom: 12px;
}
.reset-all-btn:hover {
  background: color-mix(in srgb, #e53e3e 16%, transparent);
}

.db-path-row {
  display: flex;
  flex-direction: column;
  gap: 6px;
  width: 100%;
  margin-bottom: 10px;
}
.db-path-label {
  font-size: 11px;
  color: var(--text-secondary);
}
.db-path-field {
  display: flex;
  align-items: center;
  height: 32px;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-secondary);
  overflow: hidden;
  transition: border-color 0.1s;
}
.db-path-field:hover {
  border-color: color-mix(in srgb, var(--text-secondary) 50%, transparent);
}
.db-path-field.editing {
  border-color: var(--accent-color);
}
.db-path-text {
  flex: 1;
  padding: 0 8px;
  font-size: 11px;
  color: var(--text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  direction: ltr;
  text-align: left;
  cursor: text;
  min-width: 0;
}
.db-path-text:hover {
  color: var(--text-primary);
}
.db-path-text.placeholder {
  direction: rtl;
  text-align: right;
  opacity: 0.6;
}
.db-path-input {
  flex: 1;
  height: 100%;
  padding: 0 8px;
  font-size: 11px;
  direction: ltr;
  text-align: left;
  background: transparent;
  border: none;
  outline: none;
  color: var(--text-primary);
  min-width: 0;
}
.folder-btn {
  flex-shrink: 0;
  width: 32px;
  height: 32px;
  padding: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  border-inline-end: 1px solid var(--border-color);
  border-radius: 0;
  background: transparent;
  color: var(--text-secondary);
}
.folder-btn:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}

.section-label {
  font-size: 12px;
  font-weight: 700;
  color: var(--text-primary);
  padding: 4px 0;
  margin-bottom: 10px;
  border-bottom: 1px solid var(--border-color);
  width: 100%;
}
.section-label:not(:first-child) {
  margin-top: 16px;
}

.select-box {
  display: flex;
  align-items: center;
  width: 100%;
  height: 28px;
  padding: 0 8px;
  cursor: pointer;
  user-select: none;
  box-sizing: border-box;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
}
.select-box:hover {
  border-color: var(--accent-color);
}
.select-display {
  flex: 1;
  font-size: 12px;
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.select-chevron {
  color: var(--text-secondary);
  flex-shrink: 0;
}
</style>

<style>
.city-dropdown {
  overflow-y: auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  direction: rtl;
}
.city-option {
  display: flex;
  align-items: center;
  padding: 0 10px;
  height: 32px;
  cursor: pointer;
  font-size: 13px;
  color: var(--text-primary);
}
.city-option:hover {
  background: var(--hover-bg);
}
.city-option.selected {
  background: var(--accent-bg);
  color: var(--accent-color);
  font-weight: 500;
}
</style>
