<script setup lang="ts">
import { computed } from 'vue'

/**
 * The live sample above the font controls, pinned to the top of the wizard card's
 * scroller so it stays visible while the controls below it scroll.
 *
 * Shared by the book and commentary steps: same box, different sample text and a
 * different set of font values, so both are props.
 */
const props = defineProps<{
  headerFont: string
  textFont: string
  /** Percent of the reading default, as the font-size setting stores it. */
  fontSize: number
  fontWeight: number
  linePadding: number
  heading: string
  body: string
}>()

/**
 * The heading sits one weight step above the body so it still reads as a heading, but
 * it TRACKS the weight slider rather than being pinned — a hardcoded bold here made the
 * slider look broken, since it never moved the heading line. Capped at 700, the top of
 * the slider's range.
 */
const headingWeight = computed(() => Math.min(props.fontWeight + 100, 700))
</script>

<template>
  <div
    class="reading-preview"
    :style="{
      fontFamily: textFont,
      fontSize: fontSize * 0.14 + 'px',
      fontWeight: fontWeight,
      lineHeight: linePadding,
    }"
  >
    <div class="reading-preview-header" :style="{ fontFamily: headerFont, fontWeight: headingWeight }">
      {{ heading }}
    </div>
    <div class="reading-preview-body">{{ body }}</div>
  </div>
</template>

<style scoped>
/* The step body pads the card by 16px/20px; the negative insets pull this back out to
   the card's own edges so nothing shows down its sides, and the padding is put back
   inside. Opaque background so the rows scrolling under it stay covered. */
.reading-preview {
  position: sticky;
  top: 0;
  z-index: 2;
  margin: -16px -20px 12px;
  padding: 16px 20px;
  background: var(--bg-secondary);
  border-bottom: 1px solid var(--border-color);
  color: var(--text-primary);
  direction: rtl;
  text-align: justify;
  overflow: hidden;
}

/* No font-weight here: the inline style above owns it (and would override this). */
.reading-preview-header {
  font-size: 1.15em;
  margin-bottom: 4px;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.reading-preview-body {
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
</style>
