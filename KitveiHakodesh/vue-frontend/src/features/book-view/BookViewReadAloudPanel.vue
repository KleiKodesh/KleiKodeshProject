<script setup lang="ts">
import { computed, ref } from 'vue'
import { useDraggable } from '@vueuse/core'
import { useI18n } from 'vue-i18n'
import {
  IconPlay20Filled,
  IconPause20Filled,
  IconStop20Filled,
  IconPrevious20Regular,
  IconNext20Regular,
  IconHighlight20Regular,
  IconHighlight20Filled,
  IconArrowAutofitDown20Regular,
  IconDismiss20Regular,
  IconReOrderDotsVertical20Regular,
  IconFastForward20Regular,
  IconRewind20Regular,
} from '@iconify-prerendered/vue-fluent'
import { READ_ALOUD_RATE_MIN, READ_ALOUD_RATE_MAX, READ_ALOUD_RATE_STEP, type BookViewReadAloud } from './useBookViewReadAloud'
import BookViewReadAloudVoicePicker from './BookViewReadAloudVoicePicker.vue'
import BookViewReadAloudSkipDropdown from './BookViewReadAloudSkipDropdown.vue'

/**
 * The read-aloud controls: a small horizontal bar floating over the text, at its top left
 * at first and moved by its drag handle (kept inside the text area), open while the
 * toolbar's read-aloud button is on. The text is RTL, so the line before
 * points right (the "next" glyph) and the line after points left.
 */
const props = defineProps<{ readAloud: BookViewReadAloud }>()
const emit = defineEmits<{ play: []; close: [] }>()
const { t } = useI18n()

const r = props.readAloud

const panelEl = ref<HTMLElement | null>(null)
const handleEl = ref<HTMLElement | null>(null)
const { style: dragStyle } = useDraggable(panelEl, {
  handle: handleEl,
  containerElement: () => panelEl.value?.parentElement ?? null,
  initialValue: { x: 8, y: 8 },
  preventDefault: true,
})

const playLabel = computed(() =>
  t(!r.isReading.value ? 'bookView.readAloud.play' : r.isPaused.value ? 'bookView.readAloud.resume' : 'bookView.readAloud.pause'),
)

function onPlay() {
  if (!r.isReading.value) emit('play')
  else if (r.isPaused.value) r.resume()
  else r.pause()
}

const rateLabel = computed(() => `${r.rate.value.toFixed(1)}×`)

function onVoiceChange(voiceUri: string) {
  r.voiceUri.value = voiceUri
  r.applySettingsChange()
}
</script>

<template>
  <div ref="panelEl" class="read-aloud-panel" :style="dragStyle" role="toolbar" aria-orientation="horizontal" :aria-label="t('bookView.readAloud.panel')">
    <span ref="handleEl" class="drag-handle" :title="t('bookView.readAloud.drag')">
      <IconReOrderDotsVertical20Regular />
    </span>
    <button :class="{ active: r.isReading.value }" :title="playLabel" @click="onPlay">
      <IconPause20Filled v-if="r.isReading.value && !r.isPaused.value" class="pause-icon" />
      <IconPlay20Filled v-else />
    </button>
    <button :disabled="!r.isReading.value" :title="t('bookView.readAloud.stop')" @click="r.stop()">
      <IconStop20Filled />
    </button>
    <button :disabled="!r.position.value" :title="t('bookView.readAloud.previousLine')" @click="r.skip(-1)">
      <IconNext20Regular />
    </button>
    <button :disabled="!r.position.value" :title="t('bookView.readAloud.nextLine')" @click="r.skip(1)">
      <IconPrevious20Regular />
    </button>
    <div class="separator" />
    <!-- The current speed shows only in these two tooltips. -->
    <button :disabled="r.rate.value <= READ_ALOUD_RATE_MIN" :title="`${t('bookView.readAloud.slower')} (${rateLabel})`" @click="r.changeRate(-READ_ALOUD_RATE_STEP)">
      <IconRewind20Regular />
    </button>
    <button :disabled="r.rate.value >= READ_ALOUD_RATE_MAX" :title="`${t('bookView.readAloud.faster')} (${rateLabel})`" @click="r.changeRate(READ_ALOUD_RATE_STEP)">
      <IconFastForward20Regular />
    </button>
    <div class="separator" />
    <button
      :class="{ active: r.highlightWords.value }"
      :title="t('bookView.readAloud.highlight')"
      @click="r.highlightWords.value = !r.highlightWords.value"
    >
      <IconHighlight20Filled v-if="r.highlightWords.value" />
      <IconHighlight20Regular v-else />
    </button>
    <button
      :class="{ active: r.followAlong.value }"
      :title="t('bookView.readAloud.follow')"
      @click="r.followAlong.value = !r.followAlong.value"
    >
      <IconArrowAutofitDown20Regular />
    </button>
    <BookViewReadAloudSkipDropdown
      v-model:enabled="r.skipEnabled.value"
      v-model:options="r.skipOptions.value"
      @changed="r.applySettingsChange()"
    />
    <BookViewReadAloudVoicePicker :voices="r.voices.value" :model-value="r.voiceUri.value" @update:model-value="onVoiceChange" />
    <div class="separator" />
    <button :title="t('bookView.readAloud.close')" @click="emit('close')">
      <IconDismiss20Regular />
    </button>
  </div>
</template>

<style scoped>
.read-aloud-panel {
  /* left/top come from the drag (dragStyle), relative to the text area. */
  position: absolute;
  z-index: 20;
  display: flex;
  flex-direction: row;
  align-items: center;
  /* Sized like a compact floating debug bar: 30px tall, 22px square buttons 3px apart. */
  --read-aloud-button-size: 22px;
  /* The shared split dropdown (skip) sizes itself from this. */
  --toolbar-button-size: var(--read-aloud-button-size);
  box-sizing: border-box;
  height: 30px;
  padding: 0 4px;
  gap: 3px;
  background: var(--bg-toolbar);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 2px 8px rgb(0 0 0 / 15%);
}
button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--read-aloud-button-size);
  height: var(--read-aloud-button-size);
  padding: 3px;
  border-radius: 4px;
  flex-shrink: 0;
}
button svg {
  width: 16px;
  height: 16px;
}
button.active {
  color: var(--accent-color);
}
button:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
.pause-icon {
  color: #d13438;
}
.separator {
  width: 1px;
  height: 18px;
  background: var(--border-color);
  margin: 0 2px;
}
.drag-handle {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: var(--read-aloud-button-size);
  cursor: grab;
  color: var(--text-secondary);
  touch-action: none;
}
.drag-handle:active {
  cursor: grabbing;
}
.drag-handle svg {
  width: 16px;
  height: 16px;
}
</style>

<style>
/* The word being read; global because ::highlight() names live on the document. */
::highlight(kh-read-aloud) {
  background-color: color-mix(in srgb, var(--accent-color) 30%, transparent);
}
</style>
