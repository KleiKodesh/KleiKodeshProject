<script setup lang="ts">
// Zmanim settings dialog: the place (country, city, neighbourhood), the community custom
// and which zman lines to show. Same look as the app's other dialogs; Esc or the backdrop closes.
// Not teleported: it must open inside the window that hosts the tool (the widget window or the
// calendar page), and position: fixed covers that window.
import { computed, ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { IconDismiss20Regular } from '@iconify-prerendered/vue-fluent'
import PlaceTreePicker from './PlaceTreePicker.vue'
import CommunityPicker from './CommunityPicker.vue'
import ZmanimOpinionsPanel from './ZmanimOpinionsPanel.vue'
import RoundingMark from './RoundingMark.vue'

defineProps<{ placeId: number | undefined }>()
const emit = defineEmits<{ close: [], pick: [id: number] }>()

// Listen in the window that actually holds the dialog (the widget window is a separate window).
const backdrop = ref<HTMLElement | null>(null)
const hostWindow = computed(() => backdrop.value?.ownerDocument.defaultView ?? null)
useEventListener(hostWindow, 'keydown', (event: KeyboardEvent) => {
  if (event.code === 'Escape') {
    event.preventDefault()
    emit('close')
  }
})
</script>

<template>
  <div ref="backdrop" class="zs-backdrop" @click.self="emit('close')">
      <div class="zs-dialog" role="dialog" aria-modal="true" :aria-label="$t('hebrewCalendar.zmanimSettings')">
        <div class="zs-head">
          <p class="zs-title">{{ $t('hebrewCalendar.zmanimSettings') }}</p>
          <button class="zs-close" type="button" :title="$t('settings.cancel')" @click="emit('close')">
            <IconDismiss20Regular />
          </button>
        </div>
        <div class="zs-body">
          <label class="zs-label">{{ $t('settings.calendar.city') }}</label>
          <PlaceTreePicker :place-id="placeId" @pick="(id) => emit('pick', id)" />
          <label class="zs-label">{{ $t('settings.calendar.community') }}</label>
          <CommunityPicker />
          <p class="zs-legend" data-rounding-legend>
            <span>18:05<RoundingMark sign="earlier" /> {{ $t('hebrewCalendar.rounded.legendEarlier') }}</span>
            <span>18:05<RoundingMark sign="later" /> {{ $t('hebrewCalendar.rounded.legendLater') }}</span>
          </p>
          <label class="zs-label">{{ $t('hebrewCalendar.chooseOpinions') }}</label>
          <ZmanimOpinionsPanel />
        </div>
      </div>
    </div>
</template>

<style scoped>
.zs-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}
.zs-dialog {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 16px 16px 14px;
  width: 560px;
  max-width: calc(100vw - 32px);
  max-height: calc(100vh - 48px);
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.zs-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.zs-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}
.zs-close {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  background: none;
  border: none;
  border-radius: 4px;
  color: var(--text-secondary);
  cursor: pointer;
  padding: 0;
  /* Icon lines up with the content edge, not the button's padding box. */
  margin-inline-end: -6px;
  flex-shrink: 0;
}
.zs-close svg {
  width: 16px;
  height: 16px;
}
.zs-close:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
  color: var(--text-primary);
}
.zs-body {
  display: flex;
  flex-direction: column;
  gap: 4px;
  overflow-y: auto;
  min-height: 0;
  scrollbar-width: thin;
  /* Scrollbar sits at the dialog edge, with a gap from the content. */
  margin-inline-end: -12px;
  padding-inline-end: 8px;
}
.zs-label {
  font-size: 11px;
  font-weight: 700;
  color: var(--text-secondary);
  margin-top: 6px;
}
.zs-legend {
  margin: 4px 0 0;
  display: flex;
  flex-wrap: wrap;
  gap: 2px 14px;
  font-size: 0.8em;
  color: var(--text-secondary);
}
</style>
