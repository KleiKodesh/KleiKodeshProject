<script setup lang="ts">
import { ref, computed, nextTick } from 'vue'
import { onClickOutside } from '@vueuse/core'
import { IconChevronDown20Regular, IconChevronUp20Regular } from '@iconify-prerendered/vue-fluent'
import themesData from '@/theme/themes.json'
import { useThemeStore } from '@/theme/themeStore'
import { storeToRefs } from 'pinia'

const themeStore = useThemeStore()
const { themePreset } = storeToRefs(themeStore)

type ThemeKey = keyof typeof themesData

const families = Object.entries(
  Object.entries(themesData).reduce((acc, [key, theme]) => {
    const f = theme.family
    if (!acc[f]) acc[f] = []
    acc[f].push({ key: key as ThemeKey, theme })
    return acc
  }, {} as Record<string, { key: ThemeKey; theme: typeof themesData[ThemeKey] }[]>)
)

const currentTheme = computed(() => themesData[themePreset.value as ThemeKey])
const currentName = computed(() => currentTheme.value ? `${currentTheme.value.name} — ${currentTheme.value.isDark ? 'כהה' : 'בהיר'}` : themePreset.value)

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
  await nextTick()
  if (!boxRef.value || !dropdownRef.value) return
  const rect = boxRef.value.getBoundingClientRect()
  const dropH = dropdownRef.value.offsetHeight
  const spaceBelow = window.innerHeight - rect.bottom - 8
  const spaceAbove = rect.top - 8
  const goUp = spaceAbove > spaceBelow && spaceAbove > dropH
  dropdownStyle.value = {
    position: 'fixed',
    left: rect.left + 'px',
    width: rect.width + 'px',
    zIndex: '10000',
    maxHeight: Math.min(320, goUp ? spaceAbove : spaceBelow) + 'px',
    overflowY: 'auto',
    ...(goUp
      ? { bottom: (window.innerHeight - rect.top + 4) + 'px', top: 'auto' }
      : { top: (rect.bottom + 4) + 'px', bottom: 'auto' }),
  }
}

function select(key: ThemeKey) { themePreset.value = key; isOpen.value = false }
</script>

<template>
  <div ref="boxRef" class="select-box" @click="toggle" tabindex="0">
    <span class="swatch-inline" :style="{
      background: currentTheme?.ui.bgSecondary,
      borderColor: currentTheme?.ui.borderColor,
    }">
      <span class="si-bar" :style="{ background: currentTheme?.ui.bgPrimary }" />
      <span class="si-accent" :style="{ background: currentTheme?.ui.accentColor }" />
    </span>
    <span class="select-display">{{ currentName }}</span>
    <component :is="isOpen ? IconChevronUp20Regular : IconChevronDown20Regular" class="select-chevron" />
  </div>

  <Teleport to="body">
    <div v-if="isOpen" ref="dropdownRef" class="theme-dropdown" :style="dropdownStyle">
      <div v-for="([family, variants]) in families" :key="family" class="theme-family">
        <button
          v-for="{ key, theme } in variants"
          :key="key"
          class="theme-btn"
          :class="{ active: themePreset === key }"
          :title="theme.name + (theme.isDark ? ' — כהה' : ' — בהיר')"
          @click.stop="select(key)"
        >
          <span class="swatch" :style="{ background: theme.ui.bgSecondary, borderColor: theme.ui.borderColor }">
            <span class="swatch-bar" :style="{ background: theme.ui.bgPrimary }" />
            <span class="swatch-accent" :style="{ background: theme.ui.accentColor }" />
            <span class="swatch-text" :style="{ background: theme.ui.textPrimary }" />
          </span>
        </button>
        <span class="family-name">{{ variants[0]?.theme.name }}</span>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.select-box {
  flex: 1; display: flex; align-items: center; gap: 8px;
  height: 28px; padding: 0 8px; cursor: pointer; user-select: none;
  background: var(--bg-secondary); border: 1px solid var(--border-color); border-radius: 4px;
}
.select-box:hover { border-color: var(--accent-color); }
.select-display { flex: 1; font-size: 12px; color: var(--text-primary); }
.select-chevron { color: var(--text-secondary); flex-shrink: 0; }

.swatch-inline {
  display: flex; flex-direction: column; gap: 1px;
  width: 20px; height: 16px; border-radius: 2px; border: 1px solid;
  padding: 2px; overflow: hidden; flex-shrink: 0;
}
.si-bar { height: 4px; border-radius: 1px; opacity: 0.6; }
.si-accent { height: 4px; border-radius: 1px; }
</style>

<style>
.theme-dropdown {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  padding: 10px 12px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.theme-family {
  display: flex;
  align-items: center;
  gap: 8px;
}
.family-name {
  font-size: 12px;
  color: var(--text-secondary);
  min-width: 60px;
}
.theme-btn {
  padding: 4px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: none;
  cursor: pointer;
}
.theme-btn.active { border-color: var(--accent-color); }
.theme-btn:hover { border-color: var(--text-secondary); }
.swatch {
  display: flex; flex-direction: column; gap: 2px;
  width: 36px; height: 28px; border-radius: 2px; border: 1px solid;
  padding: 3px; overflow: hidden;
}
.swatch-bar  { height: 5px; border-radius: 1px; opacity: 0.7; }
.swatch-accent { height: 5px; border-radius: 1px; }
.swatch-text { height: 4px; border-radius: 1px; opacity: 0.5; }
</style>
