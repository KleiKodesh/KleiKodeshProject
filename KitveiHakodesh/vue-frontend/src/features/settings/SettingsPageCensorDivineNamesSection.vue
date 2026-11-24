<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import {
  DIVINE_NAME_MODE_OPTIONS,
  ELOKIM_MODE_OPTIONS,
  OTHER_NAME_OPTIONS,
  type OtherNameKey,
} from '@/utils/censorDivineNames'
import SettingRow from './SettingRow.vue'
import ToggleGroup from './ToggleGroup.vue'

const settings = useSettingsStore()
const { divineNameMode, elokimMode, otherNamesSelected } = storeToRefs(settings)

function isOtherNameCensored(key: OtherNameKey): boolean {
  return otherNamesSelected.value.includes(key)
}

function toggleOtherName(key: OtherNameKey) {
  const selected = otherNamesSelected.value
  otherNamesSelected.value = selected.includes(key)
    ? selected.filter((k) => k !== key)
    : [...selected, key]
}
</script>

<template>
  <!-- ── כיסוי שם ה' ── -->
  <div data-section="section-censor-divine-names" :data-section-label="$t('settings.censor.section')">
    <div id="section-censor-divine-names" class="section-label">{{ $t('settings.censor.section') }}</div>

    <SettingRow
      id="nav-censor-divine-names"
      :data-nav-label="$t('settings.censor.section')"
      :label="$t('settings.censor.main')"
      :hint="$t('settings.censor.mainHint')"
      wrap
    >
      <ToggleGroup v-model="divineNameMode" :options="[...DIVINE_NAME_MODE_OPTIONS]" />
    </SettingRow>

    <SettingRow
      v-if="divineNameMode !== 'none'"
      id="nav-censor-elokim"
      :data-nav-label="$t('settings.censor.elokimNav')"
      :label="$t('settings.censor.elokim')"
      :hint="$t('settings.censor.elokimHint')"
      wrap
    >
      <ToggleGroup v-model="elokimMode" :options="[...ELOKIM_MODE_OPTIONS]" />
    </SettingRow>

    <SettingRow
      v-if="divineNameMode !== 'none'"
      id="nav-censor-other-names"
      :data-nav-label="$t('settings.censor.other')"
      :label="$t('settings.censor.other')"
      :hint="$t('settings.censor.otherHint')"
      wrap
    >
      <div class="other-names-chips">
        <button
          v-for="opt in OTHER_NAME_OPTIONS"
          :key="opt.value"
          class="other-name-chip"
          :class="{ active: isOtherNameCensored(opt.value) }"
          :title="opt.label"
          @click="toggleOtherName(opt.value)"
        >{{ opt.label }}</button>
      </div>
    </SettingRow>
  </div>
</template>

<style scoped>
.other-names-chips {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(130px, 1fr));
  gap: 4px;
  width: 100%;
}

.other-name-chip {
  height: 28px;
  padding: 0 10px;
  border: 1px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  cursor: pointer;
  font-size: 12px;
  border-radius: 4px;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

.other-name-chip:hover {
  background: var(--hover-bg);
}

.other-name-chip.active {
  background: var(--accent-color);
  color: white;
  border-color: var(--accent-color);
}
</style>
