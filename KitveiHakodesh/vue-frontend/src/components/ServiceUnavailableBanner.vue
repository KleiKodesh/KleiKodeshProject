<script setup lang="ts">
import { computed } from 'vue'
import {
  serviceUnavailableReason,
  isRetryingService,
  retryService,
} from '@/webview-host/serviceAvailability'
import { SERVICE_UNAVAILABLE_TEXTS, SERVICE_ACTION_LABELS, SERVICE_RETRYING_LABEL } from './serviceUnavailableTexts'

// The one place the app says the KitveiHakodesh service cannot be reached, and why. Every data
// op goes through the service, so instead of each feature failing on its own, serviceClient
// records the failure (serviceAvailability.ts) and this banner explains it with a
// button that asks before it acts: only a click on it may raise the Windows admin prompt
// (install / repair when needed) - no data request ever does. Mount ONCE at the app root.
const texts = computed(() =>
  serviceUnavailableReason.value ? SERVICE_UNAVAILABLE_TEXTS[serviceUnavailableReason.value] : null,
)
</script>

<template>
  <Teleport to="body">
    <Transition name="service-banner">
      <div v-if="texts" class="service-banner" role="alert">
        <span class="service-banner-icon" aria-hidden="true">
          <svg viewBox="0 0 20 20" width="18" height="18" fill="currentColor">
            <path d="M10 2a8 8 0 1 1 0 16 8 8 0 0 1 0-16Zm0 10.5a1 1 0 1 0 0 2 1 1 0 0 0 0-2Zm0-7a.75.75 0 0 0-.74.65l-.01.1v4.5a.75.75 0 0 0 1.49.1l.01-.1v-4.5A.75.75 0 0 0 10 5.5Z"/>
          </svg>
        </span>
        <div class="service-banner-text">
          <strong>{{ texts.title }}</strong>
          <span>{{ texts.body }}</span>
        </div>
        <button
          class="service-banner-retry"
          :disabled="isRetryingService"
          @click="retryService"
        >
          {{ isRetryingService ? SERVICE_RETRYING_LABEL : SERVICE_ACTION_LABELS[serviceUnavailableReason!] }}
        </button>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.service-banner {
  /* Pinned 16px from both edges and centred by auto margins - NOT left:50% + translate,
     which caps a shrink-to-fit box at HALF the viewport and, in a narrow Word task pane,
     broke the text after every word. */
  position: fixed;
  top: 12px;
  left: 16px;
  right: 16px;
  margin-inline: auto;
  width: fit-content;
  max-width: 640px;
  z-index: 10002;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
  padding: 12px 16px;
  direction: rtl;
  font-family: var(--header-font);
  font-size: 13px;
  line-height: 1.45;
  color: var(--text-primary);
  background: var(--bg-primary);
  border: 1px solid var(--status-danger);
  border-radius: 8px;
  box-shadow: 0 8px 16px rgba(0, 0, 0, 0.14), 0 0 2px rgba(0, 0, 0, 0.12);
}

.service-banner-icon {
  flex-shrink: 0;
  display: inline-flex;
  color: var(--status-danger);
}

.service-banner-text {
  /* At least ~16 em of text before the button wraps onto its own line. */
  flex: 1 1 16em;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.service-banner-retry {
  flex-shrink: 0;
  margin-inline-start: auto;
  padding: 6px 14px;
  font: inherit;
  color: var(--text-on-accent, #fff);
  background: var(--accent-color);
  border: none;
  border-radius: 4px;
  cursor: pointer;
}
.service-banner-retry:disabled {
  opacity: 0.6;
  cursor: default;
}

.service-banner-enter-active,
.service-banner-leave-active {
  transition: opacity 250ms ease, transform 250ms ease;
}
.service-banner-enter-from,
.service-banner-leave-to {
  opacity: 0;
  transform: translateY(-12px);
}
</style>
