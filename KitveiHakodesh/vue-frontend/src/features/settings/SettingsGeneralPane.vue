<script setup lang="ts">
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { storeToRefs } from 'pinia'
import SettingRow from './SettingRow.vue'
import SliderSetting from './SliderSetting.vue'
import ToggleGroup from './ToggleGroup.vue'
import ThemePicker from './ThemePicker.vue'

const settings = useSettingsStore()
const { censorDivineNames, appZoom, newTabPage, resumeLastRead, defaultAutoSyncCommentary } =
  storeToRefs(settings)

const bookViewStore = useBookViewStore()
const { toolbarPosition } = storeToRefs(bookViewStore)
</script>

<template>
  <div class="general-pane">
    <div class="section-label">אפליקציה</div>

    <SettingRow label="ערכת נושא" hint="צבעי הממשק של האפליקציה">
      <ThemePicker />
    </SettingRow>

    <SliderSetting
      label="גודל תצוגה"
      v-model="appZoom"
      :min="0.5"
      :max="1.5"
      :step="0.05"
      hint="משנה את גודל כל ממשק האפליקציה"
    />

    <div class="section-label">ניווט</div>

    <SettingRow label="מיקום סרגל הכלים בתצוגת ספר" wrap>
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

    <SettingRow label="פתח טאב חדש אל" hint="הדף שיפתח בלחיצה על טאב חדש" wrap>
      <ToggleGroup
        v-model="newTabPage"
        :options="[
          { label: 'דף הבית', value: 'homepage' },
          { label: 'פתיחת ספר', value: 'openfile' },
          { label: 'היברו בוקס', value: 'hebrewbooks' },
          { label: 'חיפוש', value: 'search' },
        ]"
      />
    </SettingRow>

    <div class="section-label">קריאה</div>

    <SettingRow
      label="זכור מיקום אחרון בספר"
      hint="בפתיחת ספר מחדש, האפליקציה תחזור אוטומטית למקום שבו הפסקת לקרוא"
    >
      <ToggleGroup
        v-model="resumeLastRead"
        :options="[
          { label: 'כן', value: true },
          { label: 'לא', value: false },
        ]"
      />
    </SettingRow>

    <SettingRow
      label="סנכרן מפרשים כברירת מחדל"
      hint="ניתן לשנות לכל ספר בנפרד דרך כפתור סנכרן מפרשים בסרגל הכלים"
    >
      <ToggleGroup
        v-model="defaultAutoSyncCommentary"
        :options="[
          { label: 'כן', value: true },
          { label: 'לא', value: false },
        ]"
      />
    </SettingRow>

    <SettingRow label="כיסוי שם ה'" hint="מחליף את האות ה׳ בשמות הקודש באות ק׳">
      <ToggleGroup
        v-model="censorDivineNames"
        :options="[
          { label: 'כתיב מלא', value: false },
          { label: 'כיסוי (ה←ק)', value: true },
        ]"
      />
    </SettingRow>
  </div>
</template>

<style scoped>
.general-pane {
  display: contents;
}

.section-label {
  font-size: 12px;
  font-weight: 700;
  color: var(--text-primary);
  padding: 4px 0;
  margin-bottom: 10px;
  border-bottom: 1px solid var(--border-color);
}
.section-label:not(:first-child) {
  margin-top: 16px;
}
</style>
