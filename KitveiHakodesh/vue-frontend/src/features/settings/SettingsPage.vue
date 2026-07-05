<script setup lang="ts">
import { ref, computed, nextTick, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { IconSearch20Regular, IconNavigation20Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useThemeStore } from '@/theme/themeStore'
import { useSettings } from './useSettingsPage'
import { useSettingsSearch, type SettingsNavEntry } from './useSettingsSearch'
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
  linesContentMaxWidth,
  commentaryMaxWidth,
  titleBarHiddenButtons,
  pdfPageFilters,
} = storeToRefs(settings)

const themeStore = useThemeStore()
const { themePreset } = storeToRefs(themeStore)

const isDarkMode = computed(() => themePreset.value.includes('-dark'))

function applyDarkMode(value: boolean) {
  if (value !== isDarkMode.value) themeStore.toggleDarkMode()
}

// ── Title bar button toggle ───────────────────────────────────────────────────

const TITLE_BAR_BUTTONS = [
  { id: 'hamburger',      label: 'תפריט' },
  { id: 'theme-toggle',   label: 'ערכת נושא' },
  { id: 'toolbar-toggle', label: 'סרגל כלים' },
  { id: 'pdf-filter',     label: 'ערכת נושא ל-PDF' },
  { id: 'ocr',            label: 'OCR' },
  { id: 'home',           label: 'בית' },
  { id: 'new-tab',        label: 'לשונית חדשה' },
  { id: 'close-tab',      label: 'סגור לשונית' },
]

function applyPdfPageFilters(value: boolean) {
  if (value !== pdfPageFilters.value) settings.togglePdfPageFilters()
}

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

const bookViewStore = useBookViewStore()
const { toolbarPosition } = storeToRefs(bookViewStore)

useSettings() // wires the commentary-mirror watcher

// ── Search (DOM walker) ──────────────────────────────────────────────────────

// scrollContainerRef is the full-width body — scrollbar lives at the page edge
const scrollContainerRef = ref<HTMLElement | null>(null)
const { searchQuery, getSectionNavEntries, getSectionNavTree } = useSettingsSearch(scrollContainerRef)

// ── Side nav tree (wide screen) ───────────────────────────────────────────────

const sideNavTree = ref<SettingsNavEntry[]>([])
const sideNavExpandedSections = ref<Set<string>>(new Set())

function rebuildSideNavTree() {
  sideNavTree.value = getSectionNavTree()
  // Auto-expand all sections that have children
  sideNavExpandedSections.value = new Set(
    sideNavTree.value.filter((entry) => entry.children.length > 0).map((entry) => entry.id),
  )
}

function toggleSideNavSection(sectionId: string) {
  const expanded = new Set(sideNavExpandedSections.value)
  if (expanded.has(sectionId)) {
    expanded.delete(sectionId)
  } else {
    expanded.add(sectionId)
  }
  sideNavExpandedSections.value = expanded
}

// ── Nav dropdown (narrow screen) ─────────────────────────────────────────────

const navPanelOpen = ref(false)
const navToggleRef = ref<HTMLElement | null>(null)
const navPanelRef = ref<HTMLElement | null>(null)
const navEntries = ref<{ id: string; label: string }[]>([])

const { justClosed } = useDropdownClose(navPanelRef, () => { navPanelOpen.value = false }, {
  toggleButton: navToggleRef,
})

onMounted(() => {
  nextTick(() => {
    navEntries.value = getSectionNavEntries()
    rebuildSideNavTree()
  })
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
  // Try data-section card first, then fall back to a plain id (subsection headings)
  const el =
    document.querySelector<HTMLElement>(`[data-section="${sectionId}"]`) ??
    document.getElementById(sectionId)
  el?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

// ── Font display refs for cross-instance close coordination ──────────────────

const bookDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)
const commentaryDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)

// 950 is the sentinel "ללא הגבלה" stop at the top of the slider.
// Stored value is 0 (unlimited) or 400–900 (px). The slider uses 400–950 step 50.
const CONTENT_MAX_WIDTH_UNLIMITED_SENTINEL = 950

function formatMaxWidth(value: number): string {
  return value === CONTENT_MAX_WIDTH_UNLIMITED_SENTINEL ? 'ללא הגבלה' : `${value}px`
}

const linesContentMaxWidthSlider = computed({
  get: () => linesContentMaxWidth.value === 0 ? CONTENT_MAX_WIDTH_UNLIMITED_SENTINEL : linesContentMaxWidth.value,
  set: (sliderValue: number) => {
    linesContentMaxWidth.value = sliderValue === CONTENT_MAX_WIDTH_UNLIMITED_SENTINEL ? 0 : sliderValue
  },
})

const commentaryMaxWidthSlider = computed({
  get: () => commentaryMaxWidth.value === 0 ? CONTENT_MAX_WIDTH_UNLIMITED_SENTINEL : commentaryMaxWidth.value,
  set: (sliderValue: number) => {
    commentaryMaxWidth.value = sliderValue === CONTENT_MAX_WIDTH_UNLIMITED_SENTINEL ? 0 : sliderValue
  },
})
</script>

<template>
  <div class="settings-page">

    <!-- ── Wide-screen side nav ── -->
    <nav class="settings-side-nav">
      <ul class="side-nav-list">
        <li v-for="entry in sideNavTree" :key="entry.id" class="side-nav-section">
          <button
            class="side-nav-section-btn"
            :class="{ 'has-children': entry.children.length > 0 }"
            @click="entry.children.length > 0 ? toggleSideNavSection(entry.id) : navigateToSection(entry.id)"
          >
            <span class="side-nav-section-label" @click.stop="navigateToSection(entry.id)">
              {{ entry.label }}
            </span>
            <span
              v-if="entry.children.length > 0"
              class="side-nav-chevron"
              :class="{ expanded: sideNavExpandedSections.has(entry.id) }"
            >›</span>
          </button>
          <ul
            v-if="entry.children.length > 0 && sideNavExpandedSections.has(entry.id)"
            class="side-nav-children"
          >
            <li v-for="child in entry.children" :key="child.id">
              <div v-if="child.isSubsectionHeading" class="side-nav-subsection-heading">
                {{ child.label }}
              </div>
              <button v-else class="side-nav-child-btn" @click="navigateToSection(child.id)">
                {{ child.label }}
              </button>
            </li>
          </ul>
        </li>
      </ul>
    </nav>

    <!-- ── Full-width scroller — scrollbar at page edge ── -->
    <div ref="scrollContainerRef" class="settings-body">
      <!-- ── Centered content column ── -->
      <div class="settings-body-inner">

        <!-- ── Sticky search bar + nav dropdown (narrow screen) ── -->
        <div class="settings-toolbar">
          <div class="search-container">
            <div class="nav-toggle-wrapper narrow-only">
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
        </div>

        <!-- ── אפליקציה ── -->
        <div data-section="section-app" data-section-label="אפליקציה">
          <div id="section-app" class="section-label">אפליקציה</div>

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

          <SettingRow id="nav-toolbar-position" data-nav-label="מיקום סרגל הכלים" label="מיקום סרגל הכלים בתצוגת ספר" wrap>
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

          <div id="section-title-bar-buttons" class="subsection-label">כפתורי סרגל הכלים</div>
          <SettingRow id="nav-title-bar-buttons" data-nav-label="הצג / הסתר כפתורים" label="הצג / הסתר כפתורים" hint="לחץ על כפתור כדי להחליף מצב הצגה" wrap>
            <div class="title-bar-chips">
              <button
                v-for="button in TITLE_BAR_BUTTONS"
                :key="button.id"
                class="title-bar-chip"
                :class="{ active: isTitleBarButtonEnabled(button.id) }"
                @click="toggleTitleBarButton(button.id)"
              >{{ button.label }}</button>
            </div>
          </SettingRow>
        </div>

        <!-- ── קריאה ── -->        <div data-section="section-reading" data-section-label="קריאה">
          <div id="section-reading" class="section-label">קריאה</div>

          <SettingRow
            id="nav-resume-last-read"
            data-nav-label="זכור מיקום אחרון"
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
            id="nav-auto-sync-commentary"
            data-nav-label="סנכרן מפרשים"
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

          <SettingRow id="nav-censor-divine-names" data-nav-label="כיסוי שם ה'" label="כיסוי שם ה'" hint="מחליף את האות ה׳ בשמות הקודש באות ד׳">
            <ToggleGroup
              v-model="censorDivineNames"
              :options="[
                { label: 'כיסוי (ה←ד)', value: true },
                { label: 'כתיב מלא', value: false },
              ]"
            />
          </SettingRow>
        </div>

        <!-- ── תצוגת ספר + תצוגת פירושים ── -->
        <div data-section="section-book-display" data-section-label="תצוגת ספר">
          <div id="section-book-display" class="section-label">תצוגת ספר</div>

          <FontDisplaySettings
            id="nav-book-font-display"
            data-nav-label="גופן וגודל"
            ref="bookDisplayRef"
            v-model:header-font="headerFont"
            v-model:text-font="textFont"
            v-model:font-size="fontSize"
            v-model:line-padding="linePadding"
            @close-other="commentaryDisplayRef?.closeDropdowns()"
          />

          <SliderSetting
            id="nav-lines-max-width"
            data-nav-label="רוחב מקסימלי"
            label="רוחב מקסימלי עבור עמודת הטקסט"
            hint="הגבל את רוחב שורת הקריאה לנוחות מרבית"
            v-model="linesContentMaxWidthSlider"
            :min="500"
            :max="950"
            :step="50"
            :format-value="formatMaxWidth"
          />

          <div id="section-commentary-display" class="subsection-label">תצוגת פירושים</div>

          <SettingRow id="nav-commentary-settings-mode" data-nav-label="הגדרות נפרדות לפירושים" hint="האם להשתמש בהגדרות גופן נפרדות לפירושים, או לרשת את הגדרות הספר">
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
            id="nav-commentary-font-display"
            data-nav-label="גופן פירושים"
            ref="commentaryDisplayRef"
            v-model:header-font="commentaryHeaderFont"
            v-model:text-font="commentaryTextFont"
            v-model:font-size="commentaryFontSize"
            v-model:line-padding="commentaryLinePadding"
            @close-other="bookDisplayRef?.closeDropdowns()"
          />

          <SliderSetting
            v-if="useSeparateCommentarySettings"
            id="nav-commentary-max-width"
            data-nav-label="רוחב מקסימלי פירושים"
            label="רוחב מקסימלי עבור עמודת הפירושים"
            hint="הגבל את רוחב שורת הקריאה בפירושים לנוחות מרבית"
            v-model="commentaryMaxWidthSlider"
            :min="500"
            :max="950"
            :step="50"
            :format-value="formatMaxWidth"
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
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>N</kbd>
              <span class="shortcut-desc">לשונית חדשה</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>T</kbd>
              <span class="shortcut-desc">פתח רשימת לשוניות</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Tab</kbd>
              <span class="shortcut-desc">לשונית הבאה</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Shift</kbd><span class="kbd-plus">+</span><kbd>Tab</kbd>
              <span class="shortcut-desc">לשונית הקודמת</span>
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
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>G</kbd>
              <span class="shortcut-desc">עבור לדף הבית</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>M</kbd>
              <span class="shortcut-desc">פתח תפריט ראשי</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>L</kbd>
              <span class="shortcut-desc">החלף ערכת נושא</span>
            </div>
            <!-- Quick navigation -->
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>1</kbd>
              <span class="shortcut-desc">ספרים</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>2</kbd>
              <span class="shortcut-desc">חיפוש</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>3</kbd>
              <span class="shortcut-desc">היברו-בוקס</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>4</kbd>
              <span class="shortcut-desc">פתח קובץ</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>5</kbd>
              <span class="shortcut-desc">חיפוש קבצים</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>6</kbd>
              <span class="shortcut-desc">מילון</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>7</kbd>
              <span class="shortcut-desc">לוח שנה</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>8</kbd>
              <span class="shortcut-desc">מידות ושיעורים</span>
            </div>
            <div class="shortcut-row">
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>9</kbd>
              <span class="shortcut-desc">סביבות עבודה</span>
            </div>
            <div class="shortcut-row">
              <kbd>F1</kbd>
              <span class="shortcut-desc">הגדרות</span>
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
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>K</kbd>
              <span class="shortcut-desc">הצג / הסתר תוכן עניינים (בתצוגת ספר)</span>
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
              <kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>H</kbd>
              <span class="shortcut-desc">הצג / הסתר סרגל האפליקציה</span>
            </div>
            <div class="shortcut-row">
              <kbd>F11</kbd>
              <span class="shortcut-desc">מסך מלא</span>
            </div>
            <div class="shortcut-row">
              <kbd>F7</kbd>
              <span class="shortcut-desc">הפעלת סמן טקסט לניווט ולבחירת טקסט באמצעות המקלדת</span>
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
  flex-direction: row;
  height: 100%;
  direction: rtl;
  background: var(--bg-primary);
  position: relative;
}

/* ── Side nav — hidden on narrow, fixed column on wide ── */
.settings-side-nav {
  display: none;
  flex-shrink: 0;
  width: 180px;
  height: 100%;
  overflow-y: auto;
  border-left: 1px solid var(--border-color);
  padding: 16px 0 40px;
  background: var(--bg-secondary);
}

.side-nav-list {
  list-style: none;
  margin: 0;
  padding: 0;
}

.side-nav-section {
  margin: 0;
}

.side-nav-section-btn {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  height: 32px;
  padding: 0 14px;
  background: transparent;
  border: none;
  border-radius: 0;
  cursor: pointer;
  color: var(--text-primary);
  font-size: 13px;
  text-align: right;
  gap: 4px;
}
.side-nav-section-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.side-nav-section-label {
  flex: 1;
  text-align: right;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.side-nav-chevron {
  flex-shrink: 0;
  font-size: 14px;
  color: var(--text-secondary);
  line-height: 1;
  display: inline-block;
  transform: rotate(90deg);
  transition: transform 120ms ease;
}
.side-nav-chevron.expanded {
  transform: rotate(-90deg);
}

.side-nav-children {
  list-style: none;
  margin: 0;
  padding: 0;
}

.side-nav-child-btn {
  display: flex;
  align-items: center;
  width: 100%;
  height: 28px;
  padding: 0 14px 0 20px;
  background: transparent;
  border: none;
  border-radius: 0;
  cursor: pointer;
  color: var(--text-secondary);
  font-size: 12px;
  text-align: right;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.side-nav-child-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
  color: var(--text-primary);
}

.side-nav-subsection-heading {
  padding: 6px 14px 2px;
  font-size: 10px;
  font-weight: 600;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  opacity: 0.7;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* ── Full-width scroller: scrollbar at the page edge ── */
.settings-body {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  min-width: 0;
}

/* ── Centered content column inside the scroller ── */
.settings-body-inner {
  max-width: 680px;
  margin: 0 auto;
  padding: 12px 16px 40px;
  box-sizing: border-box;
}

/* ── Sticky search bar — lives inside the scroll flow, sticks to the top ── */
.settings-toolbar {
  position: sticky;
  top: 0;
  z-index: 10;
  background: var(--bg-primary);
  padding: 8px 0;
  margin-bottom: 4px;
  margin-inline: -16px;
  padding-inline: 16px;
}

/* Anchor for the dropdown — wraps the toggle button */
.nav-toggle-wrapper {
  position: relative;
  flex-shrink: 0;
}

/* Hide the dropdown toggle on wide screens where the side nav is visible */
.narrow-only {
  display: flex;
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

/* ── Title bar button chips ── */
.title-bar-chips {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
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
  white-space: nowrap;
  border-radius: 4px;
}

.title-bar-chip:hover {
  background: var(--hover-bg);
}

.title-bar-chip.active {
  background: var(--accent-color);
  color: white;
  border-color: var(--accent-color);
}

/* ── Wide screen: show side nav, hide dropdown toggle ── */
@media (min-width: 900px) {
  .settings-side-nav {
    display: block;
  }
  .narrow-only {
    display: none;
  }
}
</style>

<style>
[data-section-hidden] {
  display: none !important;
}
</style>
