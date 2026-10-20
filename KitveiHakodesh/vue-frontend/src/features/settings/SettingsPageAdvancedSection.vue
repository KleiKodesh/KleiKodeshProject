<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import SettingRow from './SettingRow.vue'
import SettingsPagePathField from './SettingsPagePathField.vue'
import SettingsExcludedFoldersDialog from './SettingsExcludedFoldersDialog.vue'
import { onDbPathChanged } from '@/webview-host/seforimDb'
import { useSettingsStore } from '@/stores/settingsStore'
import {
  pickFolder,
  clearDbPath,
  clearHbLocalFolder,
  getDbPathInfo,
  setDbPath,
  pickDbPath as pickDbPathWithDialog,
  getServiceInstallOffer,
  installService,
} from '@/webview-host/bridge'

// ── Service install ───────────────────────────────────────────────────────────
// Offered only when the host runs without the Windows service (file search is off).
// The install raises the admin prompt once; on success the page reloads onto the service.

const serviceInstallOffer = ref(false)
const installing = ref(false)

async function runInstallService() {
  installing.value = true
  try {
    if (await installService()) location.reload()
  } finally {
    installing.value = false
  }
}

// ── Database path ─────────────────────────────────────────────────────────────
// The KitveiHakodesh service owns the setting: it validates, persists (one registry
// value for every runtime) and restarts on the new DB. The page then reloads.

const dbPath = ref('')

onMounted(async () => {
  serviceInstallOffer.value = await getServiceInstallOffer()
  const info = await getDbPathInfo()
  if (info) dbPath.value = info.path
})

async function pickDbPath() {
  const picked = await pickDbPathWithDialog()
  if (!picked) return
  const previous = dbPath.value
  dbPath.value = picked
  await onDbPathChanged(previous, picked)
}

async function commitDbPath(newPath: string) {
  const prev = dbPath.value
  try {
    await setDbPath(newPath)
    dbPath.value = newPath
    await onDbPathChanged(prev, newPath)
  } catch (err) {
    console.error('[settings] setDbPath failed:', err)
    dbPath.value = prev
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
    await onDbPathChanged(previous, defaultPath)
  }
}

// ── Excluded folders ──────────────────────────────────────────────────────────
// One Vue dialog in both runtimes; the service persists the list to excluded_folders.json
// beside its file-search index and applies it at search time.

const isExcludedFoldersDialogOpen = ref(false)

function openExcludedFolders() {
  isExcludedFoldersDialogOpen.value = true
}
</script>

<template>
  <!-- ── מתקדם ── -->
  <div data-section="section-advanced" data-section-label="מתקדם">
    <div id="section-advanced" class="section-label">מתקדם</div>

    <SettingRow
      v-if="serviceInstallOffer"
      label="שירות Windows (מומלץ)"
      hint="מפעיל את הספרייה ברקע כשירות Windows: הפעלה מהירה יותר, אינדקס חיפוש קבצים במסמכי המחשב, והשירות נשאר מוכן בין וורד לתוכנה. ההתקנה תבקש אישור מנהל פעם אחת."
    >
      <button class="manage-btn" :disabled="installing" @click="runInstallService">
        {{ installing ? 'מתקין...' : 'התקן שירות' }}
      </button>
    </SettingRow>

    <SettingRow
      label="תיקייה לשמירת ספרי היברו בוקס לצמיתות במחשב (רשות)"
      hint="כברירת מחדל, ספרים שהורדו נשמרים במטמון זמני של 10 הספרים האחרונים. בחר תיקייה כדי לשמור לצמיתות כל ספר שהורד; התיקייה תיווצר אם אינה קיימת. אפשר לבחור בתיקיית מאגר הספרים של תוכנת ארון הספרים הדיגיטלי."
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
      hint="שינוי הנתיב יפעיל מחדש את שירות הנתונים ויבנה מחדש את אינדקס החיפוש אם צריך."
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
