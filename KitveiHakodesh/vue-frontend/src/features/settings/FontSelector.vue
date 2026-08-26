<script setup lang="ts">
import { ref, computed, nextTick } from 'vue'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { IconChevronDown20Regular, IconChevronUp20Regular } from '@iconify-prerendered/vue-fluent'
import { detectAvailableFonts } from '@/webview-host/fontsApi'
import HintIcon from '@/components/HintIcon.vue'

const props = defineProps<{
  label: string
  modelValue: string
  fontType: 'sans-serif' | 'serif'
  hint?: string
  /**
   * Family this picker's setting falls back to, badged in the list. One per dropdown:
   * the book text and the te'amim text have different defaults, and the header picker
   * has none worth badging. Bare family name, no fallback chain.
   */
  defaultFont?: string
}>()
const emit = defineEmits<{ 'update:modelValue': [string]; toggle: [] }>()

const boxRef = ref<HTMLElement | null>(null)
const dropdownRef = ref<HTMLElement | null>(null)
const isOpen = ref(false)
const dropdownStyle = ref<Record<string, string>>({})

// Loaded live on every open — nothing is cached, so the list always reflects the
// machine's current fonts. isLoading shows the loading row until the answer lands;
// the generation counter keeps a stale in-flight load from finishing a newer open.
const availableFonts = ref<string[]>([])
const isLoading = ref(false)
let loadGeneration = 0

useDropdownClose(
  dropdownRef,
  (e) => {
    if (boxRef.value?.contains((e as MouseEvent).target as Node)) return
    isOpen.value = false
  },
  { ignore: [boxRef] },
)

async function toggle() {
  if (isOpen.value) {
    isOpen.value = false
    return
  }
  isOpen.value = true
  emit('toggle')
  // Enumeration starts BEFORE positioning. The early return below only means "cannot
  // measure yet"; when it also skipped the load, the dropdown stayed open showing an empty
  // list with no loading row, and only a close-and-reopen ever populated it.
  void loadFonts()
  await nextTick()
  if (!boxRef.value || !dropdownRef.value) return
  const rect = boxRef.value.getBoundingClientRect()
  const spaceBelow = window.innerHeight - rect.bottom - 8
  const spaceAbove = rect.top - 8
  const goUp = spaceAbove > spaceBelow
  const maxH = Math.min(200, goUp ? spaceAbove : spaceBelow)
  dropdownRef.value.style.maxHeight = maxH + 'px'
  dropdownStyle.value = {
    position: 'fixed',
    left: rect.left + 'px',
    width: rect.width + 'px',
    zIndex: '10000',
    ...(goUp
      ? { bottom: window.innerHeight - rect.top + 4 + 'px', top: 'auto' }
      : { top: rect.bottom + 4 + 'px', bottom: 'auto' }),
  }
}

async function loadFonts() {
  const generation = ++loadGeneration
  isLoading.value = true
  availableFonts.value = []
  try {
    const fonts = await detectAvailableFonts()
    if (generation === loadGeneration) availableFonts.value = fonts
  } finally {
    if (generation === loadGeneration) isLoading.value = false
  }
}

function select(font: string) {
  emit('update:modelValue', `'${font}', ${props.fontType}`)
  isOpen.value = false
}

const displayName = computed(() => props.modelValue.match(/'([^']+)'/)?.[1] ?? props.modelValue)

defineExpose({ isOpen })
</script>

<template>
  <div class="setting-row setting-row-item">
    <label class="setting-label">{{ label }}<HintIcon v-if="hint" :hint="hint" /></label>
    <div ref="boxRef" class="select-box" @click="toggle" tabindex="0">
      <span class="select-display">{{ displayName }}</span>
      <span class="select-preview" :style="{ fontFamily: modelValue }">אבג דהו</span>
      <component
        :is="isOpen ? IconChevronUp20Regular : IconChevronDown20Regular"
        class="select-chevron"
      />
    </div>
    <Teleport to="body">
      <div
        v-if="isOpen"
        ref="dropdownRef"
        class="select-dropdown"
        :style="dropdownStyle"
        @click.stop
      >
        <div v-if="isLoading" class="select-loading">…</div>
        <div
          v-for="font in availableFonts"
          :key="font"
          class="select-option"
          :class="{ selected: modelValue.includes(font) }"
          @click="select(font)"
        >
          <span class="opt-name">{{ font }}</span>
          <span v-if="font === defaultFont" class="opt-badge">ברירת מחדל</span>
          <span class="opt-preview" :style="{ fontFamily: `'${font}'` }">אבג דהו</span>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.setting-row {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-bottom: 10px;
}
/* Zero-specificity default so the row is self-sufficient outside the settings
   page (setup wizard); SettingsPage.vue's [data-section] rule overrides it. */
:where(.setting-label) {
  font-size: 11px;
  color: var(--text-secondary);
}
.setting-label {
  display: flex;
  align-items: center;
  gap: 4px;
}

.select-box {
  display: flex;
  align-items: center;
  width: 100%;
  height: 28px;
  padding: 0 8px;
  cursor: pointer;
  user-select: none;
  box-sizing: border-box;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
}
.select-box:hover {
  border-color: var(--accent-color);
}

.select-display {
  flex: 1;
  min-width: 0;
  font-size: 12px;
  color: var(--text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.select-preview {
  font-size: 13px;
  color: var(--text-primary);
  padding-inline-end: 6px;
  flex-shrink: 0;
}
.select-chevron {
  color: var(--text-secondary);
  flex-shrink: 0;
}
</style>

<style>
.select-dropdown {
  overflow-y: auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.select-loading {
  padding: 0 10px;
  height: 32px;
  line-height: 32px;
  text-align: center;
  color: var(--text-secondary);
}
.select-option {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0 10px;
  height: 32px;
  cursor: pointer;
  color: var(--text-primary);
  gap: 8px;
}
.opt-name {
  flex-shrink: 0;
  font-size: 12px;
  color: var(--text-secondary);
}
/* Marks the one family this picker defaults to — see the defaultFont prop. Pushed up
   against the name (the preview keeps the far edge via the row's space-between). */
.opt-badge {
  flex-shrink: 0;
  margin-inline-end: auto;
  font-size: 10px;
  line-height: 1;
  padding: 2px 5px;
  border-radius: 3px;
  /* Solid accent, not --accent-bg: the selected row already uses --accent-bg as its
     own background, and the default row is the one most likely to be selected — a
     tinted badge would all but vanish exactly where it matters most. */
  background: var(--accent-color);
  color: var(--bg-primary);
}
.opt-preview {
  font-size: 13px;
}
.select-option:hover {
  background: var(--hover-bg);
}
.select-option.selected {
  background: var(--accent-bg);
  color: var(--accent-color);
  font-weight: 500;
}
.select-option.selected .opt-name {
  color: var(--accent-color);
}
</style>
