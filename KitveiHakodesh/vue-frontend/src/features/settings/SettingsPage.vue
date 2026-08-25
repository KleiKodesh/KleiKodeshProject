<script setup lang="ts">
import { ref, watch, nextTick, onMounted } from 'vue'
import { useEventListener } from '@vueuse/core'
import { IconSearch20Regular, IconNavigation20Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import {
  getSettingsScrollTop,
  setSettingsScrollTop,
  getSettingsSearchQuery,
  setSettingsSearchQuery,
} from './settingsViewMemory'
import { useSettingsSearch } from './useSettingsSearch'
import SettingsPageSideNav from './SettingsPageSideNav.vue'
import SettingsPageThemeAndApplicationSection from './SettingsPageThemeAndApplicationSection.vue'
import SettingsPageBookAndCommentaryDisplaySection from './SettingsPageBookAndCommentaryDisplaySection.vue'
import SettingsPageCensorDivineNamesSection from './SettingsPageCensorDivineNamesSection.vue'
import SettingsPageCalendarSection from './SettingsPageCalendarSection.vue'
import SettingsPageAdvancedSection from './SettingsPageAdvancedSection.vue'
import SettingsPageResetSection from './SettingsPageResetSection.vue'
import SettingsPageKeyboardShortcutsSection from './SettingsPageKeyboardShortcutsSection.vue'

// scrollContainerRef is the full-width body — scrollbar lives at the page edge
const scrollContainerRef = ref<HTMLElement | null>(null)
const { searchQuery, getSectionNavEntries } = useSettingsSearch(scrollContainerRef)

// Restored here rather than in onMounted so the input renders with the query on the
// first paint — assigning it later shows an empty box that then fills in.
searchQuery.value = getSettingsSearchQuery()

// ── Side nav (wide screen) ────────────────────────────────────────────────────

const sideNavEntries = ref<{ id: string; label: string }[]>([])

// ── Nav dropdown (narrow screen) ─────────────────────────────────────────────

const navPanelOpen = ref(false)
const navToggleRef = ref<HTMLElement | null>(null)
const navPanelRef = ref<HTMLElement | null>(null)
const navEntries = ref<{ id: string; label: string }[]>([])

const { justClosed } = useDropdownClose(navPanelRef, () => { navPanelOpen.value = false }, {
  toggleButton: navToggleRef,
})

// ── View state (this session only) ───────────────────────────────────────────
// The settings VALUES live in the settings store, so a tab switch loses only the
// view: the search query and the scroll position. Both are saved here, and the
// restore below applies them in that order — the query decides which sections are
// visible, so the offset is only meaningful once the filter is in place.
// Storing to a module variable needs no debounce the way an IndexedDB write would.

// True only while the restore below assigns scrollTop. Assigning it fires a scroll
// event, and the browser CLAMPS the assignment when the page is currently shorter
// than the target (hidden sections use `display: none`, so a filtered page is much
// shorter). Without this guard the listener would write that clamped value back and
// the deeper position would be lost for good — the saved value must survive a target
// that is briefly out of reach.
let restoringScroll = false

useEventListener(scrollContainerRef, 'scroll', () => {
  if (restoringScroll) return
  const el = scrollContainerRef.value
  if (el) setSettingsScrollTop(el.scrollTop)
})

watch(searchQuery, (query) => {
  setSettingsSearchQuery(query)
  // A new query re-flows the page, so the offset saved under the old one no longer
  // points anywhere meaningful. Drop it and let the scroll listener refill it —
  // filtering jumps you to the top, which is where the user now is.
  setSettingsScrollTop(0)
})

onMounted(() => {
  nextTick(() => {
    navEntries.value = getSectionNavEntries()
    sideNavEntries.value = getSectionNavEntries()

    // The filter is already applied by this point: assigning searchQuery at setup IS
    // a change, and useSettingsSearch registered its watcher before that assignment,
    // so it fired and its own nextTick ran ahead of this one. Nothing to re-apply.

    // Restored last, because hidden sections shorten the page and the offset only
    // means anything once the filter is in place.
    const saved = getSettingsScrollTop()
    const el = scrollContainerRef.value
    if (saved > 0 && el) {
      restoringScroll = true
      el.scrollTop = saved
      // Released after the scroll event this assignment queued has been dispatched,
      // so a clamped result cannot overwrite the saved offset.
      requestAnimationFrame(() => { restoringScroll = false })
    }
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
      :entries="sideNavEntries"
      @navigate="navigateToSection"
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
        <SettingsPageBookAndCommentaryDisplaySection />
        <SettingsPageCensorDivineNamesSection />
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

/* One heading level, one rule: the card header underline. A group of settings
   that needs its own heading gets its own card instead of a heading nested
   inside one — so no two rules can ever meet. */
.section-label {
  font-size: 15px;
  font-weight: 600;
  color: var(--text-primary);
  padding-top: 14px;
  padding-bottom: 10px;
  margin-bottom: 24px;
  border-bottom: 1px solid var(--border-color);
  scroll-margin-top: 56px;
}

[data-section-hidden] {
  display: none !important;
}

/* ── Row-style settings: rows are separated by spacing alone. The label sits
   above its control on its own line, and the gap between rows is wider than
   the gap inside one — that pairing is what groups them, so no borders. ── */
[data-section] .setting-row-item {
  margin-bottom: 28px;
  padding: 0;
}

/* Each row component ships its own default label type (see SettingRow.vue et
   al) so it renders correctly outside this page — e.g. in the setup wizard,
   which mounts from App.vue with no [data-section] ancestor. Here we override
   it for the settings page's larger row style. */
[data-section] .setting-row-item .setting-label {
  font-size: 13.5px;
  color: var(--text-primary);
}
</style>
