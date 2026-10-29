<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import type { AltTocSection } from './useBookViewToc'
import type { TocPersistState } from '../bookViewTypes'
import type { TocEntry } from '@/webview-host/queries.types'
import { SearchableTree } from './tocSearchUtils'
import TocTreeSection from '@/components/TocTreeSection.vue'
import TocTreeLayout from '@/components/TocTreeLayout.vue'
import SplitPane from '@/components/SplitPane.vue'

const props = defineProps<{
  activeTocEntryId?: number
  activeAltTocEntryId?: number
  tocEntries: TocEntry[]
  selectedAltTocSection: AltTocSection | null
  loading: boolean
  error: string | null
  tocSearchTree?: SearchableTree
}>()

const emit = defineEmits<{ select: [TocEntry]; altSelect: [TocEntry] }>()

const tocSectionRef = ref<InstanceType<typeof TocTreeSection> | null>(null)
const altSectionRef = ref<InstanceType<typeof TocTreeSection> | null>(null)
const searchQuery = ref('')

// Focusing the search box on open / after loading lives in TocTreeLayout.

function focusTocList() {
  const el = tocSectionRef.value?.containerRef?.()
  el?.focus()
}

const hasToc = computed(() => props.tocEntries.length > 0)
const hasAlt = computed(() => props.selectedAltTocSection != null)

watch(searchQuery, (q) => {
  if (!q) return
  const section = props.selectedAltTocSection
  if (section != null && section.searchTree == null) {
    section.searchTree = new SearchableTree(section.entries)
  }
})

// ── Persistence ─────────────────────────────────────────────────────────────
// This whole subtree is v-if'd away when the panel closes and rebuilt on every
// tab switch, so none of the state below survives on its own. The book view
// snapshots it on save and hands it back through restoreState on mount.

function snapshotState(): Omit<TocPersistState, 'visible' | 'altStructureId'> {
  return {
    searchQuery: searchQuery.value,
    expanded: tocSectionRef.value?.getExpanded() ?? [],
    altExpanded: altSectionRef.value?.getExpanded() ?? [],
    scrollTop: tocSectionRef.value?.containerRef()?.scrollTop ?? 0,
    altScrollTop: altSectionRef.value?.containerRef()?.scrollTop ?? 0,
  }
}

function applyState(saved: TocPersistState) {
  if (saved.searchQuery) searchQuery.value = saved.searchQuery
  // Expanded state must land before the scroll: expanding rows changes the
  // scroll height, so restoring the offset first would clamp it to the
  // collapsed tree's height and land the reader in the wrong place.
  if (saved.expanded?.length) tocSectionRef.value?.setExpanded(saved.expanded)
  applyAltState(saved)
  nextTick(() => {
    const toc = tocSectionRef.value?.containerRef()
    if (toc && saved.scrollTop != null) toc.scrollTop = saved.scrollTop
  })
}

/**
 * The alternate-structure tree restores on its own schedule.
 *
 * Its section is keyed by `structure.id` and fed by a load separate from the main
 * TOC's — with no `loading` flag of its own — so it can mount, or REMOUNT, well
 * after applyState has run. Setting its expanded ids too early is silently thrown
 * away with the old component instance, which is exactly what happened: the ids
 * persisted correctly and the tree still came back collapsed.
 */
// restoreState is called through defineExpose from useBookViewSessionRestore, which reaches
// it after both an await and a nextTick — so the watches below are created with no active
// effect scope and unmount disposes none of them. Each self-stops on success, but neither
// condition is guaranteed: a book with no alt-TOC structure never satisfies the first, and a
// tab closed while the TOC is still loading never satisfies the second. Track and dispose.
const restoreWatchStops = new Set<() => void>()
function trackRestoreWatch(stop: () => void): () => void {
  restoreWatchStops.add(stop)
  return () => {
    restoreWatchStops.delete(stop)
    stop()
  }
}
onBeforeUnmount(() => {
  for (const stop of restoreWatchStops) stop()
  restoreWatchStops.clear()
})

function applyAltState(saved: TocPersistState) {
  const apply = () => {
    if (saved.altExpanded?.length) altSectionRef.value?.setExpanded(saved.altExpanded)
    nextTick(() => {
      const alt = altSectionRef.value?.containerRef()
      if (alt && saved.altScrollTop != null) alt.scrollTop = saved.altScrollTop
    })
  }
  if (altSectionRef.value) apply()
  // Re-apply on every (re)mount of the alt section until one sticks, then stop.
  let stop: (() => void) | undefined
  stop = trackRestoreWatch(
    watch(
      () => props.selectedAltTocSection?.structure.id,
      (id) => {
        if (id == null) return
        nextTick(() => {
          apply()
          stop?.()
        })
      },
      { flush: 'post' },
    ),
  )
}

function restoreState(saved: TocPersistState | undefined) {
  if (!saved) return
  // The sections only exist once the TOC has loaded — restoring into nothing
  // would silently drop the state, so wait for the load when one is in flight.
  if (props.loading) {
    let stop: (() => void) | undefined
    stop = trackRestoreWatch(
      watch(
        () => props.loading,
        (busy) => {
          if (busy) return
          stop?.()
          nextTick(() => applyState(saved))
        },
      ),
    )
    return
  }
  applyState(saved)
}

defineExpose({ snapshotState, restoreState })
</script>

<template>
  <TocTreeLayout v-model:query="searchQuery" :loading="loading" :error="error" @focus-list="focusTocList">
    <SplitPane :bottom-visible="hasToc && hasAlt" class="toc-body">
      <template #top>
        <TocTreeSection
          ref="tocSectionRef"
          v-if="hasToc"
          :title="null"
          :entries="tocEntries"
          :filter="searchQuery"
          :active-entry-id="activeTocEntryId"
          :search-tree="tocSearchTree"
          @select="$emit('select', $event as TocEntry)"
        />
      </template>
      <template #bottom>
        <TocTreeSection
          ref="altSectionRef"
          v-if="selectedAltTocSection"
          :key="selectedAltTocSection.structure.id"
          :title="null"
          :entries="selectedAltTocSection.entries"
          :active-entry-id="activeAltTocEntryId"
          :filter="searchQuery"
          :search-tree="selectedAltTocSection.searchTree ?? undefined"
          @select="$emit('altSelect', $event as TocEntry)"
        />
      </template>
    </SplitPane>
  </TocTreeLayout>
</template>

<style scoped>
.toc-body {
  flex: 1;
  min-height: 0;
}
</style>
