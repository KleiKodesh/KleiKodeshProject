<script setup lang="ts">
import { ref, onMounted, nextTick } from 'vue'
import { IconSearch20Regular, IconWarning20Regular } from '@iconify-prerendered/vue-fluent'
import BottomSearchBar from '@/components/BottomSearchBar.vue'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import LocalFileSearchResultsList from './LocalFileSearchResultsList.vue'
import { useLocalFileSearch } from './useLocalFileSearch'
import { useTabStore } from '@/stores/tabStore'
import { restoreLocalFile } from '@/webview-host/bridge'
import { isHosted } from '@/webview-host/seforimDb'
import type { LocalFileSearchResult } from './useLocalFileSearch'

const tabStore = useTabStore()

const searchQuery = ref('')
const {
  results, searching, showLoadingAnimation, isIndexing, indexingMessage,
  totalCount, errorMessage, installConsentPending,
  onInstallConsentGranted, onInstallConsentDeclined,
} = useLocalFileSearch(searchQuery)

const searchInputElement = ref<HTMLInputElement | null>(null)
const resultsListElement = ref<InstanceType<typeof LocalFileSearchResultsList> | null>(null)
const openingFile = ref(false)

onMounted(() => {
  nextTick(() => searchInputElement.value?.focus())
})

function focusResults() {
  resultsListElement.value?.focusContainer()
}

async function onOpenFile(item: LocalFileSearchResult) {
  if (!isHosted) {
    console.log('[LocalFileSearch] not hosted — skipping open')
    return
  }

  console.log('[LocalFileSearch] onOpenFile', item)
  openingFile.value = true

  try {
    const extension = item.fileName.substring(item.fileName.lastIndexOf('.')).toLowerCase()
    const isHtmlLike = extension === '.htm' || extension === '.html' || extension === '.txt'
    const route = isHtmlLike ? '/html-view' : '/pdf-view'
    console.log('[LocalFileSearch] extension:', extension, 'route:', route, 'fullPath:', item.fullPath)

    const restored = await restoreLocalFile(item.fullPath)
    console.log('[LocalFileSearch] restoreLocalFile result:', restored)

    if (!restored?.url) {
      console.warn('[LocalFileSearch] no url returned — aborting open')
      return
    }

    console.log('[LocalFileSearch] navigating to', route, 'url:', restored.url)
    tabStore.updateActiveTab({
      route,
      title: item.fileName,
      localFileName: item.fileName,
      localFilePath: item.fullPath,
      localFileVirtualUrl: restored.url,
    })
  } catch (error) {
    console.error('[LocalFileSearch] onOpenFile error:', error)
  } finally {
    openingFile.value = false
  }
}
</script>

<template>
  <div class="local-file-search-page">
    <div class="local-file-search-content">
      <!-- Install consent prompt — shown first time the page opens -->
      <div v-if="installConsentPending" class="consent-panel">
        <div class="consent-card">
          <h2 class="consent-title">חיפוש קבצים מקומיים</h2>
          <p class="consent-body">
            תכונה זו משתמשת בשירות אינדקס קבצים (DocumentLocator) שצריך להיות מותקן
            במחשב כשירות מערכת. ההתקנה דורשת אישור מנהל מערכת (UAC) פעם אחת בלבד.
          </p>
          <p class="consent-body">
            האם ברצונך להתקין את שירות האינדקס?
          </p>
          <div class="consent-buttons">
            <button class="consent-btn consent-btn--yes" @click="onInstallConsentGranted">
              כן, התקן
            </button>
            <button class="consent-btn consent-btn--no" @click="onInstallConsentDeclined">
              לא
            </button>
          </div>
        </div>
      </div>

      <!-- Index building — show spinner with live progress message from C# -->
      <div v-else-if="isIndexing" class="indexing-state">
        <LoadingAnimation :text="indexingMessage ?? 'האינדקס בטעינה'" />
      </div>

      <!-- Error state -->
      <div v-else-if="errorMessage" class="state-banner error-banner">
        <IconWarning20Regular class="banner-icon banner-icon--error" />
        <span>{{ errorMessage }}</span>
      </div>

      <!-- Results or empty state (only render when NOT indexing) -->
      <div v-else-if="!isIndexing" class="results-container">
        <!-- Loading while any search is in flight for more than 200ms -->
        <div v-if="showLoadingAnimation" class="searching-state">
          <LoadingAnimation text="מחפש..." />
        </div>

        <!-- Empty state when idle with no results -->
        <div v-else-if="!results.length" class="empty-state">
          <IconSearch20Regular class="empty-icon" />
          <span class="empty-msg">{{ searchQuery.trim() ? 'לא נמצאו תוצאות' : 'חפש קבצים...' }}</span>
        </div>

        <!-- Results list -->
        <LocalFileSearchResultsList
          v-else
          ref="resultsListElement"
          :items="results"
          :searching="searching"
          :is-indexing="isIndexing"
          @open-file="onOpenFile"
        />

        <!-- Opening overlay — shown immediately on click so the user knows something is happening -->
        <div v-if="openingFile" class="opening-overlay">
          <div class="opening-card">
            <div class="opening-spinner" />
            <span class="opening-label">פותח קובץ…</span>
          </div>
        </div>

        <!-- Truncation notice -->
        <div v-if="totalCount > results.length" class="truncation-notice">
          (מוצגים {{ results.length }} מתוך {{ totalCount }} תוצאות)
        </div>
      </div>
    </div>

    <BottomSearchBar>
      <template #left>
        <IconSearch20Regular class="search-icon" />
      </template>
      <input
        ref="searchInputElement"
        v-model="searchQuery"
        type="search"
        class="search-input"
        placeholder="חפש קבצים..."
        spellcheck="false"
        autocomplete="off"
        @keydown.up.prevent="focusResults"
        @keydown.down.prevent="focusResults"
        @keydown.tab.prevent="focusResults"
      />
    </BottomSearchBar>
  </div>
</template>

<style scoped>
.local-file-search-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
}
.local-file-search-content {
  flex: 1;
  overflow: hidden;
  position: relative;
  display: flex;
  flex-direction: column;
}
.results-container {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  position: relative;
}
.search-icon {
  color: var(--text-secondary);
}
.search-input {
  flex: 1;
  background: none;
  border: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
}
.search-input::placeholder {
  color: var(--text-secondary);
}
.search-input::-webkit-search-cancel-button {
  filter: grayscale(1) opacity(0.4);
}

/* Install consent */
.consent-panel {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px;
}
.consent-card {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  padding: 28px 32px;
  max-width: 340px;
  width: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 14px;
  text-align: center;
}
.consent-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
}
.consent-body {
  font-size: 13px;
  color: var(--text-secondary);
  line-height: 1.6;
  margin: 0;
}
.consent-buttons {
  display: flex;
  gap: 10px;
  margin-top: 4px;
  width: 100%;
  justify-content: center;
}
.consent-btn {
  padding: 8px 22px;
  font-size: 13px;
  border-radius: 4px;
  cursor: pointer;
  font-family: inherit;
  border: 1px solid var(--border-color);
  transition: background 0.1s;
}
.consent-btn--yes {
  background: var(--accent-color, #0078d4);
  color: #fff;
  border-color: transparent;
}
.consent-btn--yes:hover {
  background: color-mix(in srgb, var(--accent-color, #0078d4) 85%, #fff);
}
.consent-btn--no {
  background: var(--bg-primary);
  color: var(--text-secondary);
}
.consent-btn--no:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}

/* Banners */
.state-banner {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 14px;
  font-size: 12px;
  color: var(--text-secondary);
  background: color-mix(in srgb, var(--text-secondary) 8%, transparent);
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}

.error-banner {
  color: #ff3b30;
  background: color-mix(in srgb, #ff3b30 8%, transparent);
}
.banner-icon {
  flex-shrink: 0;
  font-size: 16px;
  color: inherit;
}
.banner-icon svg {
  color: inherit;
}

/* Indexing / searching state */
.indexing-state,
.searching-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  flex: 1;
}

/* Empty state */
.empty-state {
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
}
.empty-icon {
  width: 56px;
  height: 56px;
  opacity: 0.25;
  font-size: 56px;
}
.empty-msg {
  font-size: 14px;
  color: var(--text-secondary);
  opacity: 0.25;
  font-weight: 500;
}

/* Opening overlay */
.opening-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: color-mix(in srgb, var(--bg-primary) 80%, transparent);
  backdrop-filter: blur(2px);
  z-index: 10;
}
.opening-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  padding: 28px 40px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 12px;
}
.opening-spinner {
  width: 24px;
  height: 24px;
  border: 2px solid var(--border-color);
  border-top-color: var(--text-secondary);
  border-radius: 50%;
  animation: opening-spin 0.7s linear infinite;
}
@keyframes opening-spin {
  to { transform: rotate(360deg); }
}
.opening-label {
  font-size: 13px;
  color: var(--text-secondary);
}

/* Truncation notice */
.truncation-notice {
  padding: 4px 12px;
  font-size: 11px;
  color: var(--text-secondary);
  background: var(--bg-secondary);
  border-top: 1px solid var(--border-color);
  text-align: center;
  flex-shrink: 0;
}
</style>
