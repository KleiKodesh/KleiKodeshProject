<script setup lang="ts">
/**
 * The small sign after a printed zman: "+" when the minute was rounded earlier (the real time is a little
 * after it), "-" when rounded later (a little before it). Nothing for a nearest-rounded or exact minute.
 */
import type { RoundSign } from './zmanimPlaces'

defineProps<{ sign: RoundSign | undefined }>()
</script>

<template>
  <span
    v-if="sign"
    class="round-mark"
    :class="sign"
    :data-round-sign="sign"
    :title="$t(`hebrewCalendar.rounded.${sign}`)"
    :aria-label="$t(`hebrewCalendar.rounded.${sign}`)"
  >{{ sign === 'earlier' ? '+' : '-' }}</span>
</template>

<style scoped>
.round-mark {
  font-size: 0.75em;
  font-weight: 600;
  margin-inline-start: 1px;
  vertical-align: super;
  line-height: 0;
  cursor: help;
}
.round-mark.earlier { color: var(--round-earlier); }
.round-mark.later { color: var(--round-later); }
</style>
