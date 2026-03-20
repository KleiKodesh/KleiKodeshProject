<script setup lang="ts">
import { ref } from 'vue'
import { useSettingsPage } from './useSettingsPage'
import SettingRow from './SettingRow.vue'
import SliderSetting from './SliderSetting.vue'
import ToggleGroup from './ToggleGroup.vue'
import ThemePicker from './ThemePicker.vue'
import FontDisplaySettings from './FontDisplaySettings.vue'

const {
  availableFonts,
  censorDivineNames, headerFont, textFont, fontSize, linePadding,
  commentaryHeaderFont, commentaryTextFont, commentaryFontSize, commentaryLinePadding,
  useSeparateCommentarySettings, appZoom,
  newTabPage,
  reset,
} = useSettingsPage()

const activeTab = ref<'general' | 'reading'>('general')
const bookDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)
const commentaryDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)
</script>

<template>
  <div class="settings-page">

    <div class="tab-bar">
      <button :class="['tab-btn', { active: activeTab === 'general' }]" @click="activeTab = 'general'">כללי</button>
      <button :class="['tab-btn', { active: activeTab === 'reading' }]" @click="activeTab = 'reading'">קריאה</button>
      <button class="tab-btn tab-btn-reset" @click="reset">↺ איפוס</button>
    </div>

    <div v-if="activeTab === 'general'" class="pane">

      <SettingRow label="ערכת נושא">
        <ThemePicker />
      </SettingRow>

      <SliderSetting label="זום האפליקציה" v-model="appZoom" :min="0.5" :max="1.5" :step="0.05" />

      <SettingRow label="כיסוי שם ה'">
        <ToggleGroup
          v-model="censorDivineNames"
          :options="[{ label: 'כתיב מלא', value: false }, { label: 'כיסוי (ה→ק)', value: true }]"
        />
      </SettingRow>

      <SettingRow label="דף ברירת מחדל" wrap>
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

    </div>

    <div v-if="activeTab === 'reading'" class="pane">

      <div class="section-header">תצוגת ספר</div>

      <FontDisplaySettings
        ref="bookDisplayRef"
        :available-fonts="availableFonts"
        v-model:header-font="headerFont"
        v-model:text-font="textFont"
        v-model:font-size="fontSize"
        v-model:line-padding="linePadding"
        @close-other="commentaryDisplayRef?.closeDropdowns()"
      />

      <div class="section-header">תצוגת פירושים</div>

      <SettingRow>
        <ToggleGroup
          v-model="useSeparateCommentarySettings"
          :options="[{ label: 'זהה לתצוגת ספר', value: false }, { label: 'הגדרות נפרדות', value: true }]"
        />
      </SettingRow>

      <FontDisplaySettings
        v-if="useSeparateCommentarySettings"
        ref="commentaryDisplayRef"
        :available-fonts="availableFonts"
        v-model:header-font="commentaryHeaderFont"
        v-model:text-font="commentaryTextFont"
        v-model:font-size="commentaryFontSize"
        v-model:line-padding="commentaryLinePadding"
        @close-other="bookDisplayRef?.closeDropdowns()"
      />

    </div>
  </div>
</template>

<style scoped>
.settings-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  direction: rtl;
  background: var(--bg-primary);
}

.tab-bar {
  display: flex;
  flex-shrink: 0;
  border-bottom: 1px solid var(--border-color);
  background: var(--bg-toolbar);
}

.tab-btn {
  flex: 1;
  padding: 0 12px;
  height: 32px;
  border: none;
  border-bottom: 2px solid transparent;
  border-radius: 0;
  background: transparent;
  color: var(--text-secondary);
  font-size: 12px;
  cursor: pointer;
}
.tab-btn:hover { background: var(--hover-bg); color: var(--text-primary); }
.tab-btn.active { color: var(--text-primary); border-bottom-color: var(--accent-color); }
.tab-btn-reset { flex: 0 0 auto; padding: 0 12px; border-bottom: none; }
.tab-btn-reset:hover { color: #e53e3e; background: color-mix(in srgb, #e53e3e 8%, transparent); }

.pane {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 12px 16px;
}

.section-header {
  font-size: 12px;
  font-weight: 700;
  color: var(--text-primary);
  padding: 4px 0;
  margin-top: 16px;
  margin-bottom: 10px;
  border-bottom: 1px solid var(--border-color);
}

</style>
