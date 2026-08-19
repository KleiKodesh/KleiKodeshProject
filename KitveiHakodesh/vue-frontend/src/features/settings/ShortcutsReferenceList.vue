<template>
  <div class="shortcuts-list">
    <!-- Tab management. The whole group exists only where a native tab strip does:
         everywhere else the app is single-tab, so every key in it would be a no-op. -->
    <template v-if="hasNativeChromeTabs">
      <div class="shortcuts-group-label">ניהול לשוניות</div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>N</kbd></div><span class="shortcut-description">לשונית חדשה</span></div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>T</kbd></div><span class="shortcut-description">פתח רשימת לשוניות</span></div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Tab</kbd></div><span class="shortcut-description">החזק Ctrl ודפדף בין הלשוניות</span></div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Shift</kbd><span class="kbd-plus">+</span><kbd>Tab</kbd></div><span class="shortcut-description">דפדוף בכיוון ההפוך</span></div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>W</kbd></div><span class="shortcut-description">סגור לשונית</span></div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>X</kbd></div><span class="shortcut-description">סגור את כל הלשוניות</span></div>
    </template>
    <!-- Navigation -->
    <div class="shortcuts-group-label">ניווט</div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>E</kbd></div><span class="shortcut-description">מיקוד שורת החיפוש</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Alt</kbd><span class="kbd-plus">+</span><kbd>חץ ימני</kbd></div><span class="shortcut-description">חזור אחורה בהיסטוריה</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Alt</kbd><span class="kbd-plus">+</span><kbd>חץ שמאלי</kbd></div><span class="shortcut-description">התקדם קדימה בהיסטוריה</span></div>
    <!-- Single-tab hosts: Ctrl+Tab walks the open document's own history instead of
         switching tabs, so it belongs with navigation rather than tab management. -->
    <template v-if="!hasNativeChromeTabs">
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Tab</kbd></div><span class="shortcut-description">חזור אחורה בהיסטוריה</span></div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Shift</kbd><span class="kbd-plus">+</span><kbd>Tab</kbd></div><span class="shortcut-description">התקדם קדימה בהיסטוריה</span></div>
    </template>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>G</kbd></div><span class="shortcut-description">עבור לדף הבית</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>M</kbd></div><span class="shortcut-description">פתח תפריט ראשי</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>L</kbd></div><span class="shortcut-description">החלף ערכת נושא</span></div>
    <!-- Quick navigation -->
    <div class="shortcuts-group-label">ניווט מהיר</div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>1</kbd></div><span class="shortcut-description">קטלוג הספרים</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>2</kbd></div><span class="shortcut-description">חיפוש</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>3</kbd></div><span class="shortcut-description">היברו-בוקס</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>4</kbd></div><span class="shortcut-description">פתח קובץ</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>5</kbd></div><span class="shortcut-description">חיפוש קבצים</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>6</kbd></div><span class="shortcut-description">מילון</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>7</kbd></div><span class="shortcut-description">לוח שנה</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>8</kbd></div><span class="shortcut-description">מידות ושיעורים</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>9</kbd></div><span class="shortcut-description">סביבות עבודה</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>F1</kbd></div><span class="shortcut-description">הגדרות</span></div>
    <!-- Book view -->
    <div class="shortcuts-group-label">תצוגת ספר</div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>J</kbd></div><span class="shortcut-description">הצג / הסתר מפרשים למטה</span></div>
    <!-- The side commentary columns need a wide enough pane (BookViewPage's 650px
         isWideScreen gate) — below it both keys return silently, so don't list them.
         Measured on the window here; the real gate is the book pane's own width. -->
    <template v-if="hasRoomForSideCommentary">
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Shift</kbd><span class="kbd-plus">+</span><kbd>J</kbd></div><span class="shortcut-description">הצג / הסתר מפרשים מימין</span></div>
      <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Alt</kbd><span class="kbd-plus">+</span><kbd>J</kbd></div><span class="shortcut-description">הצג / הסתר מפרשים משמאל</span></div>
    </template>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>K</kbd></div><span class="shortcut-description">הצג / הסתר תוכן עניינים</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>F</kbd></div><span class="shortcut-description">חיפוש</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>חץ שמאלי</kbd></div><span class="shortcut-description">קטע הבא</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>חץ ימני</kbd></div><span class="shortcut-description">קטע הקודם</span></div>
    <!-- Display -->
    <div class="shortcuts-group-label">תצוגה</div>
    <div class="shortcut-row" v-if="!isVstoEnvironment"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>|</kbd></div><span class="shortcut-description">תצוגה מפוצלת</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>B</kbd></div><span class="shortcut-description">הצג / הסתר סרגל כלים</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>+</kbd></div><span class="shortcut-description">הגדל תצוגה</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>-</kbd></div><span class="shortcut-description">הקטן תצוגה</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>0</kbd></div><span class="shortcut-description">אפס גודל תצוגה</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>H</kbd></div><span class="shortcut-description">הצג / הסתר סרגל האפליקציה</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Shift</kbd><span class="kbd-plus">+</span><kbd>H</kbd></div><span class="shortcut-description">פסי גלילה — קבועים / הסתרה אוטומטית</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>F9</kbd></div><span class="shortcut-description">מצב קריאה — הסתר / הצג את כל הסרגלים ופסי הגלילה</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>F11</kbd></div><span class="shortcut-description">מסך מלא</span></div>
    <div class="shortcut-row"><div class="shortcut-keys"><kbd>F7</kbd></div><span class="shortcut-description">הפעלת סמן טקסט לניווט ובחירה</span></div>
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
  text-align: right;
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
