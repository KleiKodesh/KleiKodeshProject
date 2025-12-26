<template>
  <div id="app" class="hebrew-app" dir="rtl">
    <div class="search-container">
      <div class="header-container">
        <div class="header-buttons">
          <div class="header-left">
            <ToggleButton 
              v-model="showReplace"
              label="החלפה"
              title="החלפה"
              size="large"
              class="replace-toggle"
            />
          </div>
          <div class="header-center">
            <div class="header-controls">
              <div class="input-with-label">
                 <label>חפש:</label>
                <Dropdown 
                  v-model="store.state.options.mode" 
                  :options="modeOptions"
                  title="כיוון החיפוש"
                />
              </div>
              <div class="input-with-label">
                <label>מרחק בין מילים:</label>
                <input v-model.number="store.state.options.slop" type="number" min="0" class="slop-input" title="מרחק בין מילים" />
              </div>
            </div>
          </div>
          <div class="header-right">
            <button class="icon-button" title="קידודון רג'קס'">
              <svg viewBox="0 0 24 24"><path d="M2,6V8H14V6H2M2,10V12H14V10H2M20.04,10.13C19.9,10.13 19.76,10.19 19.65,10.3L18.65,11.3L20.7,13.35L21.7,12.35C21.92,12.14 21.92,11.79 21.7,11.58L20.42,10.3C20.31,10.19 20.18,10.13 20.04,10.13M18.07,11.88L12,17.94V20H14.06L20.12,13.93L18.07,11.88M2,14V16H10V14H2Z"/></svg>
            </button>
            <button class="icon-button" title="מדריך">
              <svg viewBox="0 0 24 24"><path d="M11,18H13V16H11V18M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,20C7.59,20 4,16.41 4,12C4,7.59 7.59,4 12,4C16.41,4 20,7.59 20,12C20,16.41 16.41,20 12,20M12,6A4,4 0 0,0 8,10H10A2,2 0 0,1 12,8A2,2 0 0,1 14,10C14,12 11,11.75 11,15H13C13,12.75 16,12.5 16,10A4,4 0 0,0 12,6Z"/></svg>
            </button>
            <button class="icon-button" title="אודות">
              <svg viewBox="0 0 24 24"><path d="M11,9H13V7H11M12,20C7.59,20 4,16.41 4,12C4,7.59 7.59,4 12,4C16.41,4 20,7.59 20,12C20,16.41 16.41,20 12,20M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M11,17H13V11H11V17Z"/></svg>
            </button>
          </div>
        </div>
      </div>

      <div class="regex-find-content">
        <!-- Search Panel (Always Visible) -->
        <div class="panel search-panel">
          <div class="search-box">
            <div class="input-container">
              <input 
                v-model="store.state.options.text" 
                type="text" 
                placeholder="חיפוש..."
                class="search-input"
                title="חיפוש"
                @keyup.enter="handleSearch"
              />
              <div class="input-buttons">
                <ToggleButton 
                  v-model="store.state.options.useWildcards"
                  title="השתמש בתווים כלליים"
                  size="medium"
                  variant="input-container"
                >
                  <IconRegex :size="16" />
                </ToggleButton>
                <button class="search-button" @click="handleSearch" :disabled="store.state.isSearching" title="חיפוש">
                  <IconSearch :size="16" />
                </button>
              </div>
            </div>
          </div>

          <!-- Formatting Buttons -->
          <div class="formatting-buttons">
            <ToggleButton v-model="store.state.options.bold" title="מודגש" size="medium">
              <span class="format-text bold">B</span>
            </ToggleButton>
            <ToggleButton v-model="store.state.options.italic" title="נטוי" size="medium">
              <span class="format-text italic">I</span>
            </ToggleButton>
            <ToggleButton v-model="store.state.options.underline" title="קו" size="medium">
              <span class="format-text underline">U</span>
            </ToggleButton>
            <ToggleButton v-model="store.state.options.superscript" title="כתב עילי" size="medium">
              <IconSuperscript :size="16" />
            </ToggleButton>
            <ToggleButton v-model="store.state.options.subscript" title="כתב תחתי" size="medium">
              <IconSubscript :size="16" />
            </ToggleButton>
            <button class="format-btn color-btn" title="צבע גופן">
              <span class="format-text color-text">T</span>
            </button>
            <button class="format-btn" title="נקה הגדרות עיצוב">
              <IconClear :size="16" />
            </button>
            <button class="format-btn" title="שאב הגדרות עיצוב">
              <IconCopy :size="16" />
            </button>
          </div>

          <!-- Font Options -->
          <div class="font-options">
            <div class="font-row">
              <label>סגנון:</label>
              <ComboBox 
                v-model="store.state.options.style" 
                :options="styleOptions"
                title="סגנון"
                placeholder="רגיל"
                class="flex-combobox"
              />
              <label>גופן:</label>
              <ComboBox 
                v-model="store.state.options.font" 
                :options="fontOptions"
                title="גופן"
                placeholder="ברירת מחדל"
                class="flex-combobox"
              />
              <label>גודל:</label>
              <input v-model.number="store.state.options.fontSize" type="number" min="1" class="font-input size-input" title="גודל גופן" />
            </div>
          </div>
        </div>

        <!-- Replace Panel (Toggleable) -->
        <div v-show="showReplace" class="panel replace-panel">
          <div class="search-box">
            <div class="input-container">
              <input 
                v-model="store.state.options.replace.text" 
                type="text" 
                placeholder="החלף ב.."
                class="search-input"
                title="החלף ב.."
                @keyup.enter="handleReplaceAll"
              />
              <div class="input-buttons">
                <button class="search-button" @click="handleReplaceCurrent" :disabled="!store.currentResult" title="החלף">
                  <IconReplace :size="16" />
                </button>
                <button class="search-button" @click="handleReplaceAll" :disabled="!store.hasResults" title="החלף הכל">
                  <IconReplaceAll :size="16" />
                </button>
              </div>
            </div>
          </div>

          <!-- Replace Formatting Buttons -->
          <div class="formatting-buttons">
            <ToggleButton v-model="store.state.options.replace.bold" title="מודגש" size="medium">
              <span class="format-text bold">B</span>
            </ToggleButton>
            <ToggleButton v-model="store.state.options.replace.italic" title="נטוי" size="medium">
              <span class="format-text italic">I</span>
            </ToggleButton>
            <ToggleButton v-model="store.state.options.replace.underline" title="קו" size="medium">
              <span class="format-text underline">U</span>
            </ToggleButton>
            <ToggleButton v-model="store.state.options.replace.superscript" title="כתב עילי" size="medium">
              <IconSuperscript :size="16" />
            </ToggleButton>
            <ToggleButton v-model="store.state.options.replace.subscript" title="כתב תחתי" size="medium">
              <IconSubscript :size="16" />
            </ToggleButton>
            <button class="format-btn color-btn" title="צבע גופן">
              <span class="format-text color-text">T</span>
            </button>
            <button class="format-btn" title="נקה הגדרות עיצוב">
              <IconClear :size="16" />
            </button>
            <button class="format-btn" title="שאב הגדרות עיצוב">
              <IconCopy :size="16" />
            </button>
          </div>

          <!-- Replace Font Options -->
          <div class="font-options">
            <div class="font-row">
              <label>סגנון:</label>
              <ComboBox 
                v-model="store.state.options.replace.style" 
                :options="styleOptions"
                title="סגנון"
                placeholder="רגיל"
                class="flex-combobox"
              />
              <label>גופן:</label>
              <ComboBox 
                v-model="store.state.options.replace.font" 
                :options="fontOptions"
                title="גופן"
                placeholder="ברירת מחדל"
                class="flex-combobox"
              />
              <label>גודל:</label>
              <input v-model.number="store.state.options.replace.fontSize" type="number" min="1" class="font-input size-input" title="גודל גופן" />
            </div>
          </div>
        </div>
      </div>
      
      <SearchResults 
        @replace-current="handleReplaceCurrent"
        @select-in-document="handleSelectInDocument"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import SearchResults from '@/components/SearchResults.vue'
import { useRegexFindStore } from '@/stores/regex-find'
import { RegexService } from '@/services/regex-service'
import IconSearch from '@/components/icons/IconSearch.vue'
import IconReplace from '@/components/icons/IconReplace.vue'
import IconReplaceAll from '@/components/icons/IconReplaceAll.vue'
import IconRegex from '@/components/icons/IconRegex.vue'
import IconSuperscript from '@/components/icons/IconSuperscript.vue'
import IconSubscript from '@/components/icons/IconSubscript.vue'
import IconClear from '@/components/icons/IconClear.vue'
import IconCopy from '@/components/icons/IconCopy.vue'
import ToggleButton from '@/components/ToggleButton.vue'
import ComboBox from '@/components/ComboBox.vue'
import Dropdown from '@/components/Dropdown.vue'

const store = useRegexFindStore()
const showReplace = ref(false)

// Font style options
const styleOptions = [
  { value: '', label: 'רגיל' },
  { value: 'Bold', label: 'מודגש' },
  { value: 'Italic', label: 'נטוי' },
  { value: 'Bold Italic', label: 'מודגש נטוי' },
  { value: 'Light', label: 'דק' },
  { value: 'Medium', label: 'בינוני' },
  { value: 'SemiBold', label: 'חצי מודגש' },
  { value: 'Black', label: 'שחור' }
]

// Font family options
const fontOptions = [
  { value: '', label: 'ברירת מחדל' },
  { value: 'Arial', label: 'Arial' },
  { value: 'Times New Roman', label: 'Times New Roman' },
  { value: 'Calibri', label: 'Calibri' },
  { value: 'David', label: 'David' },
  { value: 'Frank Ruehl CLM', label: 'Frank Ruehl CLM' },
  { value: 'Miriam', label: 'Miriam' },
  { value: 'Narkisim', label: 'Narkisim' },
  { value: 'Segoe UI', label: 'Segoe UI' },
  { value: 'Tahoma', label: 'Tahoma' },
  { value: 'Verdana', label: 'Verdana' }
]

// Mode options
const modeOptions = [
  { value: 'All', label: 'הכל' },
  { value: 'Forward', label: 'כלפי מטה' },
  { value: 'Back', label: 'כלפי מעלה' },
  { value: 'Selection', label: 'לפי בחירה' }
]

async function handleSearch() {
  if (!store.state.options.text.trim()) {
    store.setResults([])
    return
  }

  store.setSearching(true)
  
  try {
    const results = await RegexService.search(store.state.options)
    store.setResults(results)
  } catch (error) {
    console.error('Search failed:', error)
    store.setResults([])
  } finally {
    store.setSearching(false)
  }
}

async function handleReplaceAll() {
  if (!store.hasResults) return

  try {
    await RegexService.replaceAll(store.state.options)
    alert(`Successfully replaced all ${store.state.results.length} occurrences`)
    // Refresh search results after replace
    await handleSearch()
  } catch (error) {
    console.error('Replace all failed:', error)
    alert('Failed to replace all occurrences')
  }
}

async function handleReplaceCurrent() {
  if (!store.currentResult) return

  try {
    await RegexService.replaceCurrent(store.state.options, store.currentResult)
    alert(`Successfully replaced occurrence at position ${store.currentResult.start}-${store.currentResult.end}`)
    // Refresh search results after replace
    await handleSearch()
  } catch (error) {
    console.error('Replace current failed:', error)
    alert('Failed to replace current occurrence')
  }
}

async function handleSelectInDocument() {
  if (!store.currentResult) return
  try {
    await RegexService.selectInDocument(store.currentResult)
    // In real implementation, this would select the text in Word
  } catch (error) {
    console.error('Select in document failed:', error)
    alert('Failed to select text in document')
  }
}
</script>

<style>
* {
  margin: 0;
  padding: 0;
  box-sizing: border-box;
}

body {
  font-family: 'Segoe UI', 'Arial', sans-serif;
  font-size: 14px;
  background-color: #f0f0f0;
  color: #333;
}

#app {
  height: 100vh;
  display: flex;
  flex-direction: column;
}

.hebrew-app {
  direction: rtl;
  text-align: right;
}

.search-container {
  flex: 1;
  display: flex;
  flex-direction: column;
  width: 100%;
  height: 100%;
  background: white;
  border: 1px solid #ccc;
}

.header-container {
  background: #f8f8f8;
  border-bottom: 1px solid #ccc;
  padding: 8px 15px;
  flex-shrink: 0;
}

.header-buttons {
  display: flex;
  gap: 8px;
  justify-content: space-between;
  align-items: center;
  height: 32px;
  width: 100%;
}

.header-left {
  display: flex;
  gap: 8px;
  align-items: center;
  flex: 0 0 auto;
}

.header-center {
  display: flex;
  justify-content: center;
  align-items: center;
  flex: 1;
}

.header-right {
  display: flex;
  gap: 2px;
  align-items: center;
  flex: 0 0 auto;
}

.header-controls {
  display: flex;
  gap: 12px;
  align-items: center;
  margin: 0 8px;
  height: 32px;
}

.header-controls .input-with-label {
  display: flex;
  align-items: center;
  gap: 4px;
  height: 32px;
}

.header-controls label {
  font-size: 12px;
  color: #333;
  white-space: nowrap;
}

.mode-select {
  padding: 4px 6px;
  border: 1px solid #d2d0ce;
  border-radius: 4px;
  font-size: 12px;
  font-family: inherit;
  background: white;
  min-width: 80px;
  height: 24px;
  outline: none;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}

.mode-select:hover {
  border-color: #a19f9d;
}

.mode-select:focus {
  border-color: #0078d4;
  box-shadow: 0 0 0 0.5px #0078d4;
}

.slop-input {
  padding: 4px 6px;
  border: 1px solid #d2d0ce;
  border-radius: 4px;
  font-size: 12px;
  font-family: inherit;
  width: 40px;
  height: 24px;
  outline: none;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}

.slop-input:hover {
  border-color: #a19f9d;
}

.slop-input:focus {
  border-color: #0078d4;
  box-shadow: 0 0 0 0.5px #0078d4;
}

.replace-toggle {
  margin-left: 8px;
}

.icon-button {
  width: 24px;
  height: 24px;
  border: none;
  background: transparent;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 2px;
}

.icon-button:hover {
  background: #e8e8e8;
}

.icon-button svg {
  width: 14px;
  height: 14px;
  fill: #333;
}

.regex-find-content {
  font-family: 'Segoe UI', 'Arial', sans-serif;
  font-size: 14px;
  flex-shrink: 0;
  direction: rtl;
  text-align: right;
}

.panel {
  padding: 15px;
}

.replace-panel {
  background: #f8f8f8;
  border-top: 1px solid #ccc;
  border-bottom: 1px solid #ccc;
}

.search-box {
  margin-bottom: 8px;
}

.input-container {
  display: flex;
  border: 1px solid #d2d0ce;
  background: white;
  border-radius: 4px;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}

.input-container:hover {
  border-color: #a19f9d;
}

.input-container:focus-within {
  border-color: #0078d4;
  box-shadow: 0 0 0 0.5px #0078d4;
}

.search-input {
  flex: 1;
  border: none;
  padding: 8px 10px;
  font-size: 14px;
  font-family: inherit;
  outline: none;
  background: transparent;
}

.search-input::placeholder {
  color: #a19f9d;
}

.input-buttons {
  display: flex;
  border-right: 1px solid #eee;
}

.toggle-button, .search-button {
  width: 32px;
  height: 100%;
  min-height: 32px;
  border: none;
  background: transparent;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 2px;
}

.toggle-button:hover, .search-button:hover {
  background: #e8e8e8;
}

.toggle-button.active {
  background: #cce8ff;
}

.toggle-button svg, .search-button svg {
  width: 18px;
  height: 18px;
  fill: #333;
}

.search-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.formatting-buttons {
  display: flex;
  justify-content: space-between;
  margin-bottom: 8px;
  flex-wrap: wrap;
}

.format-btn {
  width: 32px;
  height: 32px;
  border: 1px solid #ccc;
  background: white;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 2px;
  font-size: 12px;
}

.format-btn:hover {
  background: #e8e8e8;
}

.format-btn.active {
  background: #cce8ff;
  border-color: #007acc;
}

.format-text {
  font-family: 'Baskerville Old Face', serif;
  font-size: 16px;
  font-weight: bold;
}

.format-text.bold {
  font-weight: bold;
}

.format-text.italic {
  font-style: italic;
}

.format-text.underline {
  text-decoration: underline;
}

.color-text {
  position: relative;
}

.color-text::after {
  content: '';
  position: absolute;
  bottom: -2px;
  left: 0;
  right: 0;
  height: 3px;
  background: #000;
}

.format-btn svg {
  width: 18px;
  height: 18px;
  fill: #333;
}

.font-options {
  margin-bottom: 8px;
}

.font-row {
  display: flex;
  gap: 6px;
  align-items: center;
}

.font-row label {
  font-size: 12px;
  color: #333;
  white-space: nowrap;
  flex-shrink: 0;
}

.flex-combobox {
  flex: 1;
  min-width: 0;
}

.size-input {
  width: 45px;
  flex-shrink: 0;
  padding: 6px 8px;
  border: 1px solid #d2d0ce;
  border-radius: 4px;
  font-size: 12px;
  font-family: inherit;
  outline: none;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}

.size-input:hover {
  border-color: #a19f9d;
}

.size-input:focus {
  border-color: #0078d4;
  box-shadow: 0 0 0 0.5px #0078d4;
}
</style>