<script setup lang="ts">
import { ref } from 'vue'
import { storeToRefs } from 'pinia'
import SettingRow from './SettingRow.vue'
import SettingsPagePathField from './SettingsPagePathField.vue'
import { isHosted, onDbReady } from '@/webview-host/seforimDb'
import { useSettingsStore } from '@/stores/settingsStore'
import { pickFolder } from '@/webview-host/bridge'

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
</script>

<template>
  <!-- ── היברו בוקס ── -->
  <div data-section="section-hebrewbooks" data-section-label="היברו בוקס">
    <div id="section-hebrewbooks" class="section-label">היברו בוקס</div>

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
        @clear="hebrewBooksLocalFolder = ''"
        @commit="commitHebrewBooksFolder"
      />
    </SettingRow>
    <p v-if="!isHosted" class="hint-text">זמין רק בתוך האפליקציה המארחת</p>
  </div>

  <!-- ── מסד נתונים ── -->
  <div data-section="section-database" data-section-label="מסד נתונים">
    <div id="section-database" class="section-label">מסד נתונים</div>

    <template v-if="isHosted">
      <div class="db-path-row">
        <span class="db-path-label">נתיב מסד הנתונים</span>
        <SettingsPagePathField
          :value="dbPath"
          placeholder="לא נבחר נתיב"
          :editable="true"
          @pick="pickDbPath"
          @commit="commitDbPath"
        />
      </div>
    </template>
    <p v-else class="db-path-label">זמין רק בתוך האפליקציה המארחת</p>
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
}

.db-path-label {
  font-size: 11px;
  color: var(--text-secondary);
}
</style>
