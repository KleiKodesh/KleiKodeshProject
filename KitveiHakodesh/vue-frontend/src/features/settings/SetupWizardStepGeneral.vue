<script setup lang="ts">
import { ref, nextTick, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { IconChevronDown20Regular, IconChevronUp20Regular } from '@iconify-prerendered/vue-fluent'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useZmanim, CITIES } from '@/features/hebrew-calendar/useZmanim'
import SettingRow from './SettingRow.vue'
import ToggleGroup from './ToggleGroup.vue'

const settings = useSettingsStore()
const { censorDivineNames, newTabPage, resumeLastRead, defaultAutoSyncCommentary } =
  storeToRefs(settings)

const bookViewStore = useBookViewStore()
const { toolbarPosition } = storeToRefs(bookViewStore)

const { activeCity, setCity, init: initZmanim } = useZmanim()

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

onMounted(() => {
  initZmanim()
})

async function toggleCityDropdown() {
  if (cityOpen.value) {
    cityOpen.value = false
    return
  }
  cityOpen.value = true
  await nextTick()
  if (!cityBoxRef.value || !cityDropdownRef.value) return
  const rect = cityBoxRef.value.getBoundingClientRect()
  const spaceBelow = window.innerHeight - rect.bottom - 8
  const spaceAbove = rect.top - 8
  const goUp = spaceAbove > spaceBelow
  const maxH = Math.min(240, goUp ? spaceAbove : spaceBelow)
  cityDropdownRef.value.style.maxHeight = maxH + 'px'
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
  <div class="step-content">
    <div class="step-header">
      <h2 class="step-title">כמה הגדרות מהירות</h2>
      <p class="step-desc">הגדרות אלו ישפיעו על חוויית הקריאה היומיומית שלך.</p>
    </div>
    <div class="step-scroll">
      <div class="step-card">
        <SettingRow label="כיסוי שם ה'">
          <ToggleGroup
            v-model="censorDivineNames"
            :options="[
              { label: 'כתיב מלא', value: false },
              { label: 'כיסוי (ה←ד)', value: true },
            ]"
          />
        </SettingRow>
        <SettingRow
          label="זכור מיקום אחרון בספר"
          title="בפתיחת ספר מחדש, האפליקציה תחזור אוטומטית למקום שבו הפסקת לקרוא"
        >
          <ToggleGroup
            v-model="resumeLastRead"
            :options="[
              { label: 'כן', value: true },
              { label: 'לא', value: false },
            ]"
          />
        </SettingRow>
        <SettingRow label="פתח טאב חדש אל" wrap>
          <ToggleGroup
            v-model="newTabPage"
            :options="[
              { label: 'דף הבית', value: 'homepage' },
              { label: 'פתיחת ספר', value: 'openfile' },
              { label: 'היברו בוקס', value: 'hebrewbooks' },
              { label: 'חיפוש', value: 'search' },
            ]"
          />
        </SettingRow>
        <SettingRow label="מיקום סרגל הכלים בתצוגת ספר" wrap>
          <ToggleGroup
            v-model="toolbarPosition"
            :options="[
              { label: 'למעלה', value: 'top' },
              { label: 'למטה', value: 'bottom' },
              { label: 'שמאל', value: 'left' },
              { label: 'ימין', value: 'right' },
            ]"
            @update:model-value="bookViewStore.setToolbarPosition($event)"
          />
        </SettingRow>
        <SettingRow label="סנכרן מפרשים כברירת מחדל">
          <ToggleGroup
            v-model="defaultAutoSyncCommentary"
            :options="[
              { label: 'כן', value: true },
              { label: 'לא', value: false },
            ]"
          />
        </SettingRow>
        <SettingRow label="עיר לזמני היום" hint="העיר שלפיה יחושבו זמני היום בלוח השנה">
          <div ref="cityBoxRef" class="select-box" tabindex="0" @click="toggleCityDropdown">
            <span class="select-display">{{ activeCity.name }}</span>
            <component
              :is="cityOpen ? IconChevronUp20Regular : IconChevronDown20Regular"
              class="select-chevron"
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
      </div>
    </div>
  </div>
</template>

<style scoped>
.step-content {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.step-header {
  flex-shrink: 0;
  max-width: 560px;
  width: 100%;
  margin: 0 auto;
  padding: 28px 16px 12px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  box-sizing: border-box;
}

.step-title {
  margin: 0;
  font-size: 20px;
  font-weight: 700;
  color: var(--text-primary);
  line-height: 1.2;
  animation: fade-up 0.25s ease both;
}

.step-desc {
  margin: 0;
  font-size: 13px;
  color: var(--text-secondary);
  line-height: 1.6;
  animation: fade-up 0.25s 0.05s ease both;
}

.step-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 0 16px 24px;
}

.step-card {
  max-width: 560px;
  margin: 0 auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 16px 20px;
  animation: fade-up 0.25s 0.1s ease both;
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
  font-size: 12px;
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.select-chevron {
  color: var(--text-secondary);
  flex-shrink: 0;
}

.city-dropdown {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  direction: rtl;
}
.city-option {
  height: 26px;
  padding: 0 10px;
  display: flex;
  align-items: center;
  font-size: 12px;
  color: var(--text-primary);
  cursor: pointer;
  white-space: nowrap;
}
.city-option:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.city-option.selected {
  color: var(--accent-color);
}

@keyframes fade-up {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
