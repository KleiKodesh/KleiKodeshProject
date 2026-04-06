<script setup lang="ts">
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { storeToRefs } from 'pinia'
import SettingRow from './SettingRow.vue'
import SliderSetting from './SliderSetting.vue'
import ToggleGroup from './ToggleGroup.vue'
import ThemePicker from './ThemePicker.vue'

const settings = useSettingsStore()
const { censorDivineNames, appZoom, newTabPage, resumeLastRead } = storeToRefs(settings)

const bookViewStore = useBookViewStore()
const { toolbarPosition } = storeToRefs(bookViewStore)
</script>

<template>
  <div class="general-pane">
    <SettingRow label="ערכת נושא">
      <ThemePicker />
    </SettingRow>

    <SettingRow label="מיקום סרגל הכלים" wrap>
      <ToggleGroup
        v-model="toolbarPosition"
        :options="[
          { label: 'למעלה', value: 'top' },
          { label: 'למטה', value: 'bottom' },
          { label: 'שמאל', value: 'left' },
          { label: 'ימין', value: 'right' },
        ]"
        @update:model-value="bookViewStore.setToolbarPosition($event)"
      />
    </SettingRow>

    <SliderSetting label="גודל תצוגה" v-model="appZoom" :min="0.5" :max="1.5" :step="0.05" />

    <SettingRow label="כיסוי שם ה'">
      <ToggleGroup
        v-model="censorDivineNames"
        :options="[
          { label: 'כתיב מלא', value: false },
          { label: 'כיסוי (ה←ק)', value: true },
        ]"
      />
    </SettingRow>

    <SettingRow label="פתח טאב חדש אל" wrap>
      <ToggleGroup
        v-model="newTabPage"
        :options="[
          { label: 'דף הבית', value: 'homepage' },
          { label: 'פתיחת ספר', value: 'openfile' },
          { label: 'היברו בוקס', value: 'hebrewbooks' },
          { label: 'חיפוש', value: 'kezayit-search' },
        ]"
      />
    </SettingRow>

    <SettingRow
      label="זכור מיקום אחרון בספר"
      title="בפתיחת ספר מחדש, האפליקציה תחזור אוטומטית למקום שבו הפסקת לקרוא"
    >
      <ToggleGroup
        v-model="resumeLastRead"
        :options="[
          { label: 'כן', value: true },
          { label: 'לא', value: false },
        ]"
      />
    </SettingRow>
  </div>
</template>

<style scoped>
.general-pane {
  display: contents;
}
</style>
