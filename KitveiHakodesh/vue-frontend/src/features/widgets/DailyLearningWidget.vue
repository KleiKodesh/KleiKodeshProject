<script setup lang="ts">
import { inject, ref, shallowRef, watch } from 'vue'
import { dbReady } from '@/webview-host/seforimDb'
import HebrewDayNav from '@/components/HebrewDayNav.vue'
import ExcelDocumentGlyph from '@/components/ExcelDocumentGlyph.vue'
import { IconCheckmarkCircle20Filled, IconCheckmarkCircle20Regular, IconFolderArrowRight20Regular, IconFolderOpen20Regular, IconSubtract16Regular } from '@iconify-prerendered/vue-fluent'
import { openFileInDefaultApp, revealFileInFolder } from '@/webview-host/bridge'
import { useDailyLearningLog } from './dailyLearningLog'
import { wantsNewTab } from '@/composables/useOpenInNewTab'
import { DAILY_LEARNING_OPENER_KEY, useDailyLearningOpener, type HomeDailyLearningEntry } from '@/features/home/useHomeDailyLearning'
import { computeHomeDailyLearningItems } from '@/features/home/homeDailyLearningItems'
import { findBooksByName } from '@/webview-host/seforimApi'

// The standalone calendar has no library to look the books up in: it lists every cycle
// and hands the click to its opener (a link into the app). Inside the app, as before.
/** The day to list (the separate calendar app's picked day); today when absent. */
const props = defineProps<{ date?: Date | null }>()
const standaloneOpener = inject(DAILY_LEARNING_OPENER_KEY, null)
/** The day listed: the calendar's picked day, moved by the widget's own back/forward/today. */
const day = ref(props.date ?? new Date())
watch(() => props.date, (d) => { day.value = d ?? new Date() })
// The shown day's cycles, opened through the opener. Inside the app, cycles whose book is not
// in the library are greyed out (a failed lookup is not "missing": it stays clickable).
const opener = useDailyLearningOpener()
const entries = shallowRef<HomeDailyLearningEntry[]>([])
let generation = 0
watch([() => day.value.toDateString(), dbReady], () => {
  const mine = ++generation // also drops any lookup still running for the previous day
  const d = day.value
  void computeHomeDailyLearningItems(d).then((items) => {
    if (mine !== generation) return // a later day was picked meanwhile
    // Shown at once; each book lookup only greys its own line out when it comes back "missing".
    const entryOf = (item: (typeof items)[number], open: boolean): HomeDailyLearningEntry => ({
      key: item.key, label: item.label, location: item.location,
      open: open ? (openInNewTab: boolean) => opener(d, item.key, openInNewTab) : null,
    })
    entries.value = items.map((item) => entryOf(item, true))
    if (standaloneOpener) return
    // One service call for the whole day: matched in Core against its cached title index.
    void findBooksByName(items.map((item) => item.target.books)).then((ids) => {
      if (mine === generation && ids.includes(0)) entries.value = items.map((item, i) => entryOf(item, ids[i] !== 0))
    }, () => {}) // no answer: everything stays clickable
  })
}, { immediate: true })

// Marking needs the app's service; the standalone calendar has none.
const log = standaloneOpener ? null : useDailyLearningLog(day)
</script>

<template>
  <div class="learning-widget">
    <HebrewDayNav v-model="day" />
    <div v-for="entry in entries" :key="entry.key" class="learning-line">
      <template v-if="log">
        <button
          class="mark-btn"
          :class="{ done: log.counts.value.get(entry.label) }"
          type="button"
          :title="$t('home.learningLog.mark')"
          @click="log.mark(entry.label, entry.location)"
        >
          <IconCheckmarkCircle20Filled v-if="log.counts.value.get(entry.label)" /><IconCheckmarkCircle20Regular v-else />
          <span v-if="(log.counts.value.get(entry.label) ?? 0) > 1" class="mark-count">{{ log.counts.value.get(entry.label) }}</span>
        </button>
        <button
          v-if="log.counts.value.get(entry.label)"
          class="mark-btn unmark"
          type="button"
          :title="$t('home.learningLog.unmark')"
          @click="log.unmark(entry.label)"
        ><IconSubtract16Regular /></button>
      </template>
      <button
        class="learning-row"
        :disabled="!entry.open"
        :title="entry.open ? undefined : $t('home.dateBar.bookNotInLibrary')"
        @click="entry.open?.(wantsNewTab($event))"
      >
        <span class="learning-label">{{ entry.label }}</span>
        <span class="learning-path">{{ entry.location }}</span>
      </button>
    </div>
    <div v-if="log" class="log-foot">
      <span class="log-hint" :title="log.path.value">{{ log.path.value ? $t('home.learningLog.excelHint') : $t('home.learningLog.noFile') }}</span>
      <template v-if="log.path.value">
        <button class="mark-btn" type="button" :title="$t('home.learningLog.openExcel')" @click="openFileInDefaultApp(log.path.value)"><ExcelDocumentGlyph /></button>
        <button class="mark-btn" type="button" :title="$t('home.learningLog.showInFolder')" @click="revealFileInFolder(log.path.value)"><IconFolderOpen20Regular /></button>
      </template>
      <button class="mark-btn" type="button" :title="$t(log.path.value ? 'home.learningLog.move' : 'home.learningLog.pickFolder')" @click="log.chooseFolder()"><IconFolderArrowRight20Regular /></button>
    </div>
  </div>
</template>

<style scoped>
.learning-widget { display: flex; flex-direction: column; padding: 6px; }
.learning-line { display: flex; align-items: center; gap: 2px; }
.learning-row {
  flex: 1;
  min-width: 0;
  display: flex;
  justify-content: space-between;
  gap: 16px;
  padding: 6px 8px;
  background: none;
  border: none;
  border-radius: 4px;
  font-size: 13px;
  text-align: start;
  cursor: pointer;
  color: var(--text-primary);
}
.learning-row:hover:not(:disabled) { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.learning-row:disabled { opacity: 0.45; cursor: default; }
.learning-label { font-weight: 600; }
.learning-path { color: var(--text-secondary); }
.mark-btn {
  flex: none; display: inline-flex; align-items: center; gap: 1px;
  height: 26px; min-width: 26px; padding: 0 3px; justify-content: center;
  background: none; border: 1px solid transparent; border-radius: 4px;
  color: var(--text-secondary); cursor: pointer;
}
.mark-btn:hover { color: var(--text-primary); border-color: var(--border-color); }
.mark-btn.done { color: #107c10; }
.mark-btn.unmark { min-width: 20px; }
.mark-count { font-size: 11px; font-weight: 700; }
.log-foot { display: flex; align-items: center; gap: 2px; margin-top: 6px; padding: 4px 8px 0; border-top: 1px solid var(--border-color); }
.log-hint { flex: 1; min-width: 0; font-size: 11px; color: var(--text-secondary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
</style>
