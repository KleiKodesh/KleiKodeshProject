<script setup lang="ts">
import { ref, nextTick, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { IconSearch20Regular, IconNavigation20Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettings } from './useSettingsPage'
import { useSettingsSearch } from './useSettingsSearch'
import SettingRow from './SettingRow.vue'
import SliderSetting from './SliderSetting.vue'
import ToggleGroup from './ToggleGroup.vue'
import ThemePicker from './ThemePicker.vue'
import FontDisplaySettings from './FontDisplaySettings.vue'
import SettingsAdvancedPane from './SettingsAdvancedPane.vue'

// ── Stores ──────────────────────────────────────────────────────────────────

const settings = useSettingsStore()
const {
  censorDivineNames,
  appZoom,
  newTabPage,
  resumeLastRead,
  defaultAutoSyncCommentary,
  headerFont,
  textFont,
  fontSize,
  linePadding,
  commentaryHeaderFont,
  commentaryTextFont,
  commentaryFontSize,
  commentaryLinePadding,
  useSeparateCommentarySettings,
} = storeToRefs(settings)

const bookViewStore = useBookViewStore()
const { toolbarPosition } = storeToRefs(bookViewStore)

useSettings() // wires the commentary-mirror watcher

// ── Search (DOM walker) ──────────────────────────────────────────────────────

const scrollContainerRef = ref<HTMLElement | null>(null)
const { searchQuery, getSectionNavEntries } = useSettingsSearch(scrollContainerRef)

// ── Nav panel ────────────────────────────────────────────────────────────────

const navPanelOpen = ref(false)
const navToggleRef = ref<HTMLElement | null>(null)
const navPanelRef = ref<HTMLElement | null>(null)
const navEntries = ref<{ id: string; label: string }[]>([])

const { justClosed } = useDropdownClose(navPanelRef, () => { navPanelOpen.value = false }, {
  toggleButton: navToggleRef,
})

onMounted(() => {
  // Read nav entries from the DOM after first render
  nextTick(() => { navEntries.value = getSectionNavEntries() })
})

function toggleNavPanel() {
  if (justClosed.value) return
  navEntries.value = getSectionNavEntries()
  navPanelOpen.value = !navPanelOpen.value
}

async function navigateToSection(sectionId: string) {
  navPanelOpen.value = false
  await nextTick()
  const el = document.getElementById(sectionId)
  if (el && scrollContainerRef.value) {
    const containerTop = scrollContainerRef.value.getBoundingClientRect().top
    const elTop = el.getBoundingClientRect().top
    scrollContainerRef.value.scrollTop += elTop - containerTop - 12
  }
}

// ── Font display refs for cross-instance close coordination ──────────────────

const bookDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)
const commentaryDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)
</script>

<template>
  <div class="settings-page">

    <!-- ── Top bar: full-width search with nav toggle inside ── -->
    <div class="settings-top-bar">
      <div class="search-container">
        <IconSearch20Regular class="search-icon" />
        <input
          v-model="searchQuery"
          class="search-input"
          type="search"
          placeholder="חיפוש הגדרות..."
          autocomplete="off"
        />
        <button
          ref="navToggleRef"
          class="nav-toggle-btn"
          :class="{ active: navPanelOpen }"
          @click="toggleNavPanel"
        >
          <IconNavigation20Regular />
        </button>
      </div>

      <!-- Nav panel -->
      <div v-if="navPanelOpen" ref="navPanelRef" class="nav-panel">
        <button
          v-for="entry in navEntries"
          :key="entry.id"
          class="nav-panel-item"
          @click="navigateToSection(entry.id)"
        >
          {{ entry.label }}
        </button>
      </div>
    </div>

    <!-- ── Scrollable content ── -->
    <div ref="scrollContainerRef" class="settings-scroll">

      <!-- ── אפליקציה ── -->
      <div data-section="section-app" data-section-label="אפליקציה">
        <div id="section-app" class="section-label">אפליקציה</div>

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
      </div>

      <!-- ── ניווט ── -->
      <div data-section="section-navigation" data-section-label="ניווט">
        <div id="section-navigation" class="section-label">ניווט</div>

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
      </div>

      <!-- ── קריאה ── -->
      <div data-section="section-reading" data-section-label="קריאה">
        <div id="section-reading" class="section-label">קריאה</div>

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

        <SettingRow label="כיסוי שם ה'" hint="מחליף את האות ה׳ בשמות הקודש באות ד׳">
          <ToggleGroup
            v-model="censorDivineNames"
            :options="[
              { label: 'כתיב מלא', value: false },
              { label: 'כיסוי (ה←ד)', value: true },
            ]"
          />
        </SettingRow>
      </div>

      <!-- ── תצוגת ספר + תצוגת פירושים (subsection) ── -->
      <div data-section="section-book-display" data-section-label="תצוגת ספר">
        <div id="section-book-display" class="section-label">תצוגת ספר</div>

        <FontDisplaySettings
          ref="bookDisplayRef"
          v-model:header-font="headerFont"
          v-model:text-font="textFont"
          v-model:font-size="fontSize"
          v-model:line-padding="linePadding"
          @close-other="commentaryDisplayRef?.closeDropdowns()"
        />

        <div id="section-commentary-display" class="subsection-label">תצוגת פירושים</div>

        <SettingRow hint="האם להשתמש בהגדרות גופן נפרדות לפירושים, או לרשת את הגדרות הספר">
          <ToggleGroup
            v-model="useSeparateCommentarySettings"
            :options="[
              { label: 'זהה לתצוגת ספר', value: false },
              { label: 'הגדרות נפרדות', value: true },
            ]"
          />
        </SettingRow>

        <FontDisplaySettings
          v-if="useSeparateCommentarySettings"
          ref="commentaryDisplayRef"
          v-model:header-font="commentaryHeaderFont"
          v-model:text-font="commentaryTextFont"
          v-model:font-size="commentaryFontSize"
          v-model:line-padding="commentaryLinePadding"
          @close-other="bookDisplayRef?.closeDropdowns()"
        />
      </div>

      <!-- ── Advanced sections (calendar + db + reset) ── -->
      <SettingsAdvancedPane />

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

/* ── Top bar ── */
.settings-top-bar {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 12px;
  position: relative;
}

.search-container {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 6px;
  height: 30px;
  padding: 0 10px;
  background: var(--input-bg, var(--bg-secondary));
  border: 1px solid var(--border-color);
  border-radius: 999px;
}

.search-icon {
  flex-shrink: 0;
  color: var(--text-secondary);
}

.search-input {
  flex: 1;
  height: 100%;
  background: none;
  border: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
  direction: rtl;
}

.nav-toggle-btn {
  flex-shrink: 0;
  width: 24px;
  height: 24px;
  padding: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-secondary);
  border-radius: 50%;
}
.nav-toggle-btn:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

/* ── Nav panel — anchored to the left edge of the search container ── */
.nav-panel {
  position: absolute;
  top: calc(100% + 4px);
  left: 12px;
  min-width: 160px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
  z-index: 1000;
  display: flex;
  flex-direction: column;
  padding: 4px 0;
}

.nav-panel-item {
  height: 32px;
  padding: 0 14px;
  text-align: right;
  font-size: 13px;
  color: var(--text-primary);
  background: transparent;
  border: none;
  border-radius: 0;
  cursor: pointer;
  white-space: nowrap;
}
.nav-panel-item:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

/* ── Scroll area ── */
.settings-scroll {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 12px 16px 24px;
}

/* ── Section headers ── */
:deep(.section-label) {
  font-size: 12px;
  font-weight: 700;
  color: var(--text-primary);
  padding: 4px 0;
  margin-bottom: 10px;
  border-bottom: 1px solid var(--border-color);
  scroll-margin-top: 12px;
}

/* ── Subsection headers (e.g. תצוגת פירושים inside תצוגת ספר) ── */
:deep(.subsection-label) {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  padding: 4px 0;
  margin-top: 14px;
  margin-bottom: 10px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 60%, transparent);
  scroll-margin-top: 12px;
}
</style>

<!-- Hide sections that don't match the search — applied globally so it reaches
     data-section wrappers inside child components (SettingsAdvancedPane). -->
<style>
[data-section-hidden] {
  display: none !important;
}
</style>
