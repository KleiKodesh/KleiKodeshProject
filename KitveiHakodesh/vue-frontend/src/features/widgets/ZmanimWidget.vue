<script setup lang="ts">
// The zmanim tool: a title with the current place (country › city › neighbourhood) and a
// settings toggle. The settings popup holds the place, the community custom and which
// zman lines to show (the list shows the common ones by default; more or fewer on request).
import { computed, onMounted, ref, shallowRef, watch } from 'vue'
import NextZmanPopup from '@/features/home/NextZmanPopup.vue'
import { useNextZman } from '@/features/home/useNextZman'
import { IconChevronLeft20Regular, IconChevronRight20Regular, IconHome20Regular, IconSettings20Regular } from '@iconify-prerendered/vue-fluent'
import HebrewDatePicker from '@/components/HebrewDatePicker.vue'
import ZmanimSettingsDialog from '@/features/hebrew-calendar/ZmanimSettingsDialog.vue'
import { placeTree, type PlaceTree } from '@/features/hebrew-calendar/zmanimPlaces'

/** The day to show (the separate calendar app's picked day); today when absent. */
const props = defineProps<{ date?: Date | null }>()
/** The day shown: the calendar's picked day, moved by the widget's own back/forward/today. */
const day = ref(props.date ?? new Date())
watch(() => props.date, (d) => { day.value = d ?? new Date() })
function shiftDay(by: number) {
  const d = new Date(day.value)
  d.setDate(d.getDate() + by)
  day.value = d
}
const { rows, city, setCity } = useNextZman(day)
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
        @click="settingsOpen = true"
      ><IconSettings20Regular /></button>
    </div>
    <div class="zw-nav">
      <button class="zw-gear" type="button" :title="$t('hebrewCalendar.nav.prev')" @click="shiftDay(-1)"><IconChevronRight20Regular /></button>
      <HebrewDatePicker v-model="day" />
      <button class="zw-gear" type="button" :title="$t('hebrewCalendar.nav.next')" @click="shiftDay(1)"><IconChevronLeft20Regular /></button>
      <button class="zw-gear" type="button" :title="$t('hebrewCalendar.nav.today')" @click="day = new Date()"><IconHome20Regular /></button>
    </div>
    <ZmanimSettingsDialog v-if="settingsOpen" :place-id="city?.id" @pick="setCity" @close="settingsOpen = false" />
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
.zw-nav {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 2px;
  margin: 4px 8px 0;
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
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  padding: 0;
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
/* The popup's own frame goes: the widget window (or the calendar's side column) is the frame. */
.zmanim-widget :deep(.zman-popup) { border: none; box-shadow: none; border-radius: 0; max-height: none; }
.zmanim-widget :deep(.zp-head) { display: none; }
</style>
