<script setup lang="ts">
import { ref, computed } from 'vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { isHosted, dbReady } from '@/webview-host/seforimDb'
import SetupWizardStepDb from './SetupWizardStepDb.vue'
import SetupWizardStepTheme from './SetupWizardStepTheme.vue'
import SetupWizardStepGeneral from './SetupWizardStepGeneral.vue'
import SetupWizardStepBookDisplay from './SetupWizardStepBookDisplay.vue'

const settings = useSettingsStore()

type Step = 'welcome' | 'db' | 'theme' | 'general' | 'book-display'

const steps = computed<Step[]>(() => {
  const s: Step[] = ['welcome']
  if (isHosted && !dbReady.value) s.push('db')
  s.push('theme', 'general', 'book-display')
  return s
})

const stepComponents: Record<Exclude<Step, 'welcome'>, unknown> = {
  db: SetupWizardStepDb,
  theme: SetupWizardStepTheme,
  general: SetupWizardStepGeneral,
  'book-display': SetupWizardStepBookDisplay,
}

const stepIndex = ref(0)
const currentStep = computed(() => steps.value[stepIndex.value])
const isLast = computed(() => stepIndex.value === steps.value.length - 1)
const direction = ref<'forward' | 'back'>('forward')
const dismissed = ref(false)

const progressPct = computed(() => Math.round((stepIndex.value / (steps.value.length - 1)) * 100))

function next() {
  direction.value = 'forward'
  if (!isLast.value) {
    stepIndex.value++
  } else {
    settings.completeSetup()
    dismissed.value = true
  }
}

function back() {
  direction.value = 'back'
  stepIndex.value--
}

function skip() {
  settings.completeSetup()
  dismissed.value = true
}
</script>

<template>
  <div v-if="!dismissed" class="wizard-root">
    <!-- Progress bar -->
    <div class="progress-track">
      <div class="progress-fill" :style="{ width: progressPct + '%' }" />
    </div>

    <!-- Step content -->
    <div class="wizard-scroll">
      <Transition :name="direction === 'forward' ? 'slide-fwd' : 'slide-back'" mode="out-in">
        <div :key="currentStep" class="step-root">
          <!-- Welcome -->
          <div v-if="currentStep === 'welcome'" class="step-welcome">
            <img src="/images/KitveiHakodesh.png" class="welcome-logo" alt="" />
            <h1 class="welcome-title">ברוכים הבאים לכתבי הקודש</h1>
            <p class="welcome-body">
              אשף זה ילווה אותך בהגדרת האפליקציה בכמה צעדים קצרים. ניתן לשנות הכל בהמשך.
            </p>
            <p class="welcome-note">
              זית היא תוכנת לימוד תורה חינמית ופתוחה עם ספרייה ענפה של ספרי קודש. כתבי הקודש הוא
              תוסף עצמאי לזית עבור וורד. משתמשי אוצריא יכולים גם הם להשתמש בכתבי הקודש על ידי
              הגדרת הנתיב למסד הנתונים של אוצריא.
            </p>
          </div>

          <!-- All other steps -->
          <component :is="stepComponents[currentStep as Exclude<Step, 'welcome'>]" v-else />
        </div>
      </Transition>
    </div>

    <!-- Footer — centered to match card width -->
    <div class="wizard-footer">
      <div class="wizard-footer-inner">
        <button class="skip-btn" @click="skip">דלג</button>
        <div class="nav-btns">
          <button v-if="stepIndex > 0" class="back-btn" @click="back">הקודם</button>
          <button class="next-btn" :disabled="currentStep === 'db' && !dbReady" @click="next">
            {{ currentStep === 'welcome' ? 'התחל' : isLast ? 'סיום' : 'הבא' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.wizard-root {
  position: fixed;
  inset: 0;
  z-index: 1000;
  display: flex;
  flex-direction: column;
  direction: rtl;
  background: var(--bg-primary);
}

/* ── Progress bar ── */
.progress-track {
  flex-shrink: 0;
  height: 3px;
  background: var(--border-color);
}
.progress-fill {
  height: 100%;
  background: var(--accent-color);
  transition: width 0.4s cubic-bezier(0.4, 0, 0.2, 1);
}

/* ── Step area ── */
.wizard-scroll {
  flex: 1;
  overflow: hidden;
  position: relative;
}

.step-root {
  height: 100%;
}

/* ── Step transitions ── */
.slide-fwd-enter-active,
.slide-fwd-leave-active,
.slide-back-enter-active,
.slide-back-leave-active {
  transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  position: absolute;
  width: 100%;
  height: 100%;
}
.slide-fwd-enter-from {
  transform: translateX(-100%);
}
.slide-fwd-leave-to {
  transform: translateX(100%);
}
.slide-back-enter-from {
  transform: translateX(100%);
}
.slide-back-leave-to {
  transform: translateX(-100%);
}

/* ── Welcome step ── */
.step-welcome {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  padding: 48px 28px 28px;
  gap: 14px;
  height: 100%;
  overflow-y: auto;
}

.welcome-logo {
  width: 100px;
  height: 100px;
  object-fit: contain;
  margin-bottom: 8px;
  animation: fade-up 0.3s ease both;
}

.welcome-title {
  margin: 0;
  font-size: 26px;
  font-weight: 700;
  color: var(--text-primary);
  line-height: 1.2;
  animation: fade-up 0.3s 0.05s ease both;
}

.welcome-body {
  margin: 0;
  font-size: 14px;
  color: var(--text-secondary);
  line-height: 1.7;
  max-width: 300px;
  animation: fade-up 0.3s 0.1s ease both;
}

.welcome-note {
  font-size: 11px;
  color: var(--text-secondary);
  line-height: 1.6;
  text-align: center;
  max-width: 300px;
  opacity: 0.65;
  animation: fade-up 0.3s 0.15s ease both;
}

/* ── Footer ── */
.wizard-footer {
  flex-shrink: 0;
  padding: 8px 16px 12px;
  background: var(--bg-primary);
}

.wizard-footer-inner {
  max-width: 560px;
  margin: 0 auto;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.nav-btns {
  display: flex;
  align-items: center;
  gap: 8px;
}

.next-btn {
  height: 28px;
  padding: 0 16px;
  font-size: 12px;
  font-weight: 600;
  background: var(--accent-color);
  color: #fff;
  border: none;
  border-radius: 4px;
}
.next-btn:hover {
  background: color-mix(in srgb, var(--accent-color) 82%, #000);
}
.next-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.back-btn {
  height: 28px;
  padding: 0 12px;
  font-size: 12px;
  background: transparent;
  color: var(--text-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
}
.back-btn:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.skip-btn {
  height: 28px;
  padding: 0 8px;
  font-size: 12px;
  background: transparent;
  color: var(--text-secondary);
  border: none;
  border-radius: 4px;
}
.skip-btn:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

@keyframes fade-up {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
