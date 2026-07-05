<template>
  <div data-section="section-shortcuts" data-section-label="קיצורי מקשים">
    <div id="section-shortcuts" class="section-label">קיצורי מקשים</div>
    <div class="shortcuts-grid">
      <!-- Tab management -->
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>N</kbd><span class="shortcut-description">לשונית חדשה</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>T</kbd><span class="shortcut-description">פתח רשימת לשוניות</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Tab</kbd><span class="shortcut-description">לשונית הבאה</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>Shift</kbd><span class="kbd-plus">+</span><kbd>Tab</kbd><span class="shortcut-description">לשונית הקודמת</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>W</kbd><span class="shortcut-description">סגור לשונית</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>X</kbd><span class="shortcut-description">סגור את כל הלשוניות</span></div>
      <!-- Navigation -->
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>G</kbd><span class="shortcut-description">עבור לדף הבית</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>M</kbd><span class="shortcut-description">פתח תפריט ראשי</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>L</kbd><span class="shortcut-description">החלף ערכת נושא</span></div>
      <!-- Quick navigation -->
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>1</kbd><span class="shortcut-description">ספרים</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>2</kbd><span class="shortcut-description">חיפוש</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>3</kbd><span class="shortcut-description">היברו-בוקס</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>4</kbd><span class="shortcut-description">פתח קובץ</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>5</kbd><span class="shortcut-description">חיפוש קבצים</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>6</kbd><span class="shortcut-description">מילון</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>7</kbd><span class="shortcut-description">לוח שנה</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>8</kbd><span class="shortcut-description">מידות ושיעורים</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>9</kbd><span class="shortcut-description">סביבות עבודה</span></div>
      <div class="shortcut-row"><kbd>F1</kbd><span class="shortcut-description">הגדרות</span></div>
      <!-- Book view controls -->
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>B</kbd><span class="shortcut-description">הצג / הסתר סרגל כלים (בתצוגת ספר)</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>J</kbd><span class="shortcut-description">הצג / הסתר מפרשים (בתצוגת ספר)</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>K</kbd><span class="shortcut-description">הצג / הסתר תוכן עניינים (בתצוגת ספר)</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>F</kbd><span class="shortcut-description">חיפוש (בתצוגת ספר)</span></div>
      <!-- Zoom controls -->
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>+</kbd><span class="shortcut-description">הגדל תצוגה</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>-</kbd><span class="shortcut-description">הקטן תצוגה</span></div>
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>0</kbd><span class="shortcut-description">אפס גודל תצוגה</span></div>
      <!-- Display modes -->
      <div class="shortcut-row"><kbd>Ctrl</kbd><span class="kbd-plus">+</span><kbd>H</kbd><span class="shortcut-description">הצג / הסתר סרגל האפליקציה</span></div>
      <div class="shortcut-row"><kbd>F11</kbd><span class="shortcut-description">מסך מלא</span></div>
      <div class="shortcut-row"><kbd>F7</kbd><span class="shortcut-description">הפעלת סמן טקסט לניווט ולבחירת טקסט באמצעות המקלדת</span></div>
    </div>
  </div>
</template>

<style scoped>
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

.shortcut-description {
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
</style>
