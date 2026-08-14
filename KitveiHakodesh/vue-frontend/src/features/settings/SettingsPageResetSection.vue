<script setup lang="ts">
import { ref } from 'vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import { useSettings } from './useSettingsPage'
import { resetting, resetEverything } from './appResetState'
import { showToast } from '@/composables/useToast'

const { resetSettings, resetSearchIndex, resetDocumentLocatorIndex, resetCatalogTocIndex } =
  useSettings()

type ConfirmAction = {
  label: string
  desc: string
  action: () => Promise<void> | void
  successMessage?: string
}
const pendingConfirm = ref<ConfirmAction | null>(null)

function confirmAction(action: ConfirmAction) {
  pendingConfirm.value = action
}

async function runConfirmed() {
  if (!pendingConfirm.value) return
  const action = pendingConfirm.value
  pendingConfirm.value = null
  await action.action()
  if (action.successMessage) showToast(action.successMessage, { variant: 'success' })
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
    label: 'איפוס אינדקס החיפוש בתוכן המאגר',
    desc: 'פעולה זו תמחק את אינדקס החיפוש ומטמון תוצאות החיפוש ותבנה את האינדקס מחדש. שאר נתוני האפליקציה לא יושפעו.',
    action: resetSearchIndex,
    successMessage: 'איפוס אינדקס החיפוש בתוכן המאגר הושלם בהצלחה.',
  })
}

function confirmResetCatalogTocIndex() {
  confirmAction({
    label: 'בנייה מחדש של אינדקס החיפוש בקטלוג',
    desc: 'פעולה זו תמחק את אינדקס החיפוש בשמות הספרים ובתוכן העניינים ותבנה אותו מחדש מהמאגר. שאר נתוני האפליקציה לא יושפעו.',
    action: resetCatalogTocIndex,
    successMessage: 'איפוס אינדקס החיפוש בקטלוג הושלם. הבנייה מחדש רצה כעת ברקע.',
  })
}

function confirmResetDocumentLocatorIndex() {
  confirmAction({
    label: 'בנייה מחדש של אינדקס חיפוש קבצים',
    desc: 'פעולה זו תמחק את אינדקס חיפוש הקבצים ותבנה אותו מחדש מאפס. תהליך הבנייה עשוי להימשך מספר דקות.',
    action: resetDocumentLocatorIndex,
    successMessage: 'איפוס אינדקס חיפוש הקבצים הושלם. הבנייה מחדש רצה כעת ברקע.',
  })
}

function confirmResetAll() {
  confirmAction({
    label: 'איפוס האפליקציה',
    desc: 'פעולה זו תמחק את כל נתוני האפליקציה ואת כל אינדקסי החיפוש ותטען אותה מחדש. בניית האינדקסים מחדש עשויה להימשך מספר דקות. לא ניתן לבטל פעולה זו.',
    // resetEverything sets the `resetting` flag itself before it starts.
    action: resetEverything,
  })
}
</script>

<template>
  <div data-section="section-reset" data-section-label="איפוס">
    <div id="section-reset" class="section-label">איפוס</div>

    <div class="reset-group">
      <button class="reset-btn" @click="confirmResetSettings">איפוס ההגדרות</button>
      <p class="reset-description" data-search-ignore>
        מאפס רק את הגדרות התצוגה והקריאה לברירות המחדל. מסד הנתונים, היסטוריית הקריאה, והטאבים הפתוחים נשמרים.
      </p>
    </div>
    <div class="reset-group">
      <button class="reset-btn" @click="confirmResetSearchIndex">איפוס אינדקס החיפוש בתוכן המאגר</button>
      <p class="reset-description" data-search-ignore>
        מוחק את אינדקס החיפוש ובונה אותו מחדש. שאר נתוני האפליקציה לא יושפעו.
      </p>
    </div>
    <div class="reset-group">
      <button class="reset-btn" @click="confirmResetCatalogTocIndex">בנייה מחדש של אינדקס החיפוש בקטלוג</button>
      <p class="reset-description" data-search-ignore>
        מוחק את אינדקס החיפוש בשמות הספרים ובתוכן העניינים ובונה אותו מחדש מהמאגר. שאר נתוני האפליקציה לא יושפעו.
      </p>
    </div>
    <div class="reset-group">
      <button class="reset-btn" @click="confirmResetDocumentLocatorIndex">בנייה מחדש של אינדקס חיפוש קבצים</button>
      <p class="reset-description" data-search-ignore>
        מוחק את אינדקס חיפוש הקבצים ובונה אותו מחדש מאפס. תהליך הבנייה עשוי להימשך מספר דקות.
      </p>
    </div>
    <div class="reset-group">
      <button class="reset-btn" @click="confirmResetAll">איפוס האפליקציה</button>
      <p class="reset-description" data-search-ignore>
        מוחק את כל נתוני האפליקציה — הגדרות, היסטוריית קריאה, מיקומי גלילה, טאבים פתוחים, ואת אינדקסי החיפוש בתוכן המאגר, בקטלוג, ובקבצים. לא ניתן לבטל פעולה זו.
      </p>
    </div>
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
  margin: 4px 0 0;
}

.reset-group {
  padding: 6px 0;
}

.reset-group + .reset-group {
  margin-top: 8px;
}

.reset-btn {
  width: 100%;
  height: 28px;
  padding: 0 10px;
  font-size: 12px;
  color: var(--status-danger);
  border: 1px solid color-mix(in srgb, var(--status-danger) 40%, transparent);
  background: color-mix(in srgb, var(--status-danger) 8%, transparent);
  border-radius: 4px;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

.reset-btn:hover {
  background: color-mix(in srgb, var(--status-danger) 16%, transparent);
}
</style>
