<template>
  <div class="shortcuts-list">
    <!-- Tab management. The whole group exists only where a native tab strip does:
         everywhere else the app is single-tab, so every key in it would be a no-op. -->
    <template v-if="hasNativeChromeTabs">
      <div class="shortcuts-group-label">{{ $t('settings.shortcutsList.tabManagement') }}</div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>T</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.newTab') }}</span></div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Shift</kbd><span class="kbd-plus">+</span><kbd>T</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.openTabList') }}</span></div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Tab</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.holdCtrlAndCycleThroughTabs') }}</span></div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Shift</kbd><span class="kbd-plus">+</span><kbd>Tab</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.cycleInReverse') }}</span></div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>W</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.closeTab') }}</span></div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>X</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.closeAllTabs') }}</span></div>
    </template>
    <!-- Navigation -->
    <div class="shortcuts-group-label">{{ $t('settings.shortcutsList.navigation') }}</div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>E</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.focusTheSearchBar') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Alt</kbd><span class="kbd-plus">+</span><kbd>{{ $t('settings.shortcutsList.rightArrow') }}</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.goBackInHistory') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Alt</kbd><span class="kbd-plus">+</span><kbd>{{ $t('settings.shortcutsList.leftArrow') }}</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.goForwardInHistory') }}</span></div>
    <!-- Single-tab hosts: Ctrl+Tab walks the open document's own history instead of
         switching tabs, so it belongs with navigation rather than tab management. -->
    <template v-if="!hasNativeChromeTabs">
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Tab</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.goBackInHistory') }}</span></div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Shift</kbd><span class="kbd-plus">+</span><kbd>Tab</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.goForwardInHistory') }}</span></div>
    </template>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>G</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.goToHomePage') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>M</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.openMainMenu') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Shift</kbd><span class="kbd-plus">+</span><kbd>M</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.showHideSidebar') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>F8</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.openWorkspaces') }}</span></div>
    <!-- Inside the picker: the rows and the new-name box are one up/down walk, so these
         are listed with it rather than as a group of their own. -->
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>F2</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.renameWorkspaceInList') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Delete</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.deleteWorkspaceInList') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>L</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.toggleTheme') }}</span></div>
    <!-- Quick navigation -->
    <div class="shortcuts-group-label">{{ $t('settings.shortcutsList.quickNavigation') }}</div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>1</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.bookCatalog') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>2</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.search') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>3</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.hebrewbooks') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>4</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.openFile') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>5</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.fileSearch') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>6</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.dictionary') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>7</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.calendar') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>8</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.measuresAndQuantities') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>9</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.hameshiv') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>F1</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.settings') }}</span></div>
    <!-- Book view -->
    <div class="shortcuts-group-label">{{ $t('settings.shortcutsList.bookView') }}</div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>J</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.showHideCommentariesBelow') }}</span></div>
    <!-- The side commentary columns need a wide enough pane (BookViewPage's 650px
         isWideScreen gate) — below it both keys return silently, so don't list them.
         Measured on the window here; the real gate is the book pane's own width. -->
    <template v-if="hasRoomForSideCommentary">
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Shift</kbd><span class="kbd-plus">+</span><kbd>J</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.showHideCommentariesOnTheRight') }}</span></div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Alt</kbd><span class="kbd-plus">+</span><kbd>J</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.showHideCommentariesOnTheLeft') }}</span></div>
    </template>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>K</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.showHideTableOfContents') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>F</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.search') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>{{ $t('settings.shortcutsList.leftArrow') }}</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.nextSection') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>{{ $t('settings.shortcutsList.rightArrow') }}</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.previousSection') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>U</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.showHideCommentaryMarkers') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>D</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.removeRestoreVowelsAndCantillation') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Shift</kbd><span class="kbd-plus">+</span><kbd>D</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.createBookmark') }}</span></div>
    <!-- Full-text search results -->
    <div class="shortcuts-group-label">{{ $t('settings.shortcutsList.searchResults') }}</div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>↑</kbd><span class="kbd-plus">/</span><kbd>↓</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.moveBetweenResults') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Enter</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.openResult') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Enter</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.openResultInNewTab') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Space</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.toggleResultPreview') }}</span></div>
    <!-- Display -->
    <div class="shortcuts-group-label">{{ $t('settings.shortcutsList.view') }}</div>
    <div class="shortcut-row" v-if="!isVstoEnvironment"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>|</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.splitView') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>B</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.showHideToolbar') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>+</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.zoomIn') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>-</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.zoomOut') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>0</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.resetZoom') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>H</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.showHideAppBar') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Shift</kbd><span class="kbd-plus">+</span><kbd>H</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.showHideScrollbars') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>F9</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.readingModeHideShowAllBarsAndScrollbars') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>F11</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.fullScreen') }}</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>F7</kbd></div><span class="shortcut-description">{{ $t('settings.shortcutsList.caretBrowsingForNavigationAndSelection') }}</span></div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useWindowSize } from '@vueuse/core'
import { isVstoEnvironment, hasNativeChromeTabs } from '@/webview-host/bridge'

// Mirrors BookViewPage's isWideScreen threshold, the gate the side commentary
// shortcuts actually hit. That one measures the book pane; this measures the
// window, which is the same thing outside split view and close enough inside it.
const SIDE_COMMENTARY_MIN_WIDTH = 650
const { width: windowWidth } = useWindowSize()
const hasRoomForSideCommentary = computed(() => windowWidth.value >= SIDE_COMMENTARY_MIN_WIDTH)
</script>

<style scoped>
.shortcuts-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.shortcuts-group-label {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 10px 0 4px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 60%, transparent);
  margin-bottom: 4px;
}

.shortcuts-group-label:first-child {
  padding-top: 0;
}

.shortcut-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  min-height: 30px;
  padding: 0 4px;
  border-radius: 4px;
}

.shortcut-keys {
  display: flex;
  align-items: center;
  gap: 3px;
  flex-shrink: 0;
}

.shortcut-description {
  font-size: 13px;
  color: var(--text-primary);
  text-align: start;
  flex: 1;
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
  background: var(--bg-secondary);
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
</style>
