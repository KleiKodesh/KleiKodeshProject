<script setup lang="ts">
/**
 * A printed zman with its rounding sign: "+" when the minute was rounded down (the real time is a little
 * after it), "-" when rounded up (a little before it). Nothing for a nearest-rounded or exact minute.
 * The tooltip covers the whole time, not just the small sign.
 */
import type { RoundSign } from './zmanimPlaces'

defineProps<{ sign: RoundSign | undefined }>()
</script>

<template>
  <span
    class="rounded-time"
    :title="sign ? $t(`hebrewCalendar.rounded.${sign}`) : undefined"
    :class="{ 'has-sign': sign }"
  ><slot /><span
    v-if="sign"
    class="round-mark"
    :class="sign"
    :data-round-sign="sign"
    aria-hidden="true"
  >{{ sign === 'earlier' ? '+' : '-' }}</span><span v-if="sign" class="sr-only">{{ ' ' + $t(`hebrewCalendar.rounded.${sign}`) }}</span></span>
</template>

<style scoped>
.rounded-time.has-sign { cursor: help; }
.round-mark {
  font-size: 0.75em;
  font-weight: 600;
  margin-inline-start: 1px;
  vertical-align: super;
  line-height: 0;
}
.round-mark.earlier { color: var(--round-earlier); }
.round-mark.later { color: var(--round-later); }
.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
}
</style>
