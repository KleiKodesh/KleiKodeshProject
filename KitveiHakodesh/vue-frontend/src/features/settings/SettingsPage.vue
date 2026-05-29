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

// scrollContainerRef is the full-width body — scrollbar lives at the page edge
const scrollContainerRef = ref<HTMLElement | null>(null)
const { searchQuery, getSectionNavEntries } = useSettingsSearch(scrollContainerRef)

// ── Nav dropdown ─────────────────────────────────────────────────────────────

const navPanelOpen = ref(false)
const navToggleRef = ref<HTMLElement | null>(null)
const navPanelRef = ref<HTMLElement | null>(null)
const navEntries = ref<{ id: string; label: string }[]>([])

const { justClosed } = useDropdownClose(navPanelRef, () => { navPanelOpen.value = false }, {
  toggleButton: navToggleRef,
})

onMounted(() => {
  nextTick(() => { navEntries.value = getSectionNavEntries() })
})

function toggleNavPanel() {
  if (justClosed.value) return
  navEntries.value = getSectionNavEntries()
  navPanelOpen.value = !navPanelOpen.value
}

// ── Section navigation ───────────────────────────────────────────────────────

async function navigateToSection(sectionId: string) {
  navPanelOpen.value = false
  await nextTick()
  // Target the card element (data-section) so scroll-margin-top on the card is respected
  const el = document.querySelector<HTMLElement>(`[data-section="${sectionId}"]`)
  el?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

// ── Font display refs for cross-instance close coordination ──────────────────

const bookDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)
const commentaryDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)
</script>

<template>
  <div class="settings-page">

    <!-- ── Full-width scroller — scrollbar at page edge ── -->
    <div ref="scrollContainerRef" class="settings-body">
      <!-- ── Centered content column ── -->
      <div class="settings-body-inner">

        <!-- ── Sticky search bar + nav dropdown ── -->
        <div class="settings-toolbar">
          <div class="search-container">
            <div class="nav-toggle-wrapper">
              <button
                ref="navToggleRef"
                class="nav-toggle-btn"
                :class="{ active: navPanelOpen }"
                @click="toggleNavPanel"
                aria-label="ניווט הגדרות"
              >
                <IconNavigation20Regular />
              </button>
              <!-- Nav dropdown — anchored directly below the toggle button -->
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
            <input
              v-model="searchQuery"
              class="search-input"
              type="search"
              placeholder="חיפוש הגדרות..."
              autocomplete="off"
            />
            <IconSearch20Regular class="search-icon" />
          </div>
        </div>

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

        <!-- ── תצוגת ספר + תצוגת פירושים ── -->
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

        <!-- ── קיצורי מקשים ── -->
        <div data-section="section-shortcuts" data-section-label="קיצורי מקשים">
          <div id="section-shortcuts" class="section-label">קיצורי מקשים</div>
          <div class="shortcuts-grid">
            <!-- Tab management -->
            <div class="shortcut-row">
              <kbd>Alt</kbd><span class="kbd-plus">+</span><kbd>N</kbd>
              <span class="shortcut-desc">לשונית חדשה</span>
            </div>
            <div class="shortcut-row">
              <kbd>Alt</kbd><span class="kbd-plus">+</span><kbd>T</kbd>
              <span class="shortcut-desc">פתח רשימת לשוניות</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>W</kbd>
              <span class="shortcut-desc">סגור לשונית</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>X</kbd>
              <span class="shortcut-desc">סגור את כל הלשוניות</span>
            </div>
            <!-- Navigation -->
            <div class="shortcut-row">
              <kbd>Alt</kbd><span class="kbd-plus">+</span><kbd>Home</kbd>
              <span class="shortcut-desc">עבור לדף הבית</span>
            </div>
            <div class="shortcut-row">
              <kbd>Alt</kbd><span class="kbd-plus">+</span><kbd>M</kbd>
              <span class="shortcut-desc">פתח תפריט ראשי</span>
            </div>
            <div class="shortcut-row">
              <kbd>Alt</kbd><span class="kbd-plus">+</span><kbd>L</kbd>
              <span class="shortcut-desc">החלף ערכת נושא</span>
            </div>
            <!-- Book view controls -->
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>B</kbd>
              <span class="shortcut-desc">הצג / הסתר סרגל כלים (בתצוגת ספר)</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>J</kbd>
              <span class="shortcut-desc">הצג / הסתר מפרשים (בתצוגת ספר)</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>F</kbd>
              <span class="shortcut-desc">חיפוש (בתצוגת ספר)</span>
            </div>
            <!-- Zoom controls -->
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>+</kbd>
              <span class="shortcut-desc">הגדל תצוגה</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>-</kbd>
              <span class="shortcut-desc">הקטן תצוגה</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>0</kbd>
              <span class="shortcut-desc">אפס גודל תצוגה</span>
            </div>
            <!-- Display modes -->
            <div class="shortcut-row">
              <kbd>Alt</kbd><span class="kbd-plus">+</span><kbd>F</kbd>
              <span class="shortcut-desc">הצג / הסתר סרגל האפליקציה</span>
            </div>
            <div class="shortcut-row">
              <kbd>F11</kbd>
              <span class="shortcut-desc">מסך מלא</span>
            </div>
            <div class="shortcut-row">
              <kbd>F7</kbd>
              <span class="shortcut-desc">הפעלת סמן טקסט מהבהב, בדומה לעורך טקסט</span>
            </div>
          </div>
        </div>

      </div><!-- end settings-body-inner -->
    </div><!-- end settings-body -->

  </div>
</template>

<style scoped>
.settings-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  direction: rtl;
  background: var(--bg-primary);
  position: relative;
}

/* ── Sticky search bar — lives inside the scroll flow, sticks to the top ── */
.settings-toolbar {
  position: sticky;
  top: 0;
  z-index: 10;
  background: var(--bg-primary);
  padding: 8px 0;
  margin-bottom: 4px;
  /* Bleed background to the edges of the inner column */
  margin-inline: -16px;
  padding-inline: 16px;
}

/* Anchor for the dropdown — wraps the toggle button */
.nav-toggle-wrapper {
  position: relative;
  flex-shrink: 0;
}

.search-container {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 32px;
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
.nav-toggle-btn.active {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
}

/* Nav dropdown — anchored to physical right, below the toggle button */
.nav-panel {
  position: absolute;
  top: calc(100% + 4px);
  right: 0;
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

/* ── Full-width scroller: scrollbar at the page edge ── */
.settings-body {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
}

/* ── Centered content column inside the scroller ── */
.settings-body-inner {
  max-width: 680px;
  margin: 0 auto;
  padding: 12px 16px 40px;
  box-sizing: border-box;
}

/* ── Section cards ── */
:deep([data-section]) {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 16px 20px;
  margin-bottom: 16px;
  scroll-margin-top: 64px;
}

/* ── Section headers ── */
:deep(.section-label) {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
  padding: 0 0 8px;
  margin-bottom: 12px;
  border-bottom: 1px solid var(--border-color);
  scroll-margin-top: 56px;
}

/* ── Keyboard shortcuts grid ── */
.shortcuts-grid {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.shortcut-row {
  display: flex;
  align-items: center;
  gap: 4px;
  min-height: 28px;
}

.shortcut-desc {
  font-size: 13px;
  color: var(--text-primary);
  margin-inline-start: 8px;
}

kbd {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 26px;
  height: 22px;
  padding: 0 6px;
  font-family: 'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif;
  font-size: 11px;
  font-weight: 600;
  color: var(--text-primary);
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 1px 0 var(--border-color);
  white-space: nowrap;
  direction: ltr;
}

.kbd-plus {
  font-size: 11px;
  color: var(--text-secondary);
  line-height: 1;
}

:deep(.subsection-label) {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  padding: 4px 0;
  margin-top: 16px;
  margin-bottom: 10px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 60%, transparent);
  scroll-margin-top: 56px;
}
</style>

<style>
[data-section-hidden] {
  display: none !important;
}
</style>
