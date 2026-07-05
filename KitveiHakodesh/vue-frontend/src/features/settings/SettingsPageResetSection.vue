<script setup lang="ts">
import { ref } from 'vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import { useSettings } from './useSettingsPage'
import { resetting } from './appResetState'

const { resetSettings, resetSearchIndex, resetAll, resetDocumentLocatorIndex } = useSettings()

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

async function resetSettingsAndReload() {
  resetting.value = true
  await resetSettings()
  window.location.reload()
}

function confirmResetSettings() {
  confirmAction({
    label: 'איפוס ההגדרות',
    desc: 'פעולה זו תאפס את הגדרות התצוגה והקריאה לברירות המחדל. מסד הנתונים והיסטוריית הקריאה לא יושפעו.',
    action: resetSettingsAndReload,
  })
}

function confirmResetSearchIndex() {
  confirmAction({
    label: 'איפוס אינדקס החיפוש',
    desc: 'פעולה זו תמחק את אינדקס החיפוש ומטמון תוצאות החיפוש ותבנה את האינדקס מחדש. שאר נתוני האפליקציה לא יושפעו.',
    action: resetSearchIndex,
  })
}

function confirmResetDocumentLocatorIndex() {
  confirmAction({
    label: 'בנייה מחדש של אינדקס חיפוש קבצים',
    desc: 'פעולה זו תמחק את אינדקס חיפוש הקבצים ותבנה אותו מחדש מאפס. תהליך הבנייה עשוי להימשך מספר דקות.',
    action: resetDocumentLocatorIndex,
  })
}

function confirmResetAll() {
  confirmAction({
    label: 'איפוס האפליקציה',
    desc: 'פעולה זו תמחק את כל נתוני האפליקציה ואינדקס החיפוש ותטען אותה מחדש. לא ניתן לבטל פעולה זו.',
    action: async () => {
      resetting.value = true
      await resetAll()
    },
  })
}
</script>

<template>
  <div data-section="section-reset" data-section-label="איפוס">
    <div id="section-reset" class="section-label">איפוס</div>

    <p class="reset-description" data-search-ignore>
      מאפס רק את הגדרות התצוגה והקריאה לברירות המחדל. מסד הנתונים, היסטוריית הקריאה, והטאבים הפתוחים נשמרים.
    </p>
    <button class="reset-btn" @click="confirmResetSettings">איפוס ההגדרות</button>

    <p class="reset-description" data-search-ignore>
      מוחק את אינדקס החיפוש ובונה אותו מחדש. שאר נתוני האפליקציה לא יושפעו.
    </p>
    <button class="reset-btn" @click="confirmResetSearchIndex">איפוס אינדקס החיפוש</button>

    <p class="reset-description" data-search-ignore>
      מוחק את אינדקס חיפוש הקבצים ובונה אותו מחדש מאפס. תהליך הבנייה עשוי להימשך מספר דקות.
    </p>
    <button class="reset-btn" @click="confirmResetDocumentLocatorIndex">
      בנייה מחדש של אינדקס חיפוש קבצים
    </button>

    <p class="reset-description" data-search-ignore>
      מוחק את כל נתוני האפליקציה — הגדרות, היסטוריית קריאה, מיקומי גלילה, טאבים פתוחים, ואינדקס החיפוש. לא ניתן לבטל פעולה זו.
    </p>
    <button class="reset-btn" @click="confirmResetAll">איפוס האפליקציה</button>
  </div>

  <ConfirmDialog
    v-if="pendingConfirm"
    :title="pendingConfirm.label"
    :desc="pendingConfirm.desc"
    @confirm="runConfirmed"
    @cancel="cancelConfirm"
  />
</template>

<style scoped>
.reset-description {
  font-size: 12px;
  color: var(--text-secondary);
  line-height: 1.5;
  margin: 0 0 8px;
}

.reset-btn {
  width: fit-content;
  min-width: 140px;
  height: 32px;
  padding: 0 12px;
  font-size: 13px;
  color: #e53e3e;
  border: 1px solid color-mix(in srgb, #e53e3e 40%, transparent);
  background: color-mix(in srgb, #e53e3e 8%, transparent);
  margin-bottom: 12px;
}

.reset-btn:hover {
  background: color-mix(in srgb, #e53e3e 16%, transparent);
}
</style>
