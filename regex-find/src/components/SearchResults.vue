<template>
  <div class="search-results">
    <div v-if="store.hasResults" class="results-list">
      <div 
        v-for="(result, index) in store.state.results" 
        :key="index"
        class="result-item"
        :class="{ selected: index === store.state.currentIndex }"
        @click="store.selectResult(index)"
      >
        <div class="result-content">
          <span class="before">{{ result.before }}</span>
          <span class="match">{{ result.text }}</span>
          <span class="after">{{ result.after }}</span>
        </div>
      </div>
    </div>

    <div v-else-if="store.state.isSearching" class="status-message">
      מחפש...
    </div>

    <div v-else class="status-message">
      <!-- Empty state when no search performed -->
    </div>
  </div>
</template>

<script setup lang="ts">
import { useRegexFindStore } from '@/stores/regex-find'

const store = useRegexFindStore()

defineEmits<{
  replaceCurrent: []
  selectInDocument: []
}>()
</script>

<style scoped>
.search-results {
  flex: 1;
  display: flex;
  flex-direction: column;
  border-top: 0.5px solid #ccc;
  background: transparent;
  direction: rtl;
  text-align: right;
}

.results-list {
  flex: 1;
  overflow-y: auto;
  border: none;
}

.result-item {
  padding: 5px;
  cursor: pointer;
  border-right: 2px solid transparent;
  margin: 0;
  font-size: 12px;
  line-height: 1.4;
  text-align: justify;
}

.result-item:hover {
  background: #f8f9fa;
}

.result-item.selected {
  border-right-color: #007acc;
  background: #e3f2fd;
}

.result-content {
  font-family: 'Courier New', monospace;
  word-wrap: break-word;
  overflow-wrap: break-word;
}

.before, .after {
  color: #666;
}

.match {
  background-color: #ffeb3b;
  font-weight: bold;
  padding: 1px 2px;
  border-radius: 1px;
}

.status-message {
  padding: 20px;
  text-align: center;
  color: #666;
  font-style: italic;
}
</style>