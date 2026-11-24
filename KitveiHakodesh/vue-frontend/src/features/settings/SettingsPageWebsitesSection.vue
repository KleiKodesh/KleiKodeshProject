<script setup lang="ts">
import { computed } from 'vue'
import SettingRow from './SettingRow.vue'
import ToggleGroup from './ToggleGroup.vue'
import { websitesSettings, type SiteLeaveAction } from '@/features/websites/websitesSettings'
import { displayHost } from '@/features/websites/websitesList'

/** Settings > Websites: what happens to a heavy site when you leave its tab (siteFrames.ts). */
const leaveAction = computed<SiteLeaveAction>({
  get: () => websitesSettings.leaveAction,
  set: (value) => (websitesSettings.leaveAction = value),
})
const remembered = computed(() => Object.entries(websitesSettings.remembered))

function onHeavyInput(e: Event) {
  const value = Number((e.target as HTMLInputElement).value)
  if (Number.isFinite(value) && value >= 20) websitesSettings.heavyMb = Math.round(value)
}
function forget(origin: string) {
  delete websitesSettings.remembered[origin]
}
</script>

<template>
  <div data-section="section-websites" :data-section-label="$t('websites.settingsTitle')">
    <div id="section-websites" class="section-label">{{ $t('websites.settingsTitle') }}</div>

    <SettingRow :label="$t('websites.leaveActionLabel')">
      <ToggleGroup
        v-model="leaveAction"
        :options="[
          { label: $t('websites.leaveAsk'), value: 'ask' },
          { label: $t('websites.leaveKeep'), value: 'keep' },
          { label: $t('websites.leaveUnload'), value: 'unload' },
        ]"
      />
    </SettingRow>

    <SettingRow :label="$t('websites.heavyLabel')">
      <input
        class="websites-heavy-input"
        type="number"
        min="20"
        step="10"
        :value="websitesSettings.heavyMb"
        @change="onHeavyInput"
      >
    </SettingRow>

    <SettingRow :label="$t('websites.rememberedLabel')" wrap>
      <div class="websites-remembered">
        <span v-if="!remembered.length" class="websites-remembered__none">{{ $t('websites.rememberedNone') }}</span>
        <div v-for="[origin, choice] in remembered" :key="origin" class="websites-remembered__row">
          <span>{{ displayHost(origin) }}: {{ choice === 'keep' ? $t('websites.rememberedKeep') : $t('websites.rememberedUnload') }}</span>
          <button type="button" class="websites-remembered__forget" @click="forget(origin)">{{ $t('websites.forget') }}</button>
        </div>
      </div>
    </SettingRow>
  </div>
</template>

<style scoped>
.websites-heavy-input {
  width: 90px;
  padding: 4px 6px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: transparent;
  color: inherit;
}
.websites-remembered {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.websites-remembered__none {
  color: var(--text-secondary);
}
.websites-remembered__row {
  display: flex;
  align-items: center;
  gap: 8px;
}
.websites-remembered__forget {
  padding: 2px 8px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: transparent;
  color: inherit;
  cursor: pointer;
}
</style>
