<script setup lang="ts">
import { ref } from 'vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
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
/** The reset-all action's name. Single source for the button, the confirm dialog and the
 *  progress overlay — they named the same action in three separate literals before. */
const RESET_ALL_LABEL = 'איפוס האפליקציה'

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
    label: RESET_ALL_LABEL,
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
      <button class="reset-btn" @click="confirmResetAll">{{ RESET_ALL_LABEL }}</button>
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

  <!--
    A reset can take many seconds: wiping the FTS index has to wait for an in-flight
    index build to drain its flush+merge pipeline before the directory is safe to
    delete. Without this the confirm dialog just closed and the app sat there looking
    dead, so the reset read as broken and users clicked away or killed the app mid-wipe.
    Teleported to <body> because this section is inside a scrolling settings pane that
    would otherwise clip the overlay and confine it to a fraction of the screen.
  -->
  <Teleport to="body">
    <div v-if="resetting" class="reset-overlay">
      <LoadingAnimation :text="RESET_ALL_LABEL" />
    </div>
  </Teleport>
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

<!-- Unscoped on purpose: the overlay is Teleported to <body>, so it is outside this
     component's subtree and a scoped rule's data attribute would never match it. -->
<style>
.reset-overlay {
  position: fixed;
  inset: 0;
  /* Above every other overlay in the app (the highest is the toast banner at 10001):
     this one exists to be the last thing on screen before the reload. */
  z-index: 10002;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--bg-primary);
  /* The opaque full-viewport box is what actually swallows clicks aimed at the half-wiped
     UI behind it; the cursor just says why nothing is responding. */
  cursor: progress;
}
</style>
