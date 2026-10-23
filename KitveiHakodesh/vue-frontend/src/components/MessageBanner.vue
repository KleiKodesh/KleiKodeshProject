<script setup lang="ts">
import { computed } from 'vue'
import {
  IconErrorCircle20Regular,
  IconWarning20Regular,
  IconInfo20Regular,
  IconCheckmarkCircle20Regular,
  IconDismiss20Regular,
} from '@iconify-prerendered/vue-fluent'

// THE inline message strip of the app — every user-facing error / warning / info / success
// banner that sits inside a page or dialog goes through this component (transient corner
// messages are ToastBanner, the app-wide "service down" strip is ServiceUnavailableBanner).
//
// Design — Fluent 2 MessageBar, intent variants, fully specified:
//   Box      1px solid border, 4px radius, padding 8px 12px, gap 8px, full width of its
//            container, flex row, items start-aligned so a wrapped message keeps the icon
//            on its first line.
//   Colours  per severity, from the theme tokens so light and dark both follow the preset:
//              error    --status-danger
//              warning  --status-warning
//              info     --accent-color
//              success  --status-success
//            background = 10% of the intent colour over --bg-secondary,
//            border     = 35% of the intent colour,
//            icon       = the intent colour, text = --text-primary.
//   Icon     20px Fluent system icon on the inline-START side (RTL app => on the right):
//            ErrorCircle / Warning / Info / CheckmarkCircle.
//   Text     message 14px / 1.4, wraps (overflow-wrap: anywhere), never clipped;
//            optional `details` under it in --text-secondary, 12px / 1.4, pre-wrap so
//            multi-line detail (an exception message, a path) keeps its line breaks.
//   Dismiss  32x32 hit area holding a 20px Dismiss icon, inline-END side, --text-secondary;
//            hover = 6% --text-primary wash, pressed = 12%; focus-visible = 2px accent ring.
//   Motion   150ms fade + 4px slide-in from the inline-start edge, ease-out; none under
//            prefers-reduced-motion.
//   A11y     role="alert" + aria-live="assertive" for error, role="status" + "polite" for
//            the rest; the icon is decorative; the dismiss button carries a Hebrew label.
//
// Props: severity (default 'error'), message, details?, dismissible (default true).
// Emits: dismiss — the parent owns visibility (v-if on its own state), the banner only asks.

type Severity = 'error' | 'warning' | 'info' | 'success'

const props = withDefaults(
  defineProps<{
    severity?: Severity
    message: string
    details?: string
    dismissible?: boolean
  }>(),
  { severity: 'error', details: undefined, dismissible: true },
)

const emit = defineEmits<{ dismiss: [] }>()

const icon = computed(() => {
  switch (props.severity) {
    case 'warning': return IconWarning20Regular
    case 'info': return IconInfo20Regular
    case 'success': return IconCheckmarkCircle20Regular
    default: return IconErrorCircle20Regular
  }
})

const isAssertive = computed(() => props.severity === 'error')
</script>

<template>
  <Transition name="message-banner" appear>
    <div
      class="message-banner"
      :class="`message-banner--${severity}`"
      :role="isAssertive ? 'alert' : 'status'"
      :aria-live="isAssertive ? 'assertive' : 'polite'"
    >
      <component :is="icon" class="message-banner-icon" aria-hidden="true" />
      <div class="message-banner-text">
        <span class="message-banner-message">{{ message }}</span>
        <span v-if="details" class="message-banner-details">{{ details }}</span>
      </div>
      <button
        v-if="dismissible"
        type="button"
        class="message-banner-dismiss"
        :aria-label="$t('components.close')"
        @click="emit('dismiss')"
      >
        <IconDismiss20Regular aria-hidden="true" />
      </button>
    </div>
  </Transition>
</template>

<style scoped>
.message-banner {
  --message-banner-intent: var(--status-danger);
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 8px 12px;
  box-sizing: border-box;
  width: 100%;
  flex-shrink: 0;
  color: var(--text-primary);
  background: color-mix(in srgb, var(--message-banner-intent) 10%, var(--bg-secondary));
  border: 1px solid color-mix(in srgb, var(--message-banner-intent) 35%, transparent);
  border-radius: 4px;
  font-family: var(--header-font);
  font-size: 14px;
  line-height: 1.4;
  text-align: start;
}
.message-banner--warning { --message-banner-intent: var(--status-warning); }
.message-banner--info { --message-banner-intent: var(--accent-color); }
.message-banner--success { --message-banner-intent: var(--status-success); }

.message-banner-icon {
  flex-shrink: 0;
  width: 20px;
  height: 20px;
  /* Centres the 20px icon on the first 14px/1.4 text line (~19.6px). */
  margin-top: -0.2px;
  color: var(--message-banner-intent);
}

.message-banner-text {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.message-banner-message {
  overflow-wrap: anywhere;
  word-break: break-word;
}
.message-banner-details {
  font-size: 12px;
  line-height: 1.4;
  color: var(--text-secondary);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  word-break: break-word;
}

.message-banner-dismiss {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  /* Pulls the 32px hit area back so the icon sits on the text line and the 8px padding
     stays the visual edge. */
  margin: -6px -6px -6px 0;
  padding: 0;
  color: var(--text-secondary);
  background: none;
  border: none;
  border-radius: 4px;
  cursor: pointer;
}
.message-banner-dismiss:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.message-banner-dismiss:active {
  background: color-mix(in srgb, var(--text-primary) 12%, transparent);
}
.message-banner-dismiss:focus-visible {
  outline: 2px solid var(--accent-color);
  outline-offset: -2px;
}

.message-banner-enter-active {
  transition: opacity 150ms ease-out, transform 150ms ease-out;
}
.message-banner-enter-from {
  opacity: 0;
  /* Inline-start edge: +4px in RTL, so the slide reads as "in from the start". */
  transform: translateX(4px);
}
:dir(ltr) .message-banner-enter-from {
  transform: translateX(-4px);
}
@media (prefers-reduced-motion: reduce) {
  .message-banner-enter-active {
    transition: none;
  }
}
</style>
