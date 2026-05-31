<script setup lang="ts">
import { ref } from 'vue'
import { IconDismiss20Regular } from '@iconify-prerendered/vue-fluent'

const props = defineProps<{
  maxWordDistance: number
  requireOrdered: boolean
  contextWords: number
  expandKetiv: boolean
  wildcardWrap: boolean
  grammarWrap: boolean
}>()

const emit = defineEmits<{
  'update:maxWordDistance': [number]
  'update:requireOrdered': [boolean]
  'update:contextWords': [number]
  'update:expandKetiv': [boolean]
  'update:wildcardWrap': [boolean]
  'update:grammarWrap': [boolean]
  close: []
}>()

type ActiveTab = 'options' | 'syntax'
const activeTab = ref<ActiveTab>('options')

function onDistanceInput(event: Event) {
  const value = parseInt((event.target as HTMLInputElement).value, 10)
  if (!isNaN(value) && value >= 0) emit('update:maxWordDistance', value)
}

function onContextWordsInput(event: Event) {
  const value = parseInt((event.target as HTMLInputElement).value, 10)
  if (!isNaN(value) && value >= 0) emit('update:contextWords', value)
}
</script>

<template>
  <div class="advanced-panel" @keydown.esc.stop="emit('close')">
    <div class="panel-header">
      <div class="tabs">
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'options' }"
          @click="activeTab = 'options'"
        >
          אפשרויות חיפוש מתקדמות
        </button>
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'syntax' }"
          @click="activeTab = 'syntax'"
        >
          תחביר חיפוש
        </button>
      </div>
      <button class="close-btn" title="סגור" @click="emit('close')">
        <IconDismiss20Regular />
      </button>
    </div>

    <div v-if="activeTab === 'options'" class="panel-body">
      <!-- Grammar wrap -->
      <div class="option-row">
        <span class="option-label">תחיליות וסופיות דקדוקיות</span>
        <div class="toggle-group">
          <button
            class="toggle-btn"
            :class="{ active: !props.grammarWrap }"
            @click="emit('update:grammarWrap', false)"
          >לא</button>
          <button
            class="toggle-btn"
            :class="{ active: props.grammarWrap }"
            @click="emit('update:grammarWrap', true)"
          >כן</button>
        </div>
      </div>

      <!-- Wildcard wrap -->
      <div class="option-row">
        <span class="option-label">תחיליות וסופיות</span>
        <div class="toggle-group">
          <button
            class="toggle-btn"
            :class="{ active: !props.wildcardWrap }"
            @click="emit('update:wildcardWrap', false)"
          >לא</button>
          <button
            class="toggle-btn"
            :class="{ active: props.wildcardWrap }"
            @click="emit('update:wildcardWrap', true)"
          >כן</button>
        </div>
      </div>

      <!-- כתיב expansion -->
      <div class="option-row">
        <span class="option-label">הרחב כתיב חסר/מלא</span>
        <div class="toggle-group">
          <button
            class="toggle-btn"
            :class="{ active: !props.expandKetiv }"
            @click="emit('update:expandKetiv', false)"
          >לא</button>
          <button
            class="toggle-btn"
            :class="{ active: props.expandKetiv }"
            @click="emit('update:expandKetiv', true)"
          >כן</button>
        </div>
      </div>

      <!-- Order mode -->
      <div class="option-row">
        <span class="option-label">סדר מילים לפי שאילתא</span>
        <div class="toggle-group">
          <button
            class="toggle-btn"
            :class="{ active: !props.requireOrdered }"
            @click="emit('update:requireOrdered', false)"
          >לא</button>
          <button
            class="toggle-btn"
            :class="{ active: props.requireOrdered }"
            @click="emit('update:requireOrdered', true)"
          >כן</button>
        </div>
      </div>

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

      <!-- Context words -->
      <div class="option-row">
        <label class="option-label" for="context-words-input">הקשר לפני ואחרי (מילים)</label>
        <input
          id="context-words-input"
          type="number"
          class="distance-input"
          :value="props.contextWords"
          min="0"
          max="9999"
          @input="onContextWordsInput"
        />
      </div>
    </div>

    <div v-else-if="activeTab === 'syntax'" class="syntax-help">
      <div class="syntax-reference">
        <div class="syntax-row">
          <div class="intent">תחיליות וסופיות</div>
          <div class="usage"><span class="example">ישר*</span> <span class="example">*לום</span></div>
        </div>
        <div class="syntax-row">
          <div class="intent">תו אופציונלי (כתיב חסר/מלא)</div>
          <div class="usage"><span class="example">שלו?ם</span></div>
        </div>
        <div class="syntax-row">
          <div class="intent">דומה עם שגיאה (מרחק 1)</div>
          <div class="usage"><span class="example">יצחק~</span></div>
        </div>
        <div class="syntax-row">
          <div class="intent">דומה עם שגיאה מותאמת (1–3)</div>
          <div class="usage"><span class="example">משה~2</span> <span class="example">משה~3</span></div>
        </div>
        <div class="syntax-row">
          <div class="intent">או (בחירה אחת או יותר)</div>
          <div class="usage"><span class="example">משה | אהרן</span></div>
        </div>
        <div class="syntax-row">
          <div class="intent">קידומות/סיומות דקדוקיות</div>
          <div class="usage"><span class="example">%שלום%</span> <span class="example">%שלום</span> <span class="example">שלום%</span></div>
        </div>
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
  max-height: 60%;
  min-height: 0;
}
.panel-header {
  display: flex;
  align-items: stretch;
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
  background: var(--bg-secondary);
  height: 30px;
}
.tabs {
  display: flex;
  flex: 1;
  align-items: stretch;
  overflow: hidden;
}
.tab-btn {
  display: flex;
  align-items: center;
  padding: 0 12px;
  font-size: 11px;
  font-weight: 500;
  color: var(--text-secondary);
  border-bottom: 2px solid transparent;
  border-radius: 0;
  white-space: nowrap;
  transition: color 100ms, border-color 100ms;
  margin-bottom: -1px;
}
.tab-btn:hover:not(.active) {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 5%, transparent);
}
.tab-btn.active {
  color: var(--accent-color);
  border-bottom-color: var(--accent-color);
  background: none;
}
.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  flex-shrink: 0;
  border-radius: 0;
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
  gap: 4px;
}
.toggle-btn {
  flex: 1;
  height: 24px;
  padding: 0 10px;
  border: 1px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  font-size: 11px;
  white-space: nowrap;
  border-radius: 4px;
  transition: all 100ms ease;
}
.toggle-btn:hover:not(.active) {
  background: color-mix(in srgb, var(--text-primary) 6%, var(--bg-secondary));
}
.toggle-btn.active {
  background: var(--accent-color);
  color: #fff;
  border-color: var(--accent-color);
}
.syntax-help {
  padding: 8px 12px 10px;
  overflow-y: auto;
  flex: 1;
  min-height: 0;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.syntax-reference {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.syntax-row {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 6px 8px;
  border-radius: 4px;
  background: color-mix(in srgb, var(--text-primary) 2%, transparent);
  border: 1px solid color-mix(in srgb, var(--border-color) 60%, transparent);
}
.intent {
  flex: 0 0 auto;
  min-width: 140px;
  font-size: 11px;
  font-weight: 500;
  color: var(--text-primary);
  line-height: 1.4;
}
.usage {
  flex: 1;
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
}
.example {
  font-family: 'Consolas', 'Courier New', monospace;
  color: var(--accent-color);
  font-size: 10px;
  background: color-mix(in srgb, var(--accent-color) 8%, transparent);
  padding: 2px 4px;
  border-radius: 3px;
  white-space: nowrap;
}

</style>
