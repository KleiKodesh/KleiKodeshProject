<script setup lang="ts">
import { ref, computed } from 'vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import { useSettings } from './useSettingsPage'
import { resetting, resetEverything } from './appResetState'
import { showToast } from '@/composables/useToast'
import { useI18n } from 'vue-i18n'

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
const { t } = useI18n()
const RESET_ALL_LABEL = computed(() => t('settings.reset.all'))

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
    label: t('settings.reset.settings'),
    desc: t('settings.reset.settingsConfirm'),
    action: resetSettingsAndReload,
  })
}

function confirmResetSearchIndex() {
  confirmAction({
    label: t('composables.index.rebuildFullTextSearch'),
    desc: t('settings.reset.searchIndexConfirm'),
    action: resetSearchIndex,
    successMessage: t('settings.reset.searchIndexDone'),
  })
}

function confirmResetCatalogTocIndex() {
  confirmAction({
    label: t('composables.index.rebuildCatalogToc'),
    desc: t('settings.reset.catalogConfirm'),
    action: resetCatalogTocIndex,
    successMessage: t('settings.reset.catalogDone'),
  })
}

function confirmResetDocumentLocatorIndex() {
  confirmAction({
    label: t('composables.index.rebuildLocalFiles'),
    desc: t('settings.reset.filesConfirm'),
    action: resetDocumentLocatorIndex,
    successMessage: t('settings.reset.filesDone'),
  })
}

function confirmResetAll() {
  confirmAction({
    label: RESET_ALL_LABEL.value,
    desc: t('settings.reset.allConfirm'),
    // resetEverything sets the `resetting` flag itself before it starts.
    action: resetEverything,
  })
}
</script>

<template>
  <div data-section="section-reset" :data-section-label="$t('settings.reset.section')">
    <div id="section-reset" class="section-label">{{ $t('settings.reset.section') }}</div>

    <div class="reset-list">
      <div class="reset-group">
        <button class="settings-btn reset-btn" @click="confirmResetSettings">{{ $t('settings.reset.settings') }}</button>
        <p class="reset-description" data-search-ignore>
          {{ $t('settings.reset.settingsDesc') }}
        </p>
      </div>
      <div class="reset-group">
        <button class="settings-btn reset-btn" @click="confirmResetSearchIndex">{{ $t('composables.index.rebuildFullTextSearch') }}</button>
        <p class="reset-description" data-search-ignore>
          {{ $t('settings.reset.searchIndexDesc') }}
        </p>
      </div>
      <div class="reset-group">
        <button class="settings-btn reset-btn" @click="confirmResetCatalogTocIndex">{{ $t('composables.index.rebuildCatalogToc') }}</button>
        <p class="reset-description" data-search-ignore>
          {{ $t('settings.reset.catalogDesc') }}
        </p>
      </div>
      <div class="reset-group">
        <button class="settings-btn reset-btn" @click="confirmResetDocumentLocatorIndex">{{ $t('composables.index.rebuildLocalFiles') }}</button>
        <p class="reset-description" data-search-ignore>
          {{ $t('settings.reset.filesDesc') }}
        </p>
      </div>
      <div class="reset-group">
        <button class="settings-btn reset-btn" @click="confirmResetAll">{{ RESET_ALL_LABEL }}</button>
        <p class="reset-description" data-search-ignore>
          {{ $t('settings.reset.allDesc') }}
        </p>
      </div>
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

/* All reset buttons share one column, as wide as the longest label: each group is
   a subgrid of this list, so their buttons line up at the same width. Overrides
   the page-wide button grid in SettingsPage.vue for this section only. */
.reset-list {
  display: grid;
  grid-template-columns: max-content 1fr;
}

.reset-list > .reset-group {
  grid-column: 1 / -1;
  display: grid;
  grid-template-columns: subgrid;
  gap: 0;
  padding: 6px 0;
}

.reset-group + .reset-group {
  margin-top: 8px;
}

.reset-btn {
  color: var(--status-danger);
  border-color: color-mix(in srgb, var(--status-danger) 40%, transparent);
  background: color-mix(in srgb, var(--status-danger) 8%, transparent);
}

.reset-btn:hover:not(:disabled) {
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
