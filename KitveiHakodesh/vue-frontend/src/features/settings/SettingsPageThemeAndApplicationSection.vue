<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { useThemeStore } from '@/theme/themeStore'
import SettingRow from './SettingRow.vue'
import SliderSetting from './SliderSetting.vue'
import ToggleGroup from './ToggleGroup.vue'
import ThemePicker from './ThemePicker.vue'
import ThemeBackgroundTintSlider from './ThemeBackgroundTintSlider.vue'
import { useI18n } from 'vue-i18n'
import { uiLanguage, setUiLanguage, type UiLanguage } from '@/i18n'

const { t } = useI18n()

const settings = useSettingsStore()
const { appZoom, newTabPage, titleBarHiddenButtons, pdfPageFilters, scrollbarsHidden, showRecentlyOpened, showFrequentFolders } = storeToRefs(settings)

const themeStore = useThemeStore()
const { themePreset } = storeToRefs(themeStore)

const isDarkMode = computed(() => themePreset.value.includes('-dark'))

function applyDarkMode(value: boolean) {
  if (value !== isDarkMode.value) themeStore.toggleDarkMode()
}

function applyPdfPageFilters(value: boolean) {
  if (value !== pdfPageFilters.value) settings.togglePdfPageFilters()
}

const TITLE_BAR_BUTTONS = computed(() => [
  { id: 'hamburger',      label: t('settings.titleBarButton.menu') },
  { id: 'theme-toggle',   label: t('settings.titleBarButton.theme') },
  { id: 'toolbar-toggle', label: t('settings.titleBarButton.bookToolbar') },
  { id: 'split-view',     label: t('settings.titleBarButton.splitView') },
  { id: 'ocr',            label: 'OCR' },
  { id: 'home',           label: t('settings.titleBarButton.home') },
  { id: 'workspaces',     label: t('settings.titleBarButton.workspaces') },
  { id: 'prev-tab',       label: t('settings.titleBarButton.back') },
  { id: 'next-tab',       label: t('settings.titleBarButton.forward') },
])

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
  <div data-section="section-theme" :data-section-label="$t('settings.theme.section')">
    <div id="section-theme" class="section-label">{{ $t('settings.theme.section') }}</div>

    <SettingRow id="nav-theme-picker" :data-nav-label="$t('settings.theme.section')" :label="$t('settings.theme.section')" :hint="$t('settings.theme.pickerHint')">
      <ThemePicker />
    </SettingRow>

    <ThemeBackgroundTintSlider id="nav-background-tint" :data-nav-label="$t('settings.theme.tintNav')" />

    <SettingRow id="nav-dark-mode" :data-nav-label="$t('settings.theme.darkMode')" :label="$t('settings.theme.darkMode')" :hint="$t('settings.theme.darkModeHint')">
      <ToggleGroup
        :model-value="isDarkMode"
        :options="[
          { label: $t('settings.theme.light'), value: false },
          { label: $t('settings.theme.dark'), value: true },
        ]"
        @update:model-value="applyDarkMode"
      />
    </SettingRow>

    <SettingRow id="nav-pdf-filters" :data-nav-label="$t('settings.theme.pdfNav')" :label="$t('settings.theme.pdf')" :hint="$t('settings.theme.pdfHint')">
      <ToggleGroup
        :model-value="pdfPageFilters"
        :options="[
          { label: $t('settings.yes'), value: true },
          { label: $t('settings.no'), value: false },
        ]"
        @update:model-value="applyPdfPageFilters"
      />
    </SettingRow>

    <SettingRow id="nav-scrollbars-hidden" :data-nav-label="$t('settings.theme.hideScrollbars')" :label="$t('settings.theme.hideScrollbars')" :hint="$t('settings.theme.hideScrollbarsHint')">
      <ToggleGroup
        v-model="scrollbarsHidden"
        :options="[
          { label: $t('settings.yes'), value: true },
          { label: $t('settings.no'), value: false },
        ]"
      />
    </SettingRow>

    <SliderSetting
      id="nav-app-zoom"
      :data-nav-label="$t('settings.theme.zoom')"
      :label="$t('settings.theme.zoom')"
      v-model="appZoom"
      :min="0.5"
      :max="1.5"
      :step="0.05"
      :hint="$t('settings.theme.zoomHint')"
    />
  </div>

  <!-- ── אפליקציה ── -->
  <div data-section="section-app" :data-section-label="$t('settings.app.section')">
    <div id="section-app" class="section-label">{{ $t('settings.app.section') }}</div>

    <SettingRow id="nav-ui-language" :data-nav-label="$t('settings.app.uiLanguage')" :label="$t('settings.app.uiLanguage')" :hint="$t('settings.app.uiLanguageHint')">
      <ToggleGroup
        :model-value="uiLanguage"
        :options="[
          { label: 'עברית', value: 'he' },
          { label: 'English', value: 'en' },
        ]"
        @update:model-value="(lang: UiLanguage) => setUiLanguage(lang)"
      />
    </SettingRow>

    <SettingRow id="nav-new-tab-page" :data-nav-label="$t('settings.app.newTab')" :label="$t('settings.app.newTab')" :hint="$t('settings.app.newTabHint')" wrap>
      <ToggleGroup
        v-model="newTabPage"
        :options="[
          { label: $t('settings.app.newTabHome'), value: 'homepage' },
          { label: $t('settings.app.newTabOpenFile'), value: 'openfile' },
          { label: $t('settings.app.newTabHebrewBooks'), value: 'hebrewbooks' },
          { label: $t('settings.app.newTabSearch'), value: 'search' },
        ]"
      />
    </SettingRow>

    <SettingRow id="nav-show-recently-opened" :data-nav-label="$t('settings.app.recentNav')" :label="$t('settings.app.recent')" :hint="$t('settings.app.recentHint')">
      <ToggleGroup
        v-model="showRecentlyOpened"
        :options="[
          { label: $t('settings.yes'), value: true },
          { label: $t('settings.no'), value: false },
        ]"
      />
    </SettingRow>

    <SettingRow id="nav-show-frequent-folders" :data-nav-label="$t('settings.app.frequentNav')" :label="$t('settings.app.frequent')" :hint="$t('settings.app.frequentHint')">
      <ToggleGroup
        v-model="showFrequentFolders"
        :options="[
          { label: $t('settings.yes'), value: true },
          { label: $t('settings.no'), value: false },
        ]"
      />
    </SettingRow>

    <SettingRow id="nav-title-bar-buttons" :data-nav-label="$t('settings.app.titleBarButtons')" :label="$t('settings.app.titleBarButtons')" :hint="$t('settings.app.titleBarButtonsHint')" wrap>
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
