<script setup lang="ts">
import { ref, nextTick, onMounted } from 'vue'
import { IconSearch20Regular, IconNavigation20Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useSettingsSearch, type SettingsNavEntry } from './useSettingsSearch'
import SettingsPageSideNav from './SettingsPageSideNav.vue'
import SettingsPageThemeAndApplicationSection from './SettingsPageThemeAndApplicationSection.vue'
import SettingsPageReadingAndBookDisplaySection from './SettingsPageReadingAndBookDisplaySection.vue'
import SettingsPageCalendarSection from './SettingsPageCalendarSection.vue'
import SettingsPageAdvancedSection from './SettingsPageAdvancedSection.vue'
import SettingsPageResetSection from './SettingsPageResetSection.vue'
import SettingsPageKeyboardShortcutsSection from './SettingsPageKeyboardShortcutsSection.vue'

// scrollContainerRef is the full-width body — scrollbar lives at the page edge
const scrollContainerRef = ref<HTMLElement | null>(null)
const { searchQuery, getSectionNavEntries, getSectionNavTree } = useSettingsSearch(scrollContainerRef)

// ── Side nav tree (wide screen) ───────────────────────────────────────────────

const sideNavTree = ref<SettingsNavEntry[]>([])
const sideNavExpandedSections = ref<Set<string>>(new Set())

function rebuildSideNavTree() {
  sideNavTree.value = getSectionNavTree()
  sideNavExpandedSections.value = new Set()
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

// ── Section navigation ────────────────────────────────────────────────────────

async function navigateToSection(sectionId: string) {
  navPanelOpen.value = false
  await nextTick()
  const el =
    document.querySelector<HTMLElement>(`[data-section="${sectionId}"]`) ??
    document.getElementById(sectionId)
  el?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
</script>

<template>
  <div class="settings-page">

    <SettingsPageSideNav
      :tree="sideNavTree"
      :expanded-sections="sideNavExpandedSections"
      @navigate="navigateToSection"
      @toggle-section="toggleSideNavSection"
    />

    <!-- ── Full-width scroller — scrollbar at page edge ── -->
    <div ref="scrollContainerRef" class="settings-body">
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

        <SettingsPageThemeAndApplicationSection />
        <SettingsPageReadingAndBookDisplaySection />
        <SettingsPageCalendarSection />
        <SettingsPageAdvancedSection />
        <SettingsPageResetSection />
        <SettingsPageKeyboardShortcutsSection />

      </div>
    </div>

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
  container-type: inline-size;
}

.settings-body {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  min-width: 0;
}

.settings-body-inner {
  max-width: 720px;
  margin: 0 auto;
  padding: 0 24px 40px;
  box-sizing: border-box;
}

/* ── Sticky search bar ── */
.settings-toolbar {
  position: sticky;
  top: 0;
  z-index: 10;
  background: var(--bg-primary);
  padding: 24px 24px 20px;
  margin-bottom: 4px;
  margin-inline: -24px;
}

.nav-toggle-wrapper {
  position: relative;
  flex-shrink: 0;
}

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
  min-width: 0;
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

@container (min-width: 900px) {
  .narrow-only {
    display: none;
  }
}
</style>

<!-- Section cards and headers consumed by all child section components -->
<style>
[data-section] {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 8px 24px 16px;
  margin-bottom: 24px;
  scroll-margin-top: 64px;
}

.section-label {
  font-size: 15px;
  font-weight: 600;
  color: var(--text-primary);
  padding: 14px 0;
  scroll-margin-top: 56px;
}

.subsection-label {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  padding: 4px 0;
  margin-top: 16px;
  margin-bottom: 10px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 60%, transparent);
  scroll-margin-top: 56px;
}

[data-section-hidden] {
  display: none !important;
}

/* ── Row-style settings (Edge-like): hairline divider between rows instead
   of separate boxes. Control keeps its original full-width line below the
   label — untouched button-group layout — so ToggleGroup's grid always gets
   the same available width it always had. ── */
[data-section] .setting-row-item {
  margin-bottom: 0;
  padding: 12px 0;
  border-top: 1px solid var(--border-color);
}

[data-section] .setting-row-item:first-of-type {
  border-top: none;
}

[data-section] .setting-row-item .setting-label {
  font-size: 13.5px;
  color: var(--text-primary);
}
</style>
