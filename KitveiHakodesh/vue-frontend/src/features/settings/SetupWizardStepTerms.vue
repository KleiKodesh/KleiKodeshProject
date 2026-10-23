<script setup lang="ts">
/**
 * Terms of use / copyright notice. Mandatory: the wizard hides Skip and keeps Next disabled
 * on this step until the checkbox is ticked (see SetupWizard.vue). The checkbox itself is
 * disabled until the terms box has been scrolled to the bottom.
 */
import { ref, watch } from 'vue'
import { useScroll } from '@vueuse/core'

const agreed = defineModel<boolean>({ default: false })

// The checkbox stays disabled until the user has scrolled to the bottom of the terms once.
const termsBox = ref<HTMLElement | null>(null)
const { arrivedState, measure } = useScroll(termsBox, { offset: { bottom: 4 } })
const reachedBottom = ref(agreed.value)
watch(termsBox, (el) => el && measure(), { flush: 'post' })
watch(
  () => arrivedState.bottom,
  (atBottom) => {
    if (atBottom) reachedBottom.value = true
  },
  { immediate: true },
)
</script>

<template>
  <div class="terms-step">
    <div ref="termsBox" class="terms-box toc-thin-scroll" tabindex="0">
      <h3>{{ $t('settings.terms.p0') }}</h3>
      <p>{{ $t('settings.terms.p1') }}</p>

      <h3>{{ $t('settings.terms.p2') }}</h3>
      <p>{{ $t('settings.terms.p3') }}</p>
      <p>{{ $t('settings.terms.p4') }}</p>
      <p>{{ $t('settings.terms.p5') }}</p>

      <h3>{{ $t('settings.terms.p6') }}</h3>
      <p>{{ $t('settings.terms.p7') }}</p>

      <h3>{{ $t('settings.terms.p8') }}</h3>
      <p>{{ $t('settings.terms.p9') }}</p>

      <h3>{{ $t('settings.terms.p10') }}</h3>
      <p>{{ $t('settings.terms.p11') }}</p>

      <h3>{{ $t('settings.terms.p12') }}</h3>
      <p>{{ $t('settings.terms.p13') }}</p>
      <p>{{ $t('settings.terms.p14') }}</p>
    </div>

    <label class="terms-agree" :class="{ disabled: !reachedBottom }">
      <input v-model="agreed" type="checkbox" :disabled="!reachedBottom" />
      <span>{{ $t('settings.terms.agree') }}</span>
    </label>
  </div>
</template>

<style scoped>
.terms-step {
  display: flex;
  flex-direction: column;
  gap: 12px;
  flex: 1;
  min-height: 0;
}

.terms-box {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 12px 14px;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-primary);
  font-size: 13px;
  line-height: 1.7;
  color: var(--text-primary);
}

.terms-box h3 {
  margin: 12px 0 4px;
  font-size: 13.5px;
  font-weight: 700;
}
.terms-box h3:first-child {
  margin-top: 0;
}
.terms-box p {
  margin: 0 0 6px;
  color: var(--text-secondary);
}

.terms-agree {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
  cursor: pointer;
  user-select: none;
}
.terms-agree input {
  accent-color: var(--accent-color);
  width: 15px;
  height: 15px;
  cursor: pointer;
}
.terms-agree.disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.terms-agree.disabled input {
  cursor: not-allowed;
}
</style>
