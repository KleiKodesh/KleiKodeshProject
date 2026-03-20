<script setup lang="ts">
import { ref, computed, nextTick } from 'vue'
import { onClickOutside } from '@vueuse/core'
import { IconChevronDown20Regular, IconChevronUp20Regular } from '@iconify-prerendered/vue-fluent'

const props = defineProps<{
  label: string
  modelValue: string
  availableFonts: string[]
  fontType: 'sans-serif' | 'serif'
}>()
const emit = defineEmits<{ 'update:modelValue': [string]; toggle: [] }>()

const boxRef = ref<HTMLElement | null>(null)
const dropdownRef = ref<HTMLElement | null>(null)
const isOpen = ref(false)
const dropdownStyle = ref<Record<string, string>>({})

onClickOutside(dropdownRef, (e) => {
  if (boxRef.value?.contains(e.target as Node)) return
  isOpen.value = false
})

async function toggle() {
  if (isOpen.value) { isOpen.value = false; return }
  isOpen.value = true
  emit('toggle')
  await nextTick()
  if (!boxRef.value || !dropdownRef.value) return
  const rect = boxRef.value.getBoundingClientRect()
  const dropH = dropdownRef.value.offsetHeight
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
      ? { bottom: (window.innerHeight - rect.top + 4) + 'px', top: 'auto' }
      : { top: (rect.bottom + 4) + 'px', bottom: 'auto' }),
  }
}

function select(font: string) { emit('update:modelValue', `'${font}', ${props.fontType}`); isOpen.value = false }

const displayName = computed(() => props.modelValue.match(/'([^']+)'/)?.[1] ?? props.modelValue)

defineExpose({ isOpen })
</script>

<template>
  <div class="setting-row">
    <label class="setting-label">{{ label }}</label>
    <div ref="boxRef" class="select-box" @click="toggle" tabindex="0">
      <span class="select-display">{{ displayName }}</span>
      <span class="select-preview" :style="{ fontFamily: modelValue }">אבג דהו</span>
      <component :is="isOpen ? IconChevronUp20Regular : IconChevronDown20Regular" class="select-chevron" />
    </div>
    <Teleport to="body">
      <div v-if="isOpen" ref="dropdownRef" class="select-dropdown" :style="dropdownStyle" @click.stop>
        <div v-for="font in availableFonts" :key="font"
          class="select-option" :class="{ selected: modelValue.includes(font) }"
          @click="select(font)">
          <span class="opt-name">{{ font }}</span>
          <span class="opt-preview" :style="{ fontFamily: `'${font}'` }">אבג דהו</span>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.setting-row { display: flex; flex-direction: column; gap: 4px; margin-bottom: 10px; }
.setting-label { font-size: 11px; color: var(--text-secondary); }

.select-box {
  flex: 1; position: relative; display: flex; align-items: center;
  height: 28px; padding: 0 8px; cursor: pointer; user-select: none;
  background: var(--bg-secondary); border: 1px solid var(--border-color); border-radius: 4px;
}
.select-box:hover { border-color: var(--accent-color); }

.select-display { flex: 1; font-size: 11px; color: var(--text-secondary); }
.select-preview { font-size: 14px; color: var(--text-primary); padding-inline-end: 6px; }
.select-chevron { color: var(--text-secondary); flex-shrink: 0; }
</style>

<style>
.select-dropdown {
  overflow-y: auto;
  background: var(--bg-secondary); border: 1px solid var(--border-color); border-radius: 4px;
}
.select-option { display: flex; justify-content: space-between; align-items: baseline; padding: 6px 10px; cursor: pointer; color: var(--text-primary); gap: 8px; }
.opt-name { flex-shrink: 0; font-size: 11px; color: var(--text-secondary); }
.opt-preview { font-size: 14px; }
.select-option:hover { background: var(--hover-bg); }
.select-option.selected { background: var(--accent-bg); color: var(--accent-color); font-weight: 500; }
</style>
