<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { dbReady } from '@/webview-host/seforimDb'
import { getDbPathInfo } from '@/webview-host/bridge'
import { resetting } from './appResetState'
import { useSettings } from './useSettingsPage'
import SetupWizardStepDb from './SetupWizardStepDb.vue'
import SetupWizardStepTheme from './SetupWizardStepTheme.vue'
import SetupWizardStepGeneral from './SetupWizardStepGeneral.vue'
import SetupWizardStepBookDisplay from './SetupWizardStepBookDisplay.vue'
import SetupWizardStepCommentaryDisplay from './SetupWizardStepCommentaryDisplay.vue'
import SetupWizardStepShortcuts from './SetupWizardStepShortcuts.vue'

const settings = useSettingsStore()

/**
 * Wires the commentary-mirror watcher: while 'same as book' is selected, the commentary
 * font fields track the book's.
 *
 * It belongs HERE and not in the commentary step, even though that is the step the
 * setting lives on. Steps are unmounted on every navigation (the Transition below is
 * keyed on the step), so a watcher registered by a step only runs while that step is
 * on screen — and the book fonts are chosen on the PREVIOUS step, with the commentary
 * step unmounted. Registered here it lives for the whole flow, so a book font picked
 * on one step is mirrored by the time the next one renders.
 */
useSettings()

type Step =
  | 'welcome'
  | 'db'
  | 'theme'
  | 'general'
  | 'book-display'
  | 'commentary-display'
  | 'shortcuts'

// Dev (browser + service): the db step is shown when the service reports the
// resolved seforim DB doesn't exist on disk — same purpose as the hosted
// !dbReady signal, owned by the service instead of the C# host.
const devDbMissing = ref(false)
onMounted(async () => {
  if (typeof window.__webviewAction === 'function') return // hosted — dbReady covers it
  const info = await getDbPathInfo()
  if (info && !info.exists) devDbMissing.value = true
})

const steps = computed<Step[]>(() => {
  const s: Step[] = ['welcome']
  if (!dbReady.value || devDbMissing.value) s.push('db')
  s.push('theme', 'general', 'book-display', 'commentary-display', 'shortcuts')
  return s
})

/**
 * The wizard owns ONE card — header, scrolling body, nav footer — and only the body's
 * content changes from step to step. So the heading text lives here beside the
 * component rather than inside it: the header is part of the frame, and a step that
 * rendered its own would be rendering a piece of the thing that stays put.
 *
 * `welcome` has no entry — its content is inline in the template below (a logo and a
 * footnote, not a settings list), but it renders through the same card.
 */
const STEPS: Record<Step, { title: string; desc?: string; component?: unknown }> = {
  welcome: {
    title: 'ברוכים הבאים לכתבי הקודש',
    desc: 'אשף זה ילווה אותך בהגדרת האפליקציה בכמה צעדים קצרים. ניתן לשנות הכל בהמשך.',
  },
  db: {
    title: 'בחירת מסד נתונים',
    desc: 'כתבי הקודש צריכה את מסד הנתונים של אוצריא או של זית. אם אחת מהתוכנות כבר מותקנת, הפעל אותה פעם אחת לסיום ההתקנה ואז בחר את הנתיב למסד הנתונים. ניתן לשנות את הנתיב בכל עת דרך הגדרות האפליקציה.',
    component: SetupWizardStepDb,
  },
  theme: {
    title: 'איך תרצה שהאפליקציה תיראה?',
    desc: 'בחר ערכת נושא וגודל תצוגה שנוחים לך.',
    component: SetupWizardStepTheme,
  },
  general: {
    title: 'כמה הגדרות מהירות',
    desc: 'הגדרות אלו ישפיעו על חוויית הקריאה היומיומית שלך.',
    component: SetupWizardStepGeneral,
  },
  'book-display': {
    title: 'תצוגת הספרים',
    desc: 'בחר גופנים ומרווחים לתצוגת הספרים.',
    component: SetupWizardStepBookDisplay,
  },
  'commentary-display': {
    title: 'תצוגת הפירושים',
    desc: 'הפירושים יכולים להיראות כמו הספר, או לקבל גופנים ומרווחים משל עצמם.',
    component: SetupWizardStepCommentaryDisplay,
  },
  shortcuts: {
    title: 'קיצורי מקשים שכדאי להכיר',
    desc: 'ניתן לעיין בהם שוב בכל עת דרך הגדרות האפליקציה.',
    component: SetupWizardStepShortcuts,
  },
}

const stepIndex = ref(0)
// `steps` is never empty (it always starts with 'welcome') and stepIndex only ever
// moves within it, so the fallback is unreachable — it is here to keep the type
// non-optional rather than to handle a real case.
const currentStep = computed<Step>(() => steps.value[stepIndex.value] ?? 'welcome')
const currentStepMeta = computed(() => STEPS[currentStep.value])
const isLast = computed(() => stepIndex.value === steps.value.length - 1)
const dismissed = ref(false)

const progressPct = computed(() => Math.round((stepIndex.value / (steps.value.length - 1)) * 100))

function next() {
  if (!isLast.value) {
    stepIndex.value++
  } else {
    settings.completeSetup()
    dismissed.value = true
  }
}

function back() {
  stepIndex.value--
}

function skip() {
  if (resetting.value) return
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

    <!-- ── The card. It is the static frame: title, body, nav. Only what sits in the
         body changes between steps, so the nav never moves and the card never
         resizes to its content.

         Wrapper + card, matching the calendar page: the wrapper carries the centred
         max-width and the page padding, the card fills it. Below 600px the wrapper
         drops both and the card sheds its border and radius, so it fills the viewport
         edge to edge in a small window. ── -->
    <div class="wizard-card-outer">
      <div class="wizard-card">
        <!-- Header and body fade together as one unit, so the title changes with the
             content instead of snapping ahead of it. The nav footer is deliberately
             OUTSIDE this transition: it is the same on every step, and fading it would
             make the buttons flicker under the pointer. -->
        <Transition name="step-fade" mode="out-in">
          <div :key="currentStep" class="step-pane">
            <!-- The header strip is for the settings steps. The welcome step is a
                 hero: its title runs in the flow of its own content below, so it
                 renders no strip and the card is one uninterrupted section. -->
            <div v-if="currentStep !== 'welcome'" class="wizard-card-header">
              <h2 class="step-title">{{ currentStepMeta.title }}</h2>
              <p v-if="currentStepMeta.desc" class="step-desc">{{ currentStepMeta.desc }}</p>
            </div>

            <div class="wizard-card-body toc-thin-scroll">
              <div class="step-body">
                <!-- Welcome: a hero, not a settings list — logo, title and body copy
                     as one centered column. Inline here rather than in a step
                     component because it is the only step whose title is part of its
                     content. -->
                <div v-if="currentStep === 'welcome'" class="step-welcome">
                  <img src="/images/KitveiHakodesh.png" class="welcome-logo" alt="" />
                  <h1 class="welcome-title">{{ currentStepMeta.title }}</h1>
                  <p class="welcome-body">{{ currentStepMeta.desc }}</p>
                </div>
                <component v-else :is="currentStepMeta.component" />
              </div>
            </div>
          </div>
        </Transition>

        <div class="wizard-card-footer">
          <button class="skip-btn" :disabled="resetting" @click="skip">דלג</button>
          <div class="nav-btns">
            <button v-if="stepIndex > 0" class="back-btn" @click="back">הקודם</button>
            <button class="next-btn" :disabled="currentStep === 'db' && !dbReady" @click="next">
              {{ currentStep === 'welcome' ? 'התחל' : isLast ? 'סיום' : 'הבא' }}
            </button>
          </div>
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
  transition: width 0.35s ease;
}

/* ── The card ──
   One frame for every step: header, body, footer. It fills the wizard area rather
   than sizing to its content, so the nav row sits at the same height on every step.

   Split in two the way the calendar page is: the outer element owns the centred
   max-width and the page gutter, the card fills whatever that leaves. That split is
   what lets the small-viewport rules below drop the gutter and the card's own border
   independently, so the card can reach the window edges. */
.wizard-card-outer {
  flex: 1;
  min-height: 0;
  width: 100%;
  max-width: 560px;
  margin: 0 auto;
  padding: 20px 16px;
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
}

.wizard-card {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
}

.wizard-card-header {
  flex-shrink: 0;
  padding: 18px 20px 14px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  border-bottom: 1px solid var(--border-color);
}

.step-title {
  margin: 0;
  font-size: 18px;
  font-weight: 700;
  color: var(--text-primary);
  line-height: 1.25;
}

.step-desc {
  margin: 0;
  font-size: 12.5px;
  color: var(--text-secondary);
  line-height: 1.6;
}

/* The faded unit: header + scroller. It is the flex child that fills the card above
   the footer, and a column itself so the scroller can take the remaining height. */
.step-pane {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

/* The scroller. min-height:0 is what lets a flex child shrink below its content
   height and actually scroll instead of pushing the footer off the card. */
.wizard-card-body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  position: relative;
  /* Column so .step-body becomes a flex item and can be given real height with
     `flex: 1` below. A percentage (`min-height: 100%`) does NOT work here: this
     element's own height comes from `flex: 1`, which is a resolved height, not a
     specified one, so it is not a definite basis for a child's percentage — measured,
     the hero collapsed to its content height and sat near the top of the card. */
  display: flex;
  flex-direction: column;
}

/* Padding lives here, not on the scroller: on a scroller the bottom padding
   collapses against the scroll extent in some engines, and the scrollbar would
   otherwise sit inside the padding rather than against the card edge. */
.step-body {
  padding: 16px 20px;
  box-sizing: border-box;
  /* At least the scroller's height, so a step that wants to centre itself in the card
     can (the welcome hero, below); taller content grows past it and scrolls. Block
     flow inside — a grid or a centred flex column here would stretch and spread the
     settings rows of every OTHER step. */
  flex: 1 0 auto;
}

.wizard-card-footer {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 20px;
  border-top: 1px solid var(--border-color);
}

/* ── Step transitions ──
   A plain cross-fade, deliberately understated: `mode="out-in"` means the two steps
   never overlap, so nothing needs absolute positioning and a step taller than the card
   keeps its natural height and scrolls.

   out-in runs the halves in SEQUENCE, so the felt duration is both added together —
   measured end to end, not the number below. The out half is the quicker of the two:
   leaving should not hold up the advance, and the incoming step is what the eye
   follows. */
.step-fade-leave-active {
  transition: opacity 0.14s ease-in;
}
.step-fade-enter-active {
  transition: opacity 0.26s ease-out;
}
.step-fade-enter-from,
.step-fade-leave-to {
  opacity: 0;
}

/* ── Welcome step ── One centered column filling the card: the title sits in the
   flow between the logo and the body copy, with no header strip above it. */
.step-welcome {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  gap: 14px;
  padding: 24px 8px;
  box-sizing: border-box;
  /* Centres itself over the card's full height — .step-body is sized by flex, so this
     percentage has a definite basis. The ONLY step that centres, which is why the rule
     sits here and not on the shared .step-body. */
  min-height: 100%;
}

.welcome-logo {
  width: 100px;
  height: 100px;
  object-fit: contain;
}

.welcome-title {
  margin: 0;
  font-size: 24px;
  font-weight: 700;
  color: var(--text-primary);
  line-height: 1.2;
}

.welcome-body {
  margin: 0;
  font-size: 14px;
  color: var(--text-secondary);
  line-height: 1.7;
  max-width: 300px;
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
.skip-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

/* Small viewport — same treatment as the calendar page: the gutter and the max-width
   go, and the card sheds its border and radius so it fills the window in both
   directions rather than floating inside it. */
@media (max-width: 600px) {
  .wizard-card-outer {
    max-width: 100%;
    padding: 0;
  }

  .wizard-card {
    border: none;
    border-radius: 0;
  }
}

</style>
