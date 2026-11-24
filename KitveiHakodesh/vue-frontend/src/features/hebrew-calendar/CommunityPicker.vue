<script setup lang="ts">
// The "Community custom" setting: a native select (settings page and setup wizard).
import { onMounted, ref } from 'vue'
import { chosenCommunity, communityOptions, DEFAULT_COMMUNITY, setCommunity } from './zmanimCommunity'
import { resetZmanShown } from './zmanimShown'

/** Choosing a preset sets the shown lines to that preset (the user's own edits start over from it). */
function choose(id: string) {
  setCommunity(id === DEFAULT_COMMUNITY ? '' : id)
  resetZmanShown()
}

const options = ref<Array<{ id: string, name: string }>>([])
onMounted(async () => { options.value = await communityOptions() })
</script>

<template>
  <select
    class="community-select"
    data-testid="community-select"
    :value="chosenCommunity || DEFAULT_COMMUNITY"
    @change="choose(($event.target as HTMLSelectElement).value)"
  >
    <option v-for="c in options" :key="c.id" :value="c.id">{{ c.name }}</option>
  </select>
</template>

<style scoped>
.community-select {
  width: 100%;
  height: 28px;
  padding: 0 6px;
  box-sizing: border-box;
  font: inherit;
  font-size: 12px;
  color: var(--text-primary);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
}
.community-select:hover { border-color: var(--accent-color); }
</style>
