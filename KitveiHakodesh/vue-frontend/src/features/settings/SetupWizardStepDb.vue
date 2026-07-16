<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { dbReady, onWebviewEvent, onDbReady } from '@/webview-host/seforimDb'
import { getDbPathInfo, setDbPathDev } from '@/webview-host/bridge'
import {
  IconFolderOpen20Regular,
  IconArrowDownload20Regular,
} from '@iconify-prerendered/vue-fluent'

// Dev (browser + service): no native file dialog — the path is typed instead.
// The service validates it, persists it to the SAME registry value the hosted
// app uses, and restarts on the new DB (rebuilding a stale search index).
const isDev = typeof window.__webviewAction !== 'function'
const dbPath = ref(dbReady.value ? (window.__webviewDbPath ?? '') : '')
const devInput = ref('')
const devError = ref('')

onMounted(() => {
  const unregister = onWebviewEvent((msg) => {
    if (msg.event === 'dbPathPicked') dbPath.value = msg.path as string
  })
  onUnmounted(unregister)
  if (isDev) {
    getDbPathInfo().then((info) => {
      if (info) devInput.value = info.path
    })
  }
})

function pickDbPath() {
  window.__webviewPickDbPath?.()
}

async function applyDevPath() {
  const p = devInput.value.trim()
  if (!p) return
  devError.value = ''
  try {
    await setDbPathDev(p)
    dbPath.value = p
    onDbReady(p)
  } catch (err) {
    devError.value = err instanceof Error && /not found/i.test(err.message)
      ? 'הקובץ לא נמצא בנתיב שצוין'
      : 'שמירת הנתיב נכשלה'
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
  <div class="step-content">
    <div class="step-header">
      <h2 class="step-title">בחירת מסד נתונים</h2>
      <p class="step-desc">
        כתבי הקודש צריכה את מסד הנתונים של זית או של אוצריא. אם אחת מהתוכנות כבר מותקנת, הפעל
        אותה פעם אחת לסיום ההתקנה ואז בחר את הנתיב למסד הנתונים. ניתן לשנות את הנתיב בכל עת דרך
        הגדרות האפליקציה.
      </p>
    </div>
    <div class="step-scroll">
      <div class="step-card">
        <button class="db-pick-card" @click="downloadZayit">
          <IconArrowDownload20Regular class="db-card-icon" />
          <span class="db-card-path placeholder">הורד את זית</span>
        </button>
        <button class="db-pick-card" @click="downloadOtzaria">
          <IconArrowDownload20Regular class="db-card-icon" />
          <span class="db-card-path placeholder">הורד את אוצריא</span>
        </button>
        <button v-if="!isDev" class="db-pick-card" @click="pickDbPath">
          <IconFolderOpen20Regular class="db-card-icon" />
          <span class="db-card-path" :class="{ placeholder: !dbPath }">
            {{ dbPath || 'בחר קובץ מסד נתונים' }}
          </span>
        </button>
        <template v-else>
          <div class="db-pick-card db-dev-row">
            <IconFolderOpen20Regular class="db-card-icon" />
            <input
              v-model="devInput"
              class="db-dev-input"
              placeholder="נתיב מלא לקובץ seforim.db"
              spellcheck="false"
              @keydown.enter="applyDevPath"
            />
            <button class="db-dev-apply" @click="applyDevPath">אישור</button>
          </div>
          <p v-if="devError" class="db-dev-error">{{ devError }}</p>
          <p v-if="dbPath && !devError" class="db-dev-ok">✓ {{ dbPath }}</p>
        </template>
      </div>
    </div>
  </div>
</template>

<style scoped>
.step-content {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.step-header {
  flex-shrink: 0;
  max-width: 560px;
  width: 100%;
  margin: 0 auto;
  padding: 28px 16px 12px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  box-sizing: border-box;
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
  margin: 0;
  font-size: 13px;
  color: var(--text-secondary);
  line-height: 1.6;
  animation: fade-up 0.25s 0.05s ease both;
}

.step-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 0 16px 24px;
}

.step-card {
  max-width: 560px;
  margin: 0 auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 16px 20px;
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
  text-align: left;
  min-width: 0;
}
.db-card-path.placeholder {
  direction: rtl;
  text-align: right;
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
  text-align: left;
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
  margin: 6px 2px 0;
  font-size: 11px;
  color: var(--danger-color, #d13438);
}
.db-dev-ok {
  margin: 6px 2px 0;
  font-size: 11px;
  color: var(--text-secondary);
  direction: ltr;
  text-align: left;
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
