<script setup lang="ts">
import { ref, nextTick, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { IconChevronDown20Regular, IconChevronUp20Regular } from '@iconify-prerendered/vue-fluent'
import { useSettingsStore } from '@/stores/settingsStore'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { pickFolder, clearHbLocalFolder } from '@/webview-host/bridge'
import { useZmanim } from '@/features/hebrew-calendar/useZmanim'
import { usePlacePicker } from '@/features/hebrew-calendar/usePlacePicker'
import PlaceTreePicker from '@/features/hebrew-calendar/PlaceTreePicker.vue'
import SettingRow from './SettingRow.vue'
import SettingsPagePathField from './SettingsPagePathField.vue'
import ToggleGroup from './ToggleGroup.vue'

const settings = useSettingsStore()
const { resumeLastRead, hebrewBooksLocalFolder } = storeToRefs(settings)

// Same wiring as the settings page (SettingsPageAdvancedSection).
async function pickHebrewBooksFolder() {
  const result = await pickFolder()
  if (result) hebrewBooksLocalFolder.value = result
}

function commitHebrewBooksFolder(newPath: string) {
  hebrewBooksLocalFolder.value = newPath
}

async function resetHebrewBooksFolder() {
  await clearHbLocalFolder()
  hebrewBooksLocalFolder.value = ''
}

const { activeCity, activeCityName, setCity, init: initZmanim } = useZmanim()

const cityBoxRef = ref<HTMLElement | null>(null)
const cityDropdownRef = ref<HTMLElement | null>(null)
const cityOpen = ref(false)
const cityDropdownStyle = ref<Record<string, string>>({})
const { query: placeQuery, options: placeOptions } = usePlacePicker(cityOpen)

useDropdownClose(
  cityDropdownRef,
  () => {
    cityOpen.value = false
  },
  // fitToViewport off: toggleCityDropdown positions and caps the list itself, and the fit
  // pass would wipe that max-height and re-shift the list a frame later.
  { ignore: [cityBoxRef], fitToViewport: false },
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

function pickCity(id: number) {
  setCity(id)
  cityOpen.value = false
}
</script>

<template>
  <SettingRow
    :label="$t('settings.book.remember')"
    :title="$t('settings.book.rememberHint')"
  >
    <ToggleGroup
      v-model="resumeLastRead"
      :options="[
        { label: $t('settings.yes'), value: true },
        { label: $t('settings.no'), value: false },
      ]"
    />
  </SettingRow>
  <SettingRow :label="$t('settings.calendar.city')" :hint="$t('settings.calendar.cityHintWizard')">
    <div ref="cityBoxRef" class="select-box" tabindex="0" @click="toggleCityDropdown">
      <span class="select-display">{{ activeCityName }}</span>
      <component
        :is="cityOpen ? IconChevronUp20Regular : IconChevronDown20Regular"
        class="select-chevron"
      />
    </div>
    <PlaceTreePicker class="city-tree" :place-id="activeCity?.id" @pick="setCity" />
    <Teleport to="body">
      <div
        v-if="cityOpen"
        ref="cityDropdownRef"
        class="city-dropdown"
        :style="cityDropdownStyle"
        @click.stop
      >
        <input
          v-model="placeQuery"
          type="search"
          class="city-search"
          :placeholder="$t('settings.calendar.citySearch')"
        />
        <div
          v-for="c in placeOptions"
          :key="c.id"
          class="city-option"
          :class="{ selected: activeCity?.id === c.id }"
          @click="pickCity(c.id)"
        >
          {{ c.name }}
        </div>
      </div>
    </Teleport>
  </SettingRow>
  <SettingRow
    :label="$t('settings.advanced.hbFolder')"
    :hint="$t('settings.advanced.hbFolderHint')"
  >
    <SettingsPagePathField
      :value="hebrewBooksLocalFolder"
      :placeholder="$t('settings.advanced.noFolder')"
      :clearable="true"
      :editable="true"
      @pick="pickHebrewBooksFolder"
      @clear="resetHebrewBooksFolder"
      @commit="commitHebrewBooksFolder"
    />
  </SettingRow>
</template>

<style scoped>
.city-tree { margin-top: 6px; }
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
}
.city-search {
  position: sticky;
  top: 0;
  box-sizing: border-box;
  width: 100%;
  height: 28px;
  padding: 0 10px;
  border: none;
  border-bottom: 1px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  font: inherit;
  font-size: 12px;
  outline: none;
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
</style>
