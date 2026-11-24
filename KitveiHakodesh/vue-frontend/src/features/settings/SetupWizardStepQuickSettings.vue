<script setup lang="ts">
import { onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { pickFolder, clearHbLocalFolder } from '@/webview-host/bridge'
import { useZmanim } from '@/features/hebrew-calendar/useZmanim'
import PlaceTreePicker from '@/features/hebrew-calendar/PlaceTreePicker.vue'
import CommunityPicker from '@/features/hebrew-calendar/CommunityPicker.vue'
import SettingRow from './SettingRow.vue'
import SettingsPagePathField from './SettingsPagePathField.vue'
import ToggleGroup from './ToggleGroup.vue'

const settings = useSettingsStore()
const { resumeLastRead, hebrewBooksLocalFolder } = storeToRefs(settings)

// Same wiring as the settings page (SettingsPageAdvancedSection).
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

const { activeCity, setCity, init: initZmanim } = useZmanim()

onMounted(() => {
  initZmanim()
})
</script>

<template>
  <SettingRow
    :label="$t('settings.book.remember')"
    :title="$t('settings.book.rememberHint')"
  >
    <ToggleGroup
      v-model="resumeLastRead"
      :options="[
        { label: $t('settings.yes'), value: true },
        { label: $t('settings.no'), value: false },
      ]"
    />
  </SettingRow>
  <SettingRow :label="$t('settings.calendar.city')" :hint="$t('settings.calendar.cityHintWizard')">
    <PlaceTreePicker :place-id="activeCity?.id" @pick="setCity" />
  </SettingRow>
  <SettingRow :label="$t('settings.calendar.community')" :hint="$t('settings.calendar.communityHint')">
    <CommunityPicker />
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
</template>

