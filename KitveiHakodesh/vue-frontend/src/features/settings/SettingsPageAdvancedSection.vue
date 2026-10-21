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
  const [offer, info] = await Promise.all([getServiceInstallOffer(), getDbPathInfo()])
  serviceInstallOffer.value = offer
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
  <div data-section="section-advanced" :data-section-label="$t('settings.advanced.section')">
    <div id="section-advanced" class="section-label">{{ $t('settings.advanced.section') }}</div>

    <SettingRow
      v-if="serviceInstallOffer"
      :label="$t('settings.advanced.service')"
      :hint="$t('settings.advanced.serviceHint')"
    >
      <button class="settings-btn" :disabled="installing" @click="runInstallService">
        {{ installing ? $t('settings.advanced.installing') : $t('settings.advanced.install') }}
      </button>
    </SettingRow>

    <SettingRow
      :label="$t('settings.advanced.hbFolder')"
      :hint="$t('settings.advanced.hbFolderHint')"
    >
      <SettingsPagePathField
        :value="hebrewBooksLocalFolder"
        :placeholder="$t('settings.advanced.noFolder')"
        :clearable="true"
        :editable="true"
        @pick="pickHebrewBooksFolder"
        @clear="resetHebrewBooksFolder"
        @commit="commitHebrewBooksFolder"
      />
    </SettingRow>

    <SettingRow
      :label="$t('settings.advanced.dbPath')"
      :hint="$t('settings.advanced.dbPathHint')"
    >
      <SettingsPagePathField
        :value="dbPath"
        :placeholder="$t('settings.advanced.noPath')"
        :clearable="true"
        :editable="true"
        @pick="pickDbPath"
        @clear="resetDbPath"
        @commit="commitDbPath"
      />
    </SettingRow>

    <SettingRow
      :label="$t('settings.advanced.excluded')"
      :hint="$t('settings.advanced.excludedHint')"
    >
      <button class="settings-btn" @click="openExcludedFolders">{{ $t('settings.advanced.manageExcluded') }}</button>
    </SettingRow>

    <SettingsExcludedFoldersDialog
      v-if="isExcludedFoldersDialogOpen"
      @close="isExcludedFoldersDialogOpen = false"
    />
  </div>
</template>

<style scoped>
</style>
