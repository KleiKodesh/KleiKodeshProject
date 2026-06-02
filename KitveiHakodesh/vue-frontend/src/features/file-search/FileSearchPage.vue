<script setup lang="ts">
import { ref, onMounted, nextTick } from 'vue'
import { IconSearch20Regular, IconWarning20Regular } from '@iconify-prerendered/vue-fluent'
import BottomSearchBar from '@/components/BottomSearchBar.vue'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import FileSearchResultsList from './FileSearchResultsList.vue'
import { useFileSearch } from './useFileSearch'
import { useTabStore } from '@/stores/tabStore'
import { pickLocalFile } from '@/webview-host/bridge'
import { isHosted } from '@/webview-host/seforimDb'
import type { FileSearchResult } from './useFileSearch'

const tabStore = useTabStore()

const searchQuery = ref('')
const { results, searching, isIndexing, totalCount, errorMessage } = useFileSearch(searchQuery)

const searchInputElement = ref<HTMLInputElement | null>(null)
const resultsListElement = ref<InstanceType<typeof FileSearchResultsList> | null>(null)
const openingFile = ref(false)

onMounted(() => {
  nextTick(() => searchInputElement.value?.focus())
})

function focusResults() {
  resultsListElement.value?.focusContainer()
}

async function onOpenFile(item: FileSearchResult) {
  if (!isHosted) return

  openingFile.value = true

  try {
    // Determine route from extension: HTML and plain-text files open in html-view (iframe),
    // everything else in pdf-view. Word files are converted to PDF by C# via the
    // pickFile / restoreLocalFile path; here we open the file directly by asking C# to
    // register a virtual host for it.
    const extension = item.fileName.substring(item.fileName.lastIndexOf('.')).toLowerCase()
    const isHtmlLike = extension === '.htm' || extension === '.html' || extension === '.txt'
    const route = isHtmlLike ? '/html-view' : '/pdf-view'

    const result = await (
      window as Window & {
        __webviewAction?: (action: string, args?: object) => Promise<unknown>
      }
    ).__webviewAction?.('restoreLocalFile', { filePath: item.fullPath }) as
      | { url?: string; error?: string }
      | undefined

    if (!result?.url) {
      // Fall back to native file picker so C# can handle Word conversion etc.
      const picked = await pickLocalFile()
      if (!picked) return
      const pickedExt = picked.fileName.substring(picked.fileName.lastIndexOf('.')).toLowerCase()
      const pickedIsHtmlLike = pickedExt === '.htm' || pickedExt === '.html' || pickedExt === '.txt'
      tabStore.updateActiveTab({
        route: pickedIsHtmlLike ? '/html-view' : '/pdf-view',
        title: picked.fileName,
        localFileName: picked.fileName,
        localFilePath: picked.filePath,
        localFileVirtualUrl: picked.url,
      })
      return
    }

    tabStore.updateActiveTab({
      route,
      title: item.fileName,
      localFileName: item.fileName,
      localFilePath: item.fullPath,
      localFileVirtualUrl: result.url,
    })
  } catch {
    // Silently fall through — file may require conversion or may be inaccessible
  } finally {
    openingFile.value = false
  }
}
</script>

<template>
  <div class="file-search-page">
    <div class="file-search-content">
      <!-- Everything index loading -->
      <div v-if="isIndexing" class="indexing-state">
        <LoadingAnimation text="האינדקס בטעינה" />
      </div>

      <!-- Error state -->
      <div v-else-if="errorMessage" class="state-banner error-banner">
        <IconWarning20Regular class="banner-icon banner-icon--error" />
        <span>{{ errorMessage }}</span>
      </div>

      <!-- Results or empty state (only render when NOT indexing) -->
      <div v-else-if="!isIndexing" class="results-container">
        <!-- Empty state when no results and no query -->
        <div v-if="!results.length && !searching" class="empty-state">
          <IconSearch20Regular class="empty-icon" />
          <span class="empty-msg">{{ searchQuery.trim() ? 'לא נמצאו תוצאות' : 'חפש קבצים...' }}</span>
        </div>

        <!-- Results list -->
        <FileSearchResultsList
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
.file-search-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
}
.file-search-content {
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

/* Indexing state */
.indexing-state {
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
