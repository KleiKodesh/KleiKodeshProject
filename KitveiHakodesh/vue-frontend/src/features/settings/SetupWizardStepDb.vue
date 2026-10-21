<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { onDbPathChanged } from '@/webview-host/seforimDb'
import MessageBanner from '@/components/MessageBanner.vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
import { getDbPathInfo, setDbPath, pickDbPath as pickDbPathWithDialog } from '@/webview-host/bridge'
import {
  IconFolderOpen20Regular,
  IconArrowDownload20Regular,
} from '@iconify-prerendered/vue-fluent'

// The KitveiHakodesh service owns the path: it shows the native dialog (or validates a
// typed path), persists it to the registry and restarts on the new DB (rebuilding a
// stale search index). Either way the page reloads afterwards.
const dbPath = ref('')
const devInput = ref('')
const devError = ref('')

onMounted(() => {
  getDbPathInfo().then((info) => {
    if (info?.exists) dbPath.value = info.path
    if (info) devInput.value = info.path
  })
})

async function pickDbPath() {
  const previous = (await getDbPathInfo())?.path ?? ''
  const picked = await pickDbPathWithDialog()
  if (!picked) return
  dbPath.value = picked
  await onDbPathChanged(previous, picked)
}

async function applyDevPath() {
  const p = devInput.value.trim()
  if (!p) return
  devError.value = ''
  try {
    // Asked BEFORE the switch: the service is the authority on the path in use.
    const previous = (await getDbPathInfo())?.path ?? ''
    await setDbPath(p)
    dbPath.value = p
    await onDbPathChanged(previous, p)
  } catch (err) {
    devError.value = err instanceof Error && /not found/i.test(err.message)
      ? t('settings.db.notFound')
      : t('settings.db.saveFailed')
  }
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
</script>

<template>
  <button class="db-pick-card" @click="downloadOtzaria">
    <IconArrowDownload20Regular class="db-card-icon" />
    <span class="db-card-path placeholder">{{ $t('settings.db.downloadOtzaria') }}</span>
  </button>
  <button class="db-pick-card" @click="downloadZayit">
    <IconArrowDownload20Regular class="db-card-icon" />
    <span class="db-card-path placeholder">{{ $t('settings.db.downloadZayit') }}</span>
  </button>
  <button class="db-pick-card" @click="pickDbPath">
    <IconFolderOpen20Regular class="db-card-icon" />
    <span class="db-card-path" :class="{ placeholder: !dbPath }">
      {{ dbPath || $t('settings.db.pick') }}
    </span>
  </button>
    <div class="db-pick-card db-dev-row">
      <IconFolderOpen20Regular class="db-card-icon" />
      <input
        v-model="devInput"
        class="db-dev-input"
        :placeholder="$t('settings.db.devPlaceholder')"
        spellcheck="false"
        @keydown.enter="applyDevPath"
      />
      <button class="db-dev-apply" @click="applyDevPath">{{ $t('settings.ok') }}</button>
    </div>
    <MessageBanner v-if="devError" class="db-dev-error" :message="devError" @dismiss="devError = ''" />
    <p v-if="dbPath && !devError" class="db-dev-ok">✓ {{ dbPath }}</p>
</template>

<style scoped>
.db-pick-card {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 0 10px;
  height: 32px;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-primary);
  color: var(--text-primary);
  cursor: pointer;
  transition:
    border-color 0.15s,
    background 0.15s;
}
.db-pick-card + .db-pick-card {
  margin-top: 8px;
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
  text-align: end;
  min-width: 0;
}
.db-card-path.placeholder {
  text-align: start;
  font-style: italic;
}

.db-dev-row {
  cursor: default;
}
.db-dev-input {
  flex: 1;
  min-width: 0;
  border: none;
  outline: none;
  background: transparent;
  color: var(--text-primary);
  font-size: 11px;
  direction: ltr;
  text-align: end;
}
.db-dev-apply {
  flex-shrink: 0;
  padding: 2px 10px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: var(--bg-secondary);
  color: var(--text-primary);
  font-size: 11px;
  cursor: pointer;
}
.db-dev-apply:hover {
  border-color: var(--accent-color);
}
.db-dev-error {
  margin-top: 6px;
}
.db-dev-ok {
  margin: 6px 2px 0;
  font-size: 11px;
  color: var(--text-secondary);
  direction: ltr;
  text-align: end;
}
</style>
