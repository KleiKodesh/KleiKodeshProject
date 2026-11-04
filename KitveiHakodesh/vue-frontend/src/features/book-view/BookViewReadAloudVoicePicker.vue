<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { IconChevronDown12Regular, IconCheckmark16Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useDropdownViewportFit } from '@/composables/useDropdownViewportFit'

/**
 * The read-aloud voice choice: Hebrew voices only. The button shows the voice's short name
 * ("Asaf"); the list shows the full names. The empty value means the first Hebrew voice.
 */
const props = defineProps<{ voices: SpeechSynthesisVoice[]; modelValue: string }>()
const emit = defineEmits<{ 'update:modelValue': [voiceUri: string] }>()
const { t } = useI18n()

const hebrewVoices = computed(() => props.voices.filter((v) => v.lang.toLowerCase().startsWith('he')))
const current = computed(
  () => hebrewVoices.value.find((v) => v.voiceURI === props.modelValue) ?? hebrewVoices.value[0] ?? null,
)

/** "Microsoft Asaf - Hebrew (Israel)" -> "Asaf"; "Microsoft Hila Online (Natural) - Hebrew" -> "Hila". */
function shortName(voice: SpeechSynthesisVoice): string {
  const name = voice.name
    .replace(/^(Microsoft|Google)\s+/i, '')
    .split(/\s+-\s+|\s*\(/)[0]!
    .replace(/\s+Online$/i, '')
    .trim()
  return name || voice.name
}

const isOpen = ref(false)
const dropdownRef = ref<HTMLElement | null>(null)
const buttonRef = ref<HTMLElement | null>(null)
const { justClosed } = useDropdownClose(dropdownRef, () => (isOpen.value = false), { toggleButton: buttonRef })
useDropdownViewportFit(dropdownRef)

function toggle() {
  if (justClosed.value) return
  isOpen.value = !isOpen.value
}
function choose(voiceUri: string) {
  isOpen.value = false
  emit('update:modelValue', voiceUri)
}
</script>

<template>
  <div class="voice-picker">
    <button
      ref="buttonRef"
      class="voice-button"
      :title="current ? `${t('bookView.readAloud.voice')}: ${current.name}` : t('bookView.readAloud.noHebrewVoice')"
      aria-haspopup="listbox"
      :aria-expanded="isOpen"
      @click="toggle"
    >
      <span>{{ current ? shortName(current) : t('bookView.readAloud.noHebrewVoiceShort') }}</span>
      <IconChevronDown12Regular class="chevron" />
    </button>
    <div v-if="isOpen" ref="dropdownRef" class="voice-dropdown" role="listbox">
      <div v-if="hebrewVoices.length === 0" class="voice-row empty">{{ t('bookView.readAloud.noHebrewVoice') }}</div>
      <button
        v-for="v in hebrewVoices"
        :key="v.voiceURI"
        role="option"
        class="voice-row"
        :aria-selected="v === current"
        @click="choose(v.voiceURI)"
      >
        <IconCheckmark16Regular class="check" :class="{ hidden: v !== current }" />
        <span>{{ v.name }}</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.voice-picker {
  position: relative;
}
.voice-button {
  display: flex;
  align-items: center;
  gap: 4px;
  height: var(--toolbar-button-size);
  padding: 0 6px;
  border-radius: 4px;
  font-size: 12px;
  white-space: nowrap;
}
.chevron {
  width: 12px;
  height: 12px;
}
/* The shared toolbar dropdown sizes: 180-300 wide, 320 high, 28 px rows. */
.voice-dropdown {
  position: absolute;
  top: calc(100% + 4px);
  inset-inline-start: 0;
  z-index: 30;
  min-width: 180px;
  max-width: 300px;
  max-height: 320px;
  overflow-y: auto;
  padding: 4px 0;
  background: var(--bg-toolbar);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 4px 12px rgb(0 0 0 / 18%);
}
.voice-row {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  height: 28px;
  padding: 0 10px;
  font-size: 12px;
  white-space: nowrap;
  text-align: start;
}
.voice-row.empty {
  color: var(--text-secondary);
}
.check {
  width: 14px;
  height: 14px;
  flex-shrink: 0;
}
.check.hidden {
  visibility: hidden;
}
</style>
