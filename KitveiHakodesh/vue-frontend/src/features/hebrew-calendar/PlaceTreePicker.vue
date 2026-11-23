<script setup lang="ts">
/**
 * The zmanim place in three steps: country, city, then neighbourhood (only when the city
 * has any). Each list is sorted by name in the UI language. Choosing a country only
 * narrows the cities; the place changes when a city or neighbourhood is chosen.
 */
import { computed, onMounted, ref, shallowRef, watch } from 'vue'
import { placeTree, type PlaceTree } from './zmanimPlaces'

const props = defineProps<{ placeId: number | undefined }>()
const emit = defineEmits<{ pick: [id: number] }>()

const tree = shallowRef<PlaceTree | null>(null)
onMounted(async () => { tree.value = await placeTree() })

// The country on screen: the place's, until the user picks another.
const country = ref('')
const placeCountry = computed(() => (tree.value && props.placeId ? tree.value.countryOf(props.placeId) : ''))
watch(placeCountry, (code) => { country.value = code }, { immediate: true })

const city = computed(() => (tree.value && props.placeId ? tree.value.cityOf(props.placeId) : 0))
const cities = computed(() => (tree.value && country.value ? tree.value.citiesOf(country.value) : []))
const inShownCountry = computed(() => country.value === placeCountry.value)
const places = computed(() => (tree.value && city.value && inShownCountry.value ? tree.value.placesOf(city.value) : []))

const pickNumber = (e: Event) => Number((e.target as HTMLSelectElement).value)
</script>

<template>
  <div v-if="tree" class="place-tree">
    <select
      class="place-tree-select"
      :value="country"
      :title="$t('settings.calendar.country')"
      @change="country = ($event.target as HTMLSelectElement).value"
    >
      <option v-for="c in tree.countries" :key="c.key" :value="c.key">{{ c.name }}</option>
    </select>
    <select
      class="place-tree-select"
      :value="inShownCountry ? city : 0"
      :title="$t('settings.calendar.cityOnly')"
      @change="emit('pick', pickNumber($event))"
    >
      <option v-if="!inShownCountry" :value="0" disabled>{{ $t('settings.calendar.pickCity') }}</option>
      <option v-for="c in cities" :key="c.key" :value="c.key">{{ c.name }}</option>
    </select>
    <select
      v-if="places.length > 1"
      class="place-tree-select"
      :value="placeId"
      :title="$t('settings.calendar.neighbourhood')"
      @change="emit('pick', pickNumber($event))"
    >
      <option v-for="p in places" :key="p.key" :value="p.key">{{ p.key === city ? $t('settings.calendar.wholeCity') : p.name }}</option>
    </select>
  </div>
</template>

<style scoped>
.place-tree { display: flex; flex-direction: column; gap: 4px; }
.place-tree-select {
  display: block;
  width: 100%;
  height: 26px;
  font-size: 12px;
  color: var(--text-primary);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  cursor: pointer;
}
.place-tree-select:hover { border-color: var(--accent-color); }
</style>
