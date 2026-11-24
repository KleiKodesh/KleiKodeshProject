<script setup lang="ts">
import { onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import ZmanimDisclaimer from '@/features/hebrew-calendar/ZmanimDisclaimer.vue'
import SettingRow from './SettingRow.vue'
import ToggleGroup from './ToggleGroup.vue'
import { useZmanim } from '@/features/hebrew-calendar/useZmanim'
import PlaceTreePicker from '@/features/hebrew-calendar/PlaceTreePicker.vue'
import CommunityPicker from '@/features/hebrew-calendar/CommunityPicker.vue'
import { useSettingsStore } from '@/stores/settingsStore'

const { showClock } = storeToRefs(useSettingsStore())

const { activeCity, setCity, init: initZmanim } = useZmanim()
onMounted(() => initZmanim())
</script>

<template>
  <div data-section="section-calendar" :data-section-label="$t('settings.calendar.section')">
    <div id="section-calendar" class="section-label">{{ $t('settings.calendar.section') }}</div>

    <SettingRow :label="$t('settings.calendar.city')" :hint="$t('settings.calendar.cityHint')">
      <PlaceTreePicker :place-id="activeCity?.id" @pick="setCity" />
    </SettingRow>

    <SettingRow :label="$t('settings.calendar.community')" :hint="$t('settings.calendar.communityHint')">
      <CommunityPicker />
    </SettingRow>

    <SettingRow :label="$t('settings.calendar.clock')" :hint="$t('settings.calendar.clockHint')">
      <ToggleGroup
        v-model="showClock"
        :options="[
          { label: $t('settings.yes'), value: true },
          { label: $t('settings.no'), value: false },
        ]"
      />
    </SettingRow>
    <ZmanimDisclaimer class="settings-zmanim-disclaimer" />
  </div>
</template>

