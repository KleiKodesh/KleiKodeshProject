<script setup lang="ts">
import { ref, watch } from 'vue'
import { t } from '@/i18n'
import { answerLeavePrompt, leavePrompt } from './siteFrames'

/**
 * Asked when the reader leaves a heavy website (siteFrames.decideOnLeave): keep it alive in the
 * background or free its memory. Never blocks anything; ignored, it fades and the site stays.
 */
const AUTO_DISMISS_MS = 10000
const remember = ref(false)
let timer: ReturnType<typeof setTimeout> | null = null

watch(leavePrompt, (prompt) => {
  if (timer) clearTimeout(timer)
  timer = null
  remember.value = false
  if (prompt) timer = setTimeout(() => answerLeavePrompt('keep', false), AUTO_DISMISS_MS)
})

function answer(choice: 'keep' | 'unload') {
  answerLeavePrompt(choice, remember.value)
}
</script>

<template>
  <div v-if="leavePrompt" class="site-leave-prompt" role="alertdialog" :aria-label="t('websites.leaveTitle', { site: leavePrompt.siteName, mb: leavePrompt.mb })">
    <div class="site-leave-prompt__text">
      <strong>{{ t('websites.leaveTitle', { site: leavePrompt.siteName, mb: leavePrompt.mb }) }}</strong>
      <span v-if="leavePrompt.typed" class="site-leave-prompt__warn">{{ t('websites.leaveTypedWarning') }}</span>
    </div>
    <div class="site-leave-prompt__actions">
      <button type="button" class="site-leave-prompt__keep" @click="answer('keep')">{{ t('websites.keepOpen') }}</button>
      <button type="button" class="site-leave-prompt__unload" @click="answer('unload')">{{ t('websites.unloadNow') }}</button>
      <label class="site-leave-prompt__remember">
        <input v-model="remember" type="checkbox">
        {{ t('websites.rememberForSite') }}
      </label>
    </div>
  </div>
</template>

<style scoped>
.site-leave-prompt {
  position: fixed;
  inset-block-end: 16px;
  inset-inline-end: 16px;
  z-index: 50;
  max-width: 360px;
  padding: 12px 14px;
  border-radius: 8px;
  border: 1px solid var(--border-color);
  background: var(--bg-primary, #fff);
  color: var(--text-primary);
  box-shadow: 0 6px 24px rgb(0 0 0 / 0.18);
  display: flex;
  flex-direction: column;
  gap: 10px;
  font-size: 13px;
}
.site-leave-prompt__text {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.site-leave-prompt__warn {
  color: var(--status-danger, #c42b1c);
}
.site-leave-prompt__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.site-leave-prompt__actions button {
  padding: 4px 12px;
  border-radius: 4px;
  border: 1px solid var(--border-color);
  background: var(--bg-secondary, transparent);
  color: inherit;
  cursor: pointer;
}
.site-leave-prompt__remember {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: var(--text-secondary);
}
</style>
