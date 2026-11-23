<script setup lang="ts">
// The zmanim tool: a title with the current place (country › city › neighbourhood) and a
// settings toggle. The settings popup holds the place, the community custom and which
// zman lines to show (the list shows the common ones by default; more or fewer on request).
import { computed, onMounted, ref, shallowRef } from 'vue'
import NextZmanPopup from '@/features/home/NextZmanPopup.vue'
import { useNextZman } from '@/features/home/useNextZman'
import PlaceTreePicker from '@/features/hebrew-calendar/PlaceTreePicker.vue'
import CommunityPicker from '@/features/hebrew-calendar/CommunityPicker.vue'
import ZmanimOpinionsPanel from '@/features/hebrew-calendar/ZmanimOpinionsPanel.vue'
import { placeTree, type PlaceTree } from '@/features/hebrew-calendar/zmanimPlaces'

const { rows, city, setCity } = useNextZman()
const settingsOpen = ref(false)

const tree = shallowRef<PlaceTree | null>(null)
onMounted(async () => { tree.value = await placeTree() })

/** Country › city › neighbourhood of the current place (neighbourhood only when it is one). */
const placePath = computed(() => {
  const t = tree.value, id = city.value?.id
  if (!t || !id) return ''
  const country = t.countryOf(id)
  const cityId = t.cityOf(id)
  const parts = [
    t.countries.find((c) => c.key === country)?.name,
    t.citiesOf(country).find((c) => c.key === cityId)?.name,
    id !== cityId ? t.placesOf(cityId).find((p) => p.key === id)?.name : undefined,
  ]
  return parts.filter(Boolean).join(' › ')
})
</script>

<template>
  <div class="zmanim-widget">
    <div class="zw-head">
      <span class="zw-place" :title="placePath">{{ placePath }}</span>
      <button
        class="zw-gear"
        type="button"
        :class="{ on: settingsOpen }"
        :aria-expanded="settingsOpen"
        :title="$t('hebrewCalendar.zmanimSettings')"
        @click="settingsOpen = !settingsOpen"
      >⚙</button>
    </div>
    <div v-if="settingsOpen" class="zw-settings" role="dialog" :aria-label="$t('hebrewCalendar.zmanimSettings')">
      <label class="zw-label">{{ $t('settings.calendar.city') }}</label>
      <PlaceTreePicker :place-id="city?.id" @pick="setCity" />
      <label class="zw-label">{{ $t('settings.calendar.community') }}</label>
      <CommunityPicker />
      <label class="zw-label">{{ $t('hebrewCalendar.chooseOpinions') }}</label>
      <ZmanimOpinionsPanel />
    </div>
    <NextZmanPopup :rows="rows" :city-name="''" />
  </div>
</template>

<style scoped>
.zw-head {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 8px 8px 0;
}
.zw-place {
  flex: 1;
  min-width: 0;
  font-size: 12px;
  font-weight: 700;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.zw-gear {
  flex: none;
  width: 24px;
  height: 24px;
  font-size: 14px;
  line-height: 1;
  background: none;
  border: 1px solid transparent;
  border-radius: 4px;
  color: var(--text-secondary);
  cursor: pointer;
}
.zw-gear:hover,
.zw-gear.on {
  color: var(--text-primary);
  border-color: var(--border-color);
}
.zw-settings {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin: 6px 8px 0;
  padding: 8px;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-secondary);
  max-height: 60vh;
  overflow-y: auto;
}
.zw-label {
  font-size: 11px;
  font-weight: 700;
  color: var(--text-secondary);
  margin-top: 4px;
}
/* The popup's own frame goes: the widget window (or the calendar's side column) is the frame. */
.zmanim-widget :deep(.zman-popup) { border: none; box-shadow: none; border-radius: 0; max-height: none; }
.zmanim-widget :deep(.zp-head) { display: none; }
</style>
