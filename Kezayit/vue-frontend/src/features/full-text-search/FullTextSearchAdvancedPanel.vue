<script setup lang="ts">
import { IconDismiss20Regular, IconInfo20Regular } from '@iconify-prerendered/vue-fluent'

const props = defineProps<{
  maxWordDistance: number
  requireOrdered: boolean
  showSyntaxHelp: boolean
}>()

const emit = defineEmits<{
  'update:maxWordDistance': [number]
  'update:requireOrdered': [boolean]
  'update:showSyntaxHelp': [boolean]
  close: []
}>()

function onDistanceInput(event: Event) {
  const value = parseInt((event.target as HTMLInputElement).value, 10)
  if (!isNaN(value) && value >= 0) emit('update:maxWordDistance', value)
}
</script>

<template>
  <div class="advanced-panel" @keydown.esc.stop="emit('close')">
    <div class="panel-header">
      <span class="panel-title">אפשרויות חיפוש מתקדמות</span>
      <button class="close-btn" title="סגור" @click="emit('close')">
        <IconDismiss20Regular />
      </button>
    </div>

    <div class="panel-body">
      <!-- Word distance -->
      <div class="option-row">
        <label class="option-label" for="word-distance-input">מרחק מקסימלי בין מילים</label>
        <input
          id="word-distance-input"
          type="number"
          class="distance-input"
          :value="props.maxWordDistance"
          min="0"
          max="9999"
          @input="onDistanceInput"
        />
      </div>

      <!-- Order mode -->
      <div class="option-row">
        <span class="option-label">סדר מילים</span>
        <div class="toggle-group">
          <button
            class="toggle-btn"
            :class="{ active: !props.requireOrdered }"
            @click="emit('update:requireOrdered', false)"
          >לא מסודר</button>
          <button
            class="toggle-btn"
            :class="{ active: props.requireOrdered }"
            @click="emit('update:requireOrdered', true)"
          >לפי סדר השאילתה</button>
        </div>
      </div>

      <!-- Syntax help toggle -->
      <div class="option-row syntax-row">
        <button
          class="syntax-btn"
          :class="{ active: props.showSyntaxHelp }"
          @click="emit('update:showSyntaxHelp', !props.showSyntaxHelp)"
        >
          <IconInfo20Regular />
          <span>תחביר חיפוש</span>
        </button>
      </div>
    </div>

    <!-- Syntax help content — scrollable, outside panel-body -->
    <div v-if="props.showSyntaxHelp" class="syntax-help">
        <table class="syntax-table">
          <thead>
            <tr>
              <th>תבנית</th>
              <th>משמעות ודוגמה</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td class="pattern">מילה</td>
              <td>חיפוש מדויק — כל המילים חייבות להופיע בשורה.<br><span class="example">משה תורה</span></td>
            </tr>
            <tr>
              <td class="pattern">מילה*</td>
              <td>כוכבית — אפס תווים או יותר.<br><span class="example">ישר*</span> ← ישראל, ישרים… &nbsp; <span class="example">*לום</span> ← שלום, עולם…</td>
            </tr>
            <tr>
              <td class="pattern">מיל?ה</td>
              <td>שאלתית — התו שלפניה אופציונלי.<br><span class="example">שלו?ם</span> ← שלום או שלם</td>
            </tr>
            <tr>
              <td class="pattern">מילה~</td>
              <td>טילדה — חיפוש מטושטש, מרחק עריכה 1.<br><span class="example">יצחק~</span> ← יצחק, ביצחק, ליצחק…</td>
            </tr>
            <tr>
              <td class="pattern">מילה~2 / מילה~3</td>
              <td>טילדה עם מספר — מרחק עריכה מותאם (1–3).<br><span class="example">משה~2</span> ← משה, למשה, ממשה…</td>
            </tr>
            <tr>
              <td class="pattern">א | ב</td>
              <td>מקף אנכי — OR: מספיק שאחת מהמילים תופיע.<br><span class="example">משה | אהרן תורה</span> ← (משה או אהרן) וגם תורה</td>
            </tr>
          </tbody>
        </table>
        <div class="syntax-notes">
          <p><strong>שילוב מילים:</strong> כל המילים מחוברות ב-AND — כולן חייבות להופיע באותה שורה.</p>
          <p><strong>OR (|):</strong> המקף האנכי חייב להיות מופרד ברווחים משני הצדדים.</p>
          <p><strong>ניקוד:</strong> מתעלם מניקוד — ניתן לחפש עם ניקוד או בלעדיו.</p>
          <p><strong>כוכבית ושאלתית:</strong> ניתן לשלב באותה מילה, למשל <span class="example">שלו?ם*</span>.</p>
          <p><strong>טילדה + כוכבית/שאלתית:</strong> לא ניתן לשלב — הכוכבית/שאלתית גוברת.</p>
        </div>
      </div>
  </div>
</template>

<style scoped>
.advanced-panel {
  display: flex;
  flex-direction: column;
  background: var(--bg-secondary);
  border-top: 1px solid var(--border-color);
  flex-shrink: 0;
  max-height: 70vh;
}
.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 6px 12px 6px 8px;
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
  background: var(--bg-secondary);
}
.panel-title {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  border-radius: 4px;
  color: var(--text-secondary);
}
.panel-body {
  padding: 8px 12px 10px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  flex-shrink: 0;
}
.option-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  min-height: 26px;
}
.option-label {
  font-size: 12px;
  color: var(--text-primary);
  flex-shrink: 0;
}
.distance-input {
  width: 56px;
  height: 24px;
  padding: 0 6px;
  border-radius: 4px;
  border: 1px solid var(--border-color);
  background: var(--input-bg);
  color: var(--text-primary);
  font-size: 12px;
  text-align: center;
  direction: ltr;
}
.distance-input:focus {
  outline: none;
  border-color: var(--accent-color);
}
.toggle-group {
  display: flex;
  border-radius: 4px;
  overflow: hidden;
  border: 1px solid var(--border-color);
}
.toggle-btn {
  padding: 0 8px;
  height: 24px;
  font-size: 11px;
  color: var(--text-secondary);
  background: var(--input-bg);
  border-radius: 0;
  border: none;
  border-right: 1px solid var(--border-color);
  white-space: nowrap;
}
.toggle-btn:last-child {
  border-right: none;
}
.toggle-btn.active {
  background: var(--accent-color);
  color: #fff;
}
.syntax-row {
  justify-content: flex-start;
}
.syntax-btn {
  display: flex;
  align-items: center;
  gap: 5px;
  height: 24px;
  padding: 0 8px;
  border-radius: 4px;
  border: 1px solid var(--border-color);
  background: var(--input-bg);
  font-size: 12px;
  color: var(--text-secondary);
}
.syntax-btn.active {
  color: var(--accent-color);
  border-color: var(--accent-color);
}
.syntax-help {
  border-top: 1px solid var(--border-color);
  padding: 8px 12px 10px;
  margin: 0;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.syntax-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 11px;
  direction: rtl;
}
.syntax-table th {
  text-align: right;
  padding: 4px 6px;
  font-weight: 600;
  color: var(--text-secondary);
  border-bottom: 1px solid var(--border-color);
  background: color-mix(in srgb, var(--text-primary) 4%, transparent);
}
.syntax-table td {
  padding: 5px 6px;
  vertical-align: top;
  color: var(--text-primary);
  font-size: 11px;
  line-height: 1.5;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
}
.syntax-table tr:last-child td {
  border-bottom: none;
}
.pattern {
  font-family: 'Consolas', 'Courier New', monospace;
  color: var(--accent-color);
  white-space: nowrap;
  width: 130px;
}
.example {
  font-family: 'Consolas', 'Courier New', monospace;
  color: var(--accent-color);
  font-size: 11px;
}
.syntax-notes {
  margin-top: 8px;
  display: flex;
  flex-direction: column;
  gap: 3px;
}
.syntax-notes p {
  font-size: 11px;
  color: var(--text-secondary);
  line-height: 1.5;
  margin: 0;
}
</style>
