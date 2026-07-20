<script setup lang="ts">
import { onBeforeUnmount } from 'vue'
import { useToast } from '@/composables/useToast'

// Global toast, Fluent 2 style: a compact card anchored to the bottom corner,
// NOT a full-width banner. Mount ONCE at the app root; trigger from anywhere
// with `import { showToast } from '@/composables/useToast'`.
const { message, variant, dismissToast } = useToast()

onBeforeUnmount(dismissToast)
</script>

<template>
  <Teleport to="body">
    <Transition name="toast-pop">
      <div
        v-if="message"
        class="toast"
        :class="`toast-${variant}`"
        role="status"
        @click="dismissToast"
      >
        <span class="toast-icon" aria-hidden="true">
          <!-- success -->
          <svg v-if="variant === 'success'" viewBox="0 0 20 20" width="18" height="18" fill="currentColor">
            <path d="M10 2a8 8 0 1 1 0 16 8 8 0 0 1 0-16Zm3.6 5.4a.75.75 0 0 0-1.02-.05l-.05.05L9 10.94 7.47 9.4a.75.75 0 0 0-1.11 1l.05.06 2.06 2.06c.28.29.73.3 1.03.05l.05-.05 4.06-4.06a.75.75 0 0 0 0-1.06Z"/>
          </svg>
          <!-- error -->
          <svg v-else-if="variant === 'error'" viewBox="0 0 20 20" width="18" height="18" fill="currentColor">
            <path d="M10 2a8 8 0 1 1 0 16 8 8 0 0 1 0-16Zm2.3 5.7a.75.75 0 0 0-1.06 0L10 8.94 8.76 7.7a.75.75 0 1 0-1.06 1.06L8.94 10 7.7 11.24a.75.75 0 1 0 1.06 1.06L10 11.06l1.24 1.24a.75.75 0 0 0 1.06-1.06L11.06 10l1.24-1.24a.75.75 0 0 0 0-1.06Z"/>
          </svg>
          <!-- info -->
          <svg v-else viewBox="0 0 20 20" width="18" height="18" fill="currentColor">
            <path d="M10 2a8 8 0 1 1 0 16 8 8 0 0 1 0-16Zm0 7a.75.75 0 0 0-.74.65l-.01.1v3.5a.75.75 0 0 0 1.49.1l.01-.1V9.75A.75.75 0 0 0 10 9Zm0-3a1 1 0 1 0 0 2 1 1 0 0 0 0-2Z"/>
          </svg>
        </span>
        <span class="toast-text">{{ message }}</span>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.toast {
  position: fixed;
  bottom: 20px;
  /* Logical inline-end: right in LTR, left in this RTL app — matches Windows'
     mirrored toast corner. */
  inset-inline-end: 20px;
  z-index: 10001;
  display: flex;
  align-items: center;
  gap: 10px;
  max-width: min(360px, calc(100vw - 40px));
  padding: 12px 14px;
  direction: rtl;
  font-family: var(--header-font);
  font-size: 13px;
  line-height: 1.4;
  color: var(--text-primary);
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 8px 16px rgba(0, 0, 0, 0.14), 0 0 2px rgba(0, 0, 0, 0.12);
  cursor: pointer;
}

.toast-icon {
  flex-shrink: 0;
  display: inline-flex;
  color: var(--accent-color);
}
.toast-success .toast-icon {
  color: var(--status-success);
}
.toast-error .toast-icon {
  color: var(--status-danger);
}

.toast-text {
  flex: 1;
  min-width: 0;
}

/* Fluent-ish entrance: slide up + fade, unhurried. */
.toast-pop-enter-active {
  transition: transform 320ms cubic-bezier(0.1, 0.9, 0.2, 1), opacity 320ms ease;
}
.toast-pop-leave-active {
  transition: transform 250ms ease, opacity 250ms ease;
}
.toast-pop-enter-from,
.toast-pop-leave-to {
  transform: translateY(16px);
  opacity: 0;
}
</style>
