<script setup lang="ts">
import { ref } from 'vue'
import { useSettingsPage } from './useSettingsPage'
import { useSettingsStore } from '@/stores/settingsStore'
import ConfirmDialog from '@/components/common/ConfirmDialog.vue'
import SettingsGeneralPane from './SettingsGeneralPane.vue'
import SettingsReadingPane from './SettingsReadingPane.vue'
import { resetting } from '@/utils/resetState'

const { resetSettings, resetAll } = useSettingsPage()
const settings = useSettingsStore()

const activeTab = ref<'general' | 'reading' | 'reset'>('general')

const tabIndexMap = { general: 0, reading: 1, reset: 2 } as const
const tabCount = 3

type ConfirmAction = { label: string; desc: string; action: () => Promise<void> | void }
const pendingConfirm = ref<ConfirmAction | null>(null)

function confirmAction(action: ConfirmAction) {
  pendingConfirm.value = action
}

async function runConfirmed() {
  if (!pendingConfirm.value) return
  const action = pendingConfirm.value
  pendingConfirm.value = null
  await action.action()
}

function cancelConfirm() {
  pendingConfirm.value = null
}

function resetSettingsAndReload() {
  resetting.value = true
  resetSettings()
  settings.completeSetup()
  window.location.reload()
}
function confirmResetAll() {
  confirmAction({
    label: 'איפוס האפליקציה',
    desc: 'פעולה זו תמחק את כל נתוני האפליקציה ואינדקס החיפוש ותטען אותה מחדש. לא ניתן לבטל פעולה זו.',
    action: () => {
      resetting.value = true
      resetAll()
    },
  })
}
function confirmResetSettings() {
  confirmAction({
    label: 'איפוס ההגדרות',
    desc: 'פעולה זו תאפס את הגדרות התצוגה והקריאה לברירות המחדל. מסד הנתונים והיסטוריית הקריאה לא יושפעו.',
    action: resetSettingsAndReload,
  })
}
</script>

<template>
  <div class="settings-page">
    <div
      class="tab-bar"
      :style="{ '--tab-index': tabIndexMap[activeTab], '--tab-count': tabCount }"
    >
      <button
        :class="['tab-btn', { active: activeTab === 'general' }]"
        @click="activeTab = 'general'"
      >
        כללי
      </button>
      <button
        :class="['tab-btn', { active: activeTab === 'reading' }]"
        @click="activeTab = 'reading'"
      >
        קריאה
      </button>
      <button :class="['tab-btn', { active: activeTab === 'reset' }]" @click="activeTab = 'reset'">
        איפוס
      </button>
    </div>

    <div v-if="activeTab === 'general'" class="pane">
      <SettingsGeneralPane />
    </div>

    <div v-if="activeTab === 'reading'" class="pane">
      <SettingsReadingPane />
    </div>

    <div v-if="activeTab === 'reset'" class="pane reset-pane">
      <p class="reset-desc">
        מאפס רק את הגדרות התצוגה והקריאה לברירות המחדל. מסד הנתונים, היסטוריית הקריאה, והטאבים
        הפתוחים נשמרים.
      </p>
      <button class="reset-all-btn" @click="confirmResetSettings">איפוס ההגדרות</button>
      <p class="reset-desc">
        מוחק את כל נתוני האפליקציה — הגדרות, היסטוריית קריאה, מיקומי גלילה, טאבים פתוחים, ואינדקס
        החיפוש. לא ניתן לבטל פעולה זו.
      </p>
      <button class="reset-all-btn" @click="confirmResetAll">איפוס האפליקציה</button>
    </div>

    <ConfirmDialog
      v-if="pendingConfirm"
      :title="pendingConfirm.label"
      :desc="pendingConfirm.desc"
      @confirm="runConfirmed"
      @cancel="cancelConfirm"
    />
  </div>
</template>

<style scoped>
.settings-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  direction: rtl;
  background: var(--bg-primary);
}

.tab-bar {
  display: flex;
  flex-shrink: 0;
  border-bottom: 1px solid var(--border-color);
  background: var(--bg-toolbar);
  position: relative;
}

.tab-bar::after {
  content: '';
  position: absolute;
  bottom: 0;
  height: 2px;
  background: var(--accent-color);
  width: calc(100% / var(--tab-count));
  /* RTL: tab 0 is on the right, so index 0 = right edge */
  right: calc(var(--tab-index) * 100% / var(--tab-count));
  transition: right 0.2s cubic-bezier(0.4, 0, 0.2, 1);
}

.tab-btn {
  flex: 1;
  padding: 0 12px;
  height: 32px;
  border: none;
  border-bottom: 2px solid transparent;
  border-radius: 0;
  background: transparent;
  color: var(--text-secondary);
  font-size: 12px;
  cursor: pointer;
}
.tab-btn:hover {
  background: var(--hover-bg);
  color: var(--text-primary);
}
.tab-btn:active {
  transform: none;
}
.tab-btn.active {
  color: var(--text-primary);
  border-bottom-color: transparent;
}

.reset-pane {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 12px;
}
.reset-desc {
  font-size: 12px;
  color: var(--text-secondary);
  line-height: 1.5;
  margin: 0;
}
.reset-all-btn {
  width: 140px;
  height: 32px;
  font-size: 13px;
  color: #e53e3e;
  border: 1px solid color-mix(in srgb, #e53e3e 40%, transparent);
  background: color-mix(in srgb, #e53e3e 8%, transparent);
}
.reset-all-btn:hover {
  background: color-mix(in srgb, #e53e3e 16%, transparent);
}

.pane {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 12px 16px;
}
</style>
