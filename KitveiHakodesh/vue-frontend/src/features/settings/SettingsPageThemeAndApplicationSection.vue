<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { useThemeStore } from '@/theme/themeStore'
import SettingRow from './SettingRow.vue'
import SliderSetting from './SliderSetting.vue'
import ToggleGroup from './ToggleGroup.vue'
import ThemePicker from './ThemePicker.vue'

const settings = useSettingsStore()
const { appZoom, newTabPage, titleBarHiddenButtons, pdfPageFilters, compactMode, contentBorder, scrollbarsHidden, showRecentlyOpened } = storeToRefs(settings)

const themeStore = useThemeStore()
const { themePreset } = storeToRefs(themeStore)

const isDarkMode = computed(() => themePreset.value.includes('-dark'))

function applyDarkMode(value: boolean) {
  if (value !== isDarkMode.value) themeStore.toggleDarkMode()
}

function applyPdfPageFilters(value: boolean) {
  if (value !== pdfPageFilters.value) settings.togglePdfPageFilters()
}

const TITLE_BAR_BUTTONS = [
  { id: 'hamburger',      label: 'תפריט' },
  { id: 'theme-toggle',   label: 'ערכת נושא' },
  { id: 'toolbar-toggle', label: 'סרגל כלים' },
  { id: 'split-view',     label: 'תצוגה מפוצלת' },
  { id: 'ocr',            label: 'OCR' },
  { id: 'home',           label: 'בית' },
  { id: 'prev-tab',       label: 'חזור' },
  { id: 'next-tab',       label: 'קדימה' },
]

function isTitleBarButtonEnabled(buttonId: string): boolean {
  return !titleBarHiddenButtons.value.includes(buttonId)
}

function toggleTitleBarButton(buttonId: string) {
  const hidden = titleBarHiddenButtons.value
  const index = hidden.indexOf(buttonId)
  titleBarHiddenButtons.value = index === -1
    ? [...hidden, buttonId]
    : hidden.filter((id) => id !== buttonId)
}
</script>

<template>
  <!-- ── ערכת נושא ── -->
  <div data-section="section-theme" data-section-label="ערכת נושא">
    <div id="section-theme" class="section-label">ערכת נושא</div>

    <SettingRow id="nav-theme-picker" data-nav-label="ערכת נושא" label="ערכת נושא" hint="צבעי הממשק של האפליקציה">
      <ThemePicker />
    </SettingRow>

    <SettingRow id="nav-dark-mode" data-nav-label="מצב כהה" label="מצב כהה" hint="החלף בין מצב בהיר לכהה">
      <ToggleGroup
        :model-value="isDarkMode"
        :options="[
          { label: 'בהיר', value: false },
          { label: 'כהה', value: true },
        ]"
        @update:model-value="applyDarkMode"
      />
    </SettingRow>

    <SettingRow id="nav-pdf-filters" data-nav-label="החל על דפי PDF" label="החל ערכת נושא על דפי PDF" hint="מחיל את צבעי ערכת הנושא על תוכן דפי PDF">
      <ToggleGroup
        :model-value="pdfPageFilters"
        :options="[
          { label: 'כן', value: true },
          { label: 'לא', value: false },
        ]"
        @update:model-value="applyPdfPageFilters"
      />
    </SettingRow>

    <SettingRow id="nav-compact-mode" data-nav-label="מצב קומפקטי" label="הפעל מצב קומפקטי" hint="מקטין את גובה סרגלי הכלים והכפתורים">
      <ToggleGroup
        v-model="compactMode"
        :options="[
          { label: 'כן', value: true },
          { label: 'לא', value: false },
        ]"
      />
    </SettingRow>

    <SettingRow id="nav-content-border" data-nav-label="מסגרת סביב התוכן" label="הצג מסגרת סביב התוכן" hint="מציג מסגרת מעוגלת עדינה סביב אזור התצוגה">
      <ToggleGroup
        v-model="contentBorder"
        :options="[
          { label: 'כן', value: true },
          { label: 'לא', value: false },
        ]"
      />
    </SettingRow>

    <SettingRow id="nav-scrollbars-hidden" data-nav-label="הסתר פסי גלילה" label="הסתר פסי גלילה" hint="פסי הגלילה יוסתרו ויופיעו רק בזמן גלילה או ריחוף — נכנס לתוקף בהפעלה הבאה של האפליקציה">
      <ToggleGroup
        v-model="scrollbarsHidden"
        :options="[
          { label: 'כן', value: true },
          { label: 'לא', value: false },
        ]"
      />
    </SettingRow>

    <SliderSetting
      id="nav-app-zoom"
      data-nav-label="גודל תצוגה"
      label="גודל תצוגה"
      v-model="appZoom"
      :min="0.5"
      :max="1.5"
      :step="0.05"
      hint="משנה את גודל כל ממשק האפליקציה"
    />
  </div>

  <!-- ── אפליקציה ── -->
  <div data-section="section-app" data-section-label="אפליקציה">
    <div id="section-app" class="section-label">אפליקציה</div>

    <SettingRow id="nav-new-tab-page" data-nav-label="פתח טאב חדש אל" label="פתח טאב חדש אל" hint="הדף שיפתח בלחיצה על טאב חדש" wrap>
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

    <SettingRow id="nav-show-recently-opened" data-nav-label="הצג פתוחים לאחרונה" label="הצג מסמכים שנפתחו לאחרונה בדף הבית" hint="מציג אריחי קיצור דרך לקבצים שנפתחו לאחרונה בתחתית דף הבית">
      <ToggleGroup
        v-model="showRecentlyOpened"
        :options="[
          { label: 'כן', value: true },
          { label: 'לא', value: false },
        ]"
      />
    </SettingRow>

    <SettingRow id="nav-title-bar-buttons" data-nav-label="כפתורים בסרגל הכלים" label="כפתורים בסרגל הכלים" hint="לחץ על כפתור כדי להחליף מצב הצגה" wrap>
      <div class="title-bar-chips">
        <button
          v-for="button in TITLE_BAR_BUTTONS"
          :key="button.id"
          class="title-bar-chip"
          :class="{ active: isTitleBarButtonEnabled(button.id) }"
          :title="button.label"
          @click="toggleTitleBarButton(button.id)"
        >{{ button.label }}</button>
      </div>
    </SettingRow>
  </div>
</template>

<style scoped>
.title-bar-chips {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(130px, 1fr));
  gap: 4px;
  width: 100%;
}

.title-bar-chip {
  height: 28px;
  padding: 0 10px;
  border: 1px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  cursor: pointer;
  font-size: 12px;
  border-radius: 4px;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

.title-bar-chip:hover {
  background: var(--hover-bg);
}

.title-bar-chip.active {
  background: var(--accent-color);
  color: white;
  border-color: var(--accent-color);
}
</style>
