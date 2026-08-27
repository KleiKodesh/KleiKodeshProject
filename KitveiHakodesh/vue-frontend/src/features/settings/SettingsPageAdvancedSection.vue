<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import SettingRow from './SettingRow.vue'
import SettingsPagePathField from './SettingsPagePathField.vue'
import SettingsExcludedFoldersDialog from './SettingsExcludedFoldersDialog.vue'
import ToggleGroup from './ToggleGroup.vue'
import { onDbReady } from '@/webview-host/seforimDb'
import { clearStaleBookData } from '@/webview-host/dbSwitchCleanup'
import { useSettingsStore } from '@/stores/settingsStore'
import {
  pickFolder,
  clearDbPath,
  clearHbLocalFolder,
  openExcludedFoldersManager,
  getTurnOffUpdates,
  setTurnOffUpdates,
  getDbPathInfo,
  setDbPathDev,
  pickDbPathDev,
} from '@/webview-host/bridge'

// ── Database path ─────────────────────────────────────────────────────────────
// Hosted: the C# host owns the setting (native file picker + __webviewSetDbPath).
// Dev: the KitveiHakodesh service owns it — same registry value, so both modes
// read/write one setting. In dev the path is typed into the editable field (no
// native dialog in a browser); the service validates, persists and restarts.

const isDev = typeof window.__webviewAction !== 'function'
const dbPath = ref(window.__webviewDbPath ?? '')

onMounted(async () => {
  if (!dbPath.value) {
    const info = await getDbPathInfo()
    if (info) dbPath.value = info.path
  }
})

async function pickDbPath() {
  if (isDev) {
    // The service shows the native dialog and persists the choice, then restarts on the
    // new DB. Reload so every store refetches from it; /khs waits for the respawn.
    const picked = await pickDbPathDev()
    if (!picked) return
    const previous = dbPath.value
    dbPath.value = picked
    await forgetStaleBookData(previous, picked)
    setTimeout(() => window.location.reload(), 800)
    return
  }
  window.__webviewPickDbPath?.()
}

/**
 * Drop the book-id-keyed state (recent books, last-read, tabs, caches) when the dev
 * routes below change library. Hosted mode gets this inside `onDbReady`; in dev the
 * service owns the path and the page reloads directly, so the wipe is called here —
 * always BEFORE the reload, or a store would write its stale copy back out.
 *
 * Same-path and first-time cases are skipped, so setting up the library for the first
 * time or re-picking the same folder keeps the reader's history.
 */
async function forgetStaleBookData(previousPath: string, newPath: string) {
  if (!previousPath || previousPath === newPath) return
  await clearStaleBookData()
}

async function commitDbPath(newPath: string) {
  if (isDev) {
    const prev = dbPath.value
    try {
      await setDbPathDev(newPath)
      dbPath.value = newPath
      await forgetStaleBookData(prev, newPath)
      // The service restarts on the new DB (and rebuilds a stale FTS index).
      // Reload so every store refetches from it; /khs waits for the respawn.
      setTimeout(() => window.location.reload(), 800)
    } catch (err) {
      console.error('[settings] setDbPathDev failed:', err)
      dbPath.value = prev
    }
    return
  }
  if (!window.__webviewSetDbPath) return
  try {
    await window.__webviewSetDbPath(newPath)
    dbPath.value = newPath
    await onDbReady(newPath)
  } catch (err) {
    // The rejection carries the host's FULL exception (type + stack). This catch
    // used to swallow it, so the field silently snapping back to the injected
    // path was all a user ever saw of the failure — log it for debugging.
    console.error('[settings] setDbPath failed:', err)
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
  const previous = dbPath.value
  const defaultPath = await clearDbPath()
  if (defaultPath !== null) {
    dbPath.value = defaultPath
    if (isDev) {
      await forgetStaleBookData(previous, defaultPath)
      // Service restarted on the default DB — reload so stores refetch.
      setTimeout(() => window.location.reload(), 800)
      return
    }
    // Hosted: onDbReady does the same comparison against the path being left, so
    // clearing back to the default drops the stale state exactly as a pick does.
    await onDbReady(defaultPath)
  }
}

// ── Excluded folders ──────────────────────────────────────────────────────────
// Hosted: the C# host shows the native WinForms manager, which owns its own persistence.
// Dev: the Vue dialog below mirrors it and persists through the service to an
// excluded_folders.json beside the file-search index directory — same format, but the
// dev service resolves that directory from its own bin folder, not the install folder.

const isExcludedFoldersDialogOpen = ref(false)

async function openExcludedFolders() {
  if (isDev) {
    isExcludedFoldersDialogOpen.value = true
    return
  }
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

    <SettingRow
      label="הגדר תיקיית ספרים מקומית של היברו בוקס"
      hint="אם ברשותך אוסף מקומי של ספרים מהיברו בוקס, ציין את נתיב התיקייה. הספרים יטענו מהתיקייה במקום להוריד מהאינטרנט. אם ספר אינו נמצא בתיקייה, תתבצע הורדה רגילה."
    >
      <SettingsPagePathField
        :value="hebrewBooksLocalFolder"
        placeholder="לא נבחרה תיקייה"
        :clearable="true"
        :editable="true"
        @pick="pickHebrewBooksFolder"
        @clear="resetHebrewBooksFolder"
        @commit="commitHebrewBooksFolder"
      />
    </SettingRow>

    <SettingRow
      label="הגדר נתיב למסד הנתונים"
      :hint="isDev ? 'שינוי הנתיב יפעיל מחדש את שירות הנתונים ויבנה מחדש את אינדקס החיפוש אם צריך.' : undefined"
    >
      <SettingsPagePathField
        :value="dbPath"
        placeholder="לא נבחר נתיב"
        :clearable="true"
        :editable="true"
        @pick="pickDbPath"
        @clear="resetDbPath"
        @commit="commitDbPath"
      />
    </SettingRow>

    <SettingRow
      label="החרג תיקיות מחיפוש הקבצים"
      hint="תיקיות שיוחרגו מתוצאות חיפוש הקבצים. השינויים נכנסים לתוקף מיד — אין צורך לבנות מחדש את האינדקס."
    >
      <button class="manage-btn" @click="openExcludedFolders">ניהול תיקיות מוחרגות</button>
    </SettingRow>

    <SettingRow
      id="nav-turn-off-updates"
      data-nav-label="כבה בדיקת עדכונים"
      label="כבה בדיקת עדכונים אוטומטית"
      hint="בבחירת 'כן' האפליקציה לא תבדוק אם קיים עדכון בעת הפתיחה. ההגדרה משותפת עם התוסף לוורד. עדכון שכבר הורד יותקן עם סגירת האפליקציה."
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

    <SettingsExcludedFoldersDialog
      v-if="isExcludedFoldersDialogOpen"
      @close="isExcludedFoldersDialogOpen = false"
    />
  </div>
</template>

<style scoped>
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
