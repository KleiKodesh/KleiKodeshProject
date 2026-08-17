<script setup lang="ts">
import { ref, nextTick, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { IconChevronDown20Regular, IconChevronUp20Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import SettingRow from './SettingRow.vue'
import ToggleGroup from './ToggleGroup.vue'
import { useZmanim, CITIES } from '@/features/hebrew-calendar/useZmanim'
import { useSettingsStore } from '@/stores/settingsStore'

const { showClock } = storeToRefs(useSettingsStore())

const { activeCity, setCity, init: initZmanim } = useZmanim()
onMounted(() => initZmanim())

const cityBoxRef = ref<HTMLElement | null>(null)
const cityDropdownRef = ref<HTMLElement | null>(null)
const cityOpen = ref(false)
const cityDropdownStyle = ref<Record<string, string>>({})

useDropdownClose(
  cityDropdownRef,
  (e) => {
    if (cityBoxRef.value?.contains((e as MouseEvent).target as Node)) return
    cityOpen.value = false
  },
  { ignore: [cityBoxRef] },
)

async function toggleCityDropdown() {
  if (cityOpen.value) { cityOpen.value = false; return }
  cityOpen.value = true
  await nextTick()
  if (!cityBoxRef.value || !cityDropdownRef.value) return
  const rect = cityBoxRef.value.getBoundingClientRect()
  const spaceBelow = window.innerHeight - rect.bottom - 8
  const spaceAbove = rect.top - 8
  const goUp = spaceAbove > spaceBelow
  const maximumHeight = Math.min(240, goUp ? spaceAbove : spaceBelow)
  cityDropdownRef.value.style.maxHeight = maximumHeight + 'px'
  cityDropdownStyle.value = {
    position: 'fixed',
    left: rect.left + 'px',
    width: rect.width + 'px',
    zIndex: '10000',
    ...(goUp
      ? { bottom: window.innerHeight - rect.top + 4 + 'px', top: 'auto' }
      : { top: rect.bottom + 4 + 'px', bottom: 'auto' }),
  }
}

function pickCity(name: string) {
  setCity(CITIES.find((c) => c.name === name) ?? null)
  cityOpen.value = false
}
</script>

<template>
  <div data-section="section-calendar" data-section-label="לוח שנה ושעון">
    <div id="section-calendar" class="section-label">לוח שנה ושעון</div>

    <SettingRow label="עיר לזמני היום" hint="זמני היום בלוח השנה יחושבו לפי מיקום העיר">
      <div ref="cityBoxRef" class="city-select-box" tabindex="0" @click="toggleCityDropdown">
        <span class="city-select-display">{{ activeCity.name }}</span>
        <component
          :is="cityOpen ? IconChevronUp20Regular : IconChevronDown20Regular"
          class="city-select-chevron"
        />
      </div>
      <Teleport to="body">
        <div
          v-if="cityOpen"
          ref="cityDropdownRef"
          class="city-dropdown"
          :style="cityDropdownStyle"
          @click.stop
        >
          <div
            v-for="c in CITIES"
            :key="c.name"
            class="city-option"
            :class="{ selected: activeCity.name === c.name }"
            @click="pickCity(c.name)"
          >
            {{ c.name }}
          </div>
        </div>
      </Teleport>
    </SettingRow>

    <SettingRow label="הצג שעון במצב מסך מלא" hint="שעון שקוף בפינה השמאלית התחתונה, בעת שימוש במצב מסך מלא">
      <ToggleGroup
        v-model="showClock"
        :options="[
          { label: 'כן', value: true },
          { label: 'לא', value: false },
        ]"
      />
    </SettingRow>
  </div>
</template>

<style scoped>
.city-select-box {
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

.city-select-box:hover {
  border-color: var(--accent-color);
}

.city-select-display {
  flex: 1;
  font-size: 12px;
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.city-select-chevron {
  color: var(--text-secondary);
  flex-shrink: 0;
}
</style>

<!-- City dropdown teleported to body — needs unscoped styles -->
<style>
.city-dropdown {
  overflow-y: auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  direction: rtl;
}

.city-option {
  display: flex;
  align-items: center;
  padding: 0 10px;
  height: 26px;
  cursor: pointer;
  font-size: 12px;
  color: var(--text-primary);
  line-height: 1;
}

.city-option:hover {
  background: var(--hover-bg);
}

.city-option.selected {
  background: var(--accent-bg);
  color: var(--accent-color);
  font-weight: 500;
}
</style>
