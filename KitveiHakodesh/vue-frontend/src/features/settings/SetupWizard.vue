<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import { storeToRefs } from 'pinia'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { IconChevronDown20Regular, IconChevronUp20Regular } from '@iconify-prerendered/vue-fluent'
import { useSettingsStore } from '@/stores/settingsStore'
import { isHosted, dbReady, onWebviewEvent } from '@/webview-host/seforimDb'
import SettingRow from './SettingRow.vue'
import SliderSetting from './SliderSetting.vue'
import ToggleGroup from './ToggleGroup.vue'
import ThemePicker from './ThemePicker.vue'
import { useBookViewStore } from '@/stores/bookViewStore'
import FontDisplaySettings from './FontDisplaySettings.vue'
import { useZmanim, CITIES } from '@/features/hebrew-calendar/useZmanim'
import {
  IconFolderOpen20Regular,
  IconArrowDownload20Regular,
} from '@iconify-prerendered/vue-fluent'

const settings = useSettingsStore()
const {
  censorDivineNames,
  appZoom,
  newTabPage,
  resumeLastRead,
  headerFont,
  textFont,
  fontSize,
  linePadding,
  commentaryHeaderFont,
  commentaryTextFont,
  commentaryFontSize,
  commentaryLinePadding,
  useSeparateCommentarySettings,
  defaultAutoSyncCommentary,
} = storeToRefs(settings)

const bookViewStore = useBookViewStore()
const { toolbarPosition, autoSelectTopLine } = storeToRefs(bookViewStore)

const { activeCity, setCity, init: initZmanim } = useZmanim()

const cityBoxRef = ref<HTMLElement | null>(null)
const cityDropdownRef = ref<HTMLElement | null>(null)
const cityOpen = ref(false)
const cityDropdownStyle = ref<Record<string, string>>({})

useDropdownClose(
  cityDropdownRef,
  (e) => {
    if (cityBoxRef.value?.contains((e as MouseEvent).target as Node)) return
    cityOpen.value = false
  },
  { ignore: [cityBoxRef] },
)

async function toggleCityDropdown() {
  if (cityOpen.value) {
    cityOpen.value = false
    return
  }
  cityOpen.value = true
  await nextTick()
  if (!cityBoxRef.value || !cityDropdownRef.value) return
  const rect = cityBoxRef.value.getBoundingClientRect()
  const spaceBelow = window.innerHeight - rect.bottom - 8
  const spaceAbove = rect.top - 8
  const goUp = spaceAbove > spaceBelow
  const maxH = Math.min(240, goUp ? spaceAbove : spaceBelow)
  cityDropdownRef.value.style.maxHeight = maxH + 'px'
  cityDropdownStyle.value = {
    position: 'fixed',
    left: rect.left + 'px',
    width: rect.width + 'px',
    zIndex: '10000',
    ...(goUp
      ? { bottom: window.innerHeight - rect.top + 4 + 'px', top: 'auto' }
      : { top: rect.bottom + 4 + 'px', bottom: 'auto' }),
  }
}

function pickCity(name: string) {
  setCity(CITIES.find((c) => c.name === name) ?? null)
  cityOpen.value = false
}

type Step = 'welcome' | 'db' | 'theme' | 'general' | 'book-display'

const steps = computed<Step[]>(() => {
  const s: Step[] = ['welcome']
  if (isHosted && !dbReady.value) s.push('db')
  s.push('theme', 'general', 'book-display')
  return s
})

const stepIndex = ref(0)
const currentStep = computed(() => steps.value[stepIndex.value])
const isLast = computed(() => stepIndex.value === steps.value.length - 1)
const direction = ref<'forward' | 'back'>('forward')

const stepTitles: Record<Step, string> = {
  welcome: 'ברוכים הבאים לכתבי הקודש',
  db: 'בחירת מסד נתונים',
  theme: 'איך תרצה שהאפליקציה תיראה?',
  general: 'כמה הגדרות מהירות',
  'book-display': 'תצוגת הספרים',
}

const stepDescs: Record<Step, string> = {
  welcome: '',
  db: 'כתבי הקודש צריכה את מסד הנתונים של זית או של אוצריא. אם אחת מהתוכנות כבר מותקנת, הפעל אותה פעם אחת לסיום ההתקנה ואז בחר את הנתיב למסד הנתונים. ניתן לשנות את הנתיב בכל עת דרך הגדרות האפליקציה.',
  theme: 'בחר ערכת נושא וגודל תצוגה שנוחים לך.',
  general: 'הגדרות אלו ישפיעו על חוויית הקריאה היומיומית שלך.',
  'book-display': 'בחר גופנים ומרווחים לתצוגת הספרים והפירושים.',
}

const bookDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)
const commentaryDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)

const dbPath = ref(dbReady.value ? (window.__webviewDbPath ?? '') : '')
const dismissed = ref(false)

onMounted(() => {
  initZmanim()
  const unregister = onWebviewEvent((msg) => {
    if (msg.event === 'dbPathPicked') dbPath.value = msg.path as string
  })
  onUnmounted(unregister)
})

function pickDbPath() {
  window.__webviewPickDbPath?.()
}

// NOTE: "זית" (Zayit) here refers to the external Zayit app (zayitapp.com) — a separate
// Torah study program whose database this app can use. This is NOT this app's old name.
// Do not rename or remove this function or URL.
function downloadZayit() {
  window.open('https://zayitapp.com/#/download', '_blank')
}

function downloadOtzaria() {
  window.open('https://www.otzaria.org/#download', '_blank')
}

function next() {
  direction.value = 'forward'
  if (stepIndex.value < steps.value.length - 1) stepIndex.value++
  else {
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

const progressPct = computed(() => Math.round((stepIndex.value / (steps.value.length - 1)) * 100))
</script>

<template>
  <div v-if="!dismissed" class="wizard-root">
    <!-- Progress bar — top edge, no chrome -->
    <div class="progress-track">
      <div class="progress-fill" :style="{ width: progressPct + '%' }" />
    </div>

    <!-- Scrollable content -->
    <div class="wizard-scroll">
      <Transition :name="direction === 'forward' ? 'slide-fwd' : 'slide-back'" mode="out-in">
        <div :key="currentStep" class="step-root">
          <!-- WELCOME -->
          <div v-if="currentStep === 'welcome'" class="step-welcome">
            <img src="/images/KitveiHakodesh.png" class="welcome-logo" alt="" />
            <h1 class="welcome-title">ברוכים הבאים לכתבי הקודש</h1>
            <p class="welcome-body">
              אשף זה ילווה אותך בהגדרת האפליקציה בכמה צעדים קצרים. ניתן לשנות הכל בהמשך.
            </p>
            <p class="welcome-note">
              זית היא תוכנת לימוד תורה חינמית ופתוחה עם ספרייה ענפה של ספרי קודש. כתבי הקודש הוא תוסף
              עצמאי לזית עבור וורד. משתמשי אוצריא יכולים גם הם להשתמש בכתבי הקודש על ידי הגדרת הנתיב למסד
              הנתונים של אוצריא.
            </p>
          </div>

          <!-- DB -->
          <div v-else-if="currentStep === 'db'" class="step-content">
            <div class="step-sticky">
              <h2 class="step-title">{{ stepTitles['db'] }}</h2>
              <p class="step-desc">{{ stepDescs['db'] }}</p>
            </div>
            <div class="settings-block">
              <button class="db-pick-card" @click="downloadZayit">
                <IconArrowDownload20Regular class="db-card-icon" />
                <span class="db-card-path placeholder">הורד את זית</span>
              </button>
              <button class="db-pick-card" @click="downloadOtzaria">
                <IconArrowDownload20Regular class="db-card-icon" />
                <span class="db-card-path placeholder">הורד את אוצריא</span>
              </button>
              <button class="db-pick-card" @click="pickDbPath">
                <IconFolderOpen20Regular class="db-card-icon" />
                <span class="db-card-path" :class="{ placeholder: !dbPath }">
                  {{ dbPath || 'בחר קובץ מסד נתונים' }}
                </span>
              </button>
            </div>
          </div>

          <!-- THEME -->
          <div v-else-if="currentStep === 'theme'" class="step-content">
            <div class="step-sticky">
              <h2 class="step-title">{{ stepTitles['theme'] }}</h2>
              <p class="step-desc">{{ stepDescs['theme'] }}</p>
            </div>
            <div class="settings-block">
              <SettingRow label="ערכת נושא">
                <ThemePicker />
              </SettingRow>
              <SliderSetting
                label="גודל תצוגה"
                v-model="appZoom"
                :min="0.5"
                :max="1.5"
                :step="0.05"
              />
            </div>
          </div>

          <!-- GENERAL -->
          <div v-else-if="currentStep === 'general'" class="step-content">
            <div class="step-sticky">
              <h2 class="step-title">{{ stepTitles['general'] }}</h2>
              <p class="step-desc">{{ stepDescs['general'] }}</p>
            </div>
            <div class="settings-block">
              <SettingRow label="כיסוי שם ה'">
                <ToggleGroup
                  v-model="censorDivineNames"
                  :options="[
                    { label: 'כתיב מלא', value: false },
                    { label: 'כיסוי (ה←ד)', value: true },
                  ]"
                />
              </SettingRow>
              <SettingRow
                label="זכור מיקום אחרון בספר"
                title="בפתיחת ספר מחדש, האפליקציה תחזור אוטומטית למקום שבו הפסקת לקרוא"
              >
                <ToggleGroup
                  v-model="resumeLastRead"
                  :options="[
                    { label: 'כן', value: true },
                    { label: 'לא', value: false },
                  ]"
                />
              </SettingRow>
              <SettingRow label="פתח טאב חדש אל" wrap>
                <ToggleGroup
                  v-model="newTabPage"
                  :options="[
                    { label: 'דף הבית', value: 'homepage' },
                    { label: 'פתיחת ספר', value: 'openfile' },
                    { label: 'היברו בוקס', value: 'hebrewbooks' },
                    { label: 'חיפוש', value: 'search' },
                  ]"
                />
              </SettingRow>
              <SettingRow label="מיקום סרגל הכלים בתצוגת ספר" wrap>
                <ToggleGroup
                  v-model="toolbarPosition"
                  :options="[
                    { label: 'למעלה', value: 'top' },
                    { label: 'למטה', value: 'bottom' },
                    { label: 'שמאל', value: 'left' },
                    { label: 'ימין', value: 'right' },
                  ]"
                  @update:model-value="bookViewStore.setToolbarPosition($event)"
                />
              </SettingRow>
              <SettingRow label="סנכרן מפרשים כברירת מחדל">
                <ToggleGroup
                  v-model="defaultAutoSyncCommentary"
                  :options="[
                    { label: 'כן', value: true },
                    { label: 'לא', value: false },
                  ]"
                />
              </SettingRow>
              <SettingRow label="עיר לזמני היום" hint="העיר שלפיה יחושבו זמני היום בלוח השנה">
                <div ref="cityBoxRef" class="select-box" tabindex="0" @click="toggleCityDropdown">
                  <span class="select-display">{{ activeCity.name }}</span>
                  <component
                    :is="cityOpen ? IconChevronUp20Regular : IconChevronDown20Regular"
                    class="select-chevron"
                  />
                </div>
                <Teleport to="body">
                  <div
                    v-if="cityOpen"
                    ref="cityDropdownRef"
                    class="city-dropdown"
                    :style="cityDropdownStyle"
                    @click.stop
                  >
                    <div
                      v-for="c in CITIES"
                      :key="c.name"
                      class="city-option"
                      :class="{ selected: activeCity.name === c.name }"
                      @click="pickCity(c.name)"
                    >
                      {{ c.name }}
                    </div>
                  </div>
                </Teleport>
              </SettingRow>
            </div>
          </div>

          <!-- BOOK DISPLAY -->
          <div v-else-if="currentStep === 'book-display'" class="step-content">
            <div class="step-sticky">
              <h2 class="step-title">{{ stepTitles['book-display'] }}</h2>
              <p class="step-desc">{{ stepDescs['book-display'] }}</p>
              <div
                class="reading-preview"
                :style="{
                  fontFamily: textFont,
                  fontSize: fontSize * 0.14 + 'px',
                  lineHeight: linePadding,
                }"
              >
                <div class="reading-preview-header" :style="{ fontFamily: headerFont }">
                  אבות פרק א
                </div>
                <div class="reading-preview-body">
                  משה קיבל תורה מסיני ומסרה ליהושע ויהושע לזקנים וזקנים לנביאים ונביאים מסרוה לאנשי
                  כנסת הגדולה. הם אמרו שלשה דברים הוו מתונים בדין והעמידו תלמידים הרבה ועשו סייג
                  לתורה. שמעון הצדיק היה משירי כנסת הגדולה הוא היה אומר על שלשה דברים העולם עומד על
                  התורה ועל העבודה ועל גמילות חסדים.
                </div>
              </div>
            </div>
            <div class="settings-block">
              <FontDisplaySettings
                ref="bookDisplayRef"
                v-model:header-font="headerFont"
                v-model:text-font="textFont"
                v-model:font-size="fontSize"
                v-model:line-padding="linePadding"
              />
              <SettingRow label="תצוגת פירושים">
                <ToggleGroup
                  v-model="useSeparateCommentarySettings"
                  :options="[
                    { label: 'זהה לתצוגת ספר', value: false },
                    { label: 'הגדרות נפרדות לפירושים', value: true },
                  ]"
                />
              </SettingRow>
              <FontDisplaySettings
                v-if="useSeparateCommentarySettings"
                ref="commentaryDisplayRef"
                v-model:header-font="commentaryHeaderFont"
                v-model:text-font="commentaryTextFont"
                v-model:font-size="commentaryFontSize"
                v-model:line-padding="commentaryLinePadding"
              />
            </div>
          </div>
        </div>
      </Transition>
    </div>

    <!-- Bottom bar -->
    <div class="wizard-footer">
      <button class="skip-btn" @click="skip">דלג</button>
      <div class="nav-btns">
        <button v-if="stepIndex > 0" class="back-btn" @click="back">הקודם</button>
        <button class="next-btn" :disabled="currentStep === 'db' && !dbReady" @click="next">
          {{ currentStep === 'welcome' ? 'התחל' : isLast ? 'סיום' : 'הבא' }}
        </button>
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

/* ── Scroll area ── */
.wizard-scroll {
  flex: 1;
  overflow: hidden;
  position: relative;
}

.step-root {
  height: 100%;
}

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

.reading-preview {
  padding: 10px 14px;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-secondary);
  color: var(--text-primary);
  direction: rtl;
  text-align: justify;
  animation: fade-up 0.25s 0.05s ease both;
  overflow: hidden;
}

.reading-preview-header {
  font-size: 1.15em;
  font-weight: 600;
  margin-bottom: 4px;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.reading-preview-body {
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.step-content {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.step-sticky {
  flex-shrink: 0;
  padding: 28px 28px 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.settings-block {
  flex: 1;
  overflow-y: auto;
  padding: 0 28px 16px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.step-title {
  margin: 0;
  font-size: 20px;
  font-weight: 700;
  color: var(--text-primary);
  line-height: 1.2;
  animation: fade-up 0.25s ease both;
}

.step-desc {
  margin: 0 0 8px;
  font-size: 13px;
  color: var(--text-secondary);
  line-height: 1.6;
  animation: fade-up 0.25s 0.05s ease both;
}

.settings-block {
  animation: fade-up 0.25s 0.1s ease both;
}

.db-pick-card {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 0 10px;
  height: 32px;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-secondary);
  color: var(--text-primary);
  cursor: pointer;
  transition:
    border-color 0.15s,
    background 0.15s;
  animation: fade-up 0.25s 0.1s ease both;
}
.db-pick-card:hover {
  border-color: var(--accent-color);
  background: color-mix(in srgb, var(--accent-color) 6%, transparent);
}

.db-card-icon {
  flex-shrink: 0;
  color: var(--text-secondary);
}

.db-card-path {
  flex: 1;
  font-size: 11px;
  color: var(--text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  direction: ltr;
  text-align: left;
  min-width: 0;
}
.db-otzaria-note {
  margin: 0;
  font-size: 11px;
  color: var(--text-secondary);
  line-height: 1.5;
  opacity: 0.7;
  animation: fade-up 0.25s 0.2s ease both;
}

.db-card-path.placeholder {
  direction: rtl;
  text-align: right;
  font-style: italic;
}

.select-box {
  display: flex;
  align-items: center;
  width: 100%;
  height: 28px;
  padding: 0 8px;
  cursor: pointer;
  user-select: none;
  box-sizing: border-box;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
}
.select-box:hover {
  border-color: var(--accent-color);
}
.select-display {
  flex: 1;
  font-size: 12px;
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.select-chevron {
  color: var(--text-secondary);
  flex-shrink: 0;
}

/* ── Footer ── */
.wizard-footer {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 6px 10px;
  border-top: 1px solid var(--border-color);
  background: var(--bg-toolbar);
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
