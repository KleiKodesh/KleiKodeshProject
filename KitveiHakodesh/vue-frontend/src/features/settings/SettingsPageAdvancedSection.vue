<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import SettingRow from './SettingRow.vue'
import SettingsPagePathField from './SettingsPagePathField.vue'
import ToggleGroup from './ToggleGroup.vue'
import { isHosted, onDbReady } from '@/webview-host/seforimDb'
import { useSettingsStore } from '@/stores/settingsStore'
import {
  pickFolder,
  clearDbPath,
  clearHbLocalFolder,
  openExcludedFoldersManager,
  getTurnOffUpdates,
  setTurnOffUpdates,
} from '@/webview-host/bridge'

// ── Database path ─────────────────────────────────────────────────────────────

const dbPath = ref(window.__webviewDbPath ?? '')

function pickDbPath() {
  window.__webviewPickDbPath?.()
}

async function commitDbPath(newPath: string) {
  if (!window.__webviewSetDbPath) return
  try {
    await window.__webviewSetDbPath(newPath)
    dbPath.value = newPath
    onDbReady(newPath)
  } catch {
    dbPath.value = window.__webviewDbPath ?? ''
  }
}

// ── HebrewBooks local folder ──────────────────────────────────────────────────

const { hebrewBooksLocalFolder } = storeToRefs(useSettingsStore())

async function pickHebrewBooksFolder() {
  const result = await pickFolder()
  if (result) hebrewBooksLocalFolder.value = result
}

function commitHebrewBooksFolder(newPath: string) {
  hebrewBooksLocalFolder.value = newPath
}

async function resetHebrewBooksFolder() {
  await clearHbLocalFolder()
  hebrewBooksLocalFolder.value = ''
}

// ── Database path clear ───────────────────────────────────────────────────────

async function resetDbPath() {
  const defaultPath = await clearDbPath()
  if (defaultPath !== null) {
    dbPath.value = defaultPath
    onDbReady(defaultPath)
  }
}

// ── Excluded folders ──────────────────────────────────────────────────────────

async function openExcludedFolders() {
  await openExcludedFoldersManager()
}

// ── Automatic updates ─────────────────────────────────────────────────────────
// Backed by the shared VSTO registry key (KleiKodesh\UpdateChecker\TurnOffUpdates),
// read/written directly via the host bridge — not the settings store (which persists
// to localStorage). true = the automatic update check is turned OFF.

const turnOffUpdates = ref(false)

onMounted(async () => {
  const value = await getTurnOffUpdates()
  if (value !== null) turnOffUpdates.value = value
})

async function applyTurnOffUpdates(value: boolean) {
  turnOffUpdates.value = value
  await setTurnOffUpdates(value)
}
</script>

<template>
  <!-- ── מתקדם ── -->
  <div data-section="section-advanced" data-section-label="מתקדם">
    <div id="section-advanced" class="section-label">מתקדם</div>

    <!-- היברו בוקס -->
    <div class="subsection-label">היברו בוקס</div>
    <SettingRow
      label="תיקיית ספרים מקומית"
      hint="אם ברשותך אוסף מקומי של ספרים מהיברו בוקס, ציין את נתיב התיקייה. הספרים יטענו מהתיקייה במקום להוריד מהאינטרנט. אם ספר אינו נמצא בתיקייה, תתבצע הורדה רגילה."
    >
      <SettingsPagePathField
        :value="hebrewBooksLocalFolder"
        placeholder="לא נבחרה תיקייה"
        :clearable="true"
        :editable="true"
        :disabled="!isHosted"
        @pick="pickHebrewBooksFolder"
        @clear="resetHebrewBooksFolder"
        @commit="commitHebrewBooksFolder"
      />
    </SettingRow>
    <p v-if="!isHosted" class="hint-text">זמין רק בתוך האפליקציה המארחת</p>

    <!-- מסד נתונים -->
    <div class="subsection-label">מסד נתונים</div>
    <template v-if="isHosted">
      <div class="db-path-row">
        <span class="db-path-label">נתיב מסד הנתונים</span>
        <SettingsPagePathField
          :value="dbPath"
          placeholder="לא נבחר נתיב"
          :clearable="true"
          :editable="true"
          @pick="pickDbPath"
          @clear="resetDbPath"
          @commit="commitDbPath"
        />
      </div>
    </template>
    <p v-else class="hint-text">זמין רק בתוך האפליקציה המארחת</p>

    <!-- חיפוש קבצים -->
    <div class="subsection-label">חיפוש קבצים</div>
    <SettingRow
      label="תיקיות מוחרגות"
      hint="תיקיות שיוחרגו מתוצאות חיפוש הקבצים. השינויים נכנסים לתוקף מיד — אין צורך לבנות מחדש את האינדקס."
    >
      <button
        class="manage-btn"
        :disabled="!isHosted"
        @click="openExcludedFolders"
      >
        ניהול תיקיות מוחרגות
      </button>
    </SettingRow>
    <p v-if="!isHosted" class="hint-text">זמין רק בתוך האפליקציה המארחת</p>

    <!-- עדכונים -->
    <div class="subsection-label">עדכונים</div>
    <SettingRow
      id="nav-turn-off-updates"
      data-nav-label="כבה בדיקת עדכונים"
      label="כבה בדיקת עדכונים אוטומטית"
      hint="כאשר מופעל, האפליקציה לא תבדוק אוטומטית אם קיים עדכון בעת הפתיחה. הגדרה זו משותפת עם התוסף של וורד. עדכון שכבר הורד עדיין יותקן עם סגירת האפליקציה."
    >
      <ToggleGroup
        :model-value="turnOffUpdates"
        :options="[
          { label: 'כן', value: true },
          { label: 'לא', value: false },
        ]"
        @update:model-value="applyTurnOffUpdates"
      />
    </SettingRow>
    <p v-if="!isHosted" class="hint-text">זמין רק בתוך האפליקציה המארחת</p>
  </div>
</template>

<style scoped>
.hint-text {
  font-size: 11px;
  color: var(--text-secondary);
  margin: -4px 0 10px;
}

.db-path-row {
  display: flex;
  flex-direction: column;
  gap: 6px;
  width: 100%;
  margin-bottom: 10px;
  box-sizing: border-box;
}

.db-path-label {
  font-size: 11px;
  color: var(--text-secondary);
}

.manage-btn {
  width: 100%;
  height: 28px;
  padding: 0 10px;
  font-size: 12px;
  border: 1px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  border-radius: 4px;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

.manage-btn:hover:not(:disabled) {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.manage-btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}
</style>
