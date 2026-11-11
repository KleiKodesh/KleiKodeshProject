<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import type { AltTocPane } from './useBookViewToc'
import type { TocPersistState } from '../bookViewTypes'
import type { TocEntry } from '@/webview-host/queries.types'
import { SearchableTree } from './tocSearchUtils'
import type { TreeSearcher } from '@/utils/segmentSearchTree'
import TocTreeSection from '@/components/TocTreeSection.vue'
import TocTreeLayout from '@/components/TocTreeLayout.vue'
import SplitPane from '@/components/SplitPane.vue'

const props = defineProps<{
  activeTocEntryId?: number
  activeAltTocEntryId?: number
  tocEntries: TocEntry[]
  altTocPane: AltTocPane | null
  loading: boolean
  error: string | null
  tocSearchTree?: SearchableTree
  /**
   * The saved state of a restored session, given BEFORE the trees exist: each tree
   * is created at its saved row with its saved expanded ids (no second landing).
   * restoreState with this same object then only has the search box left to do.
   */
  initialState?: TocPersistState | null
}>()

const emit = defineEmits<{ select: [TocEntry]; altSelect: [TocEntry] }>()

const tocSectionRef = ref<InstanceType<typeof TocTreeSection> | null>(null)
const altSectionRef = ref<InstanceType<typeof TocTreeSection> | null>(null)
const mergedSectionRef = ref<InstanceType<typeof TocTreeSection> | null>(null)
const searchQuery = ref(props.initialState?.searchQuery ?? '')

// Focusing the search box on open / after loading lives in TocTreeLayout.

function focusTocList() {
  const el = (mergedSectionRef.value ?? tocSectionRef.value)?.containerRef?.()
  el?.focus()
}

const hasToc = computed(() => props.tocEntries.length > 0)
const hasAlt = computed(() => props.altTocPane != null)

// The alt pane's search tree. It builds nothing until it is first searched.
const altSearchTree = computed(() =>
  props.altTocPane ? (props.altTocPane.searchTree ?? new SearchableTree(props.altTocPane.entries)) : null,
)

// The two panes are only a display split. A search runs over both structures and shows
// ONE result list, searching the two trees together (no combined copy of both). Alt ids
// may collide with main TOC ids, so alt result rows get negative ids and are mapped back
// to their own entry on select.
const searching = computed(() => searchQuery.value.length > 0 && hasToc.value && hasAlt.value)
const altId = (id: number) => -id - 1
const merged = computed(() => {
  if (!searching.value || !props.tocSearchTree || !altSearchTree.value) return null
  const mainTree = props.tocSearchTree
  const altTree = altSearchTree.value
  const altEntries = props.altTocPane!.entries
  /** The alt entries behind the alt rows of the latest results. */
  const altById = new Map<number, TocEntry>()
  const searcher: TreeSearcher = {
    search: (_nodes, query, limit) => {
      altById.clear()
      const hits = SearchableTree.searchAcross(
        [{ tree: mainTree, nodes: props.tocEntries }, { tree: altTree, nodes: altEntries }],
        query,
        limit,
      )
      return hits.map(({ node, part }) => {
        if (part === 0) return node
        const e = node as TocEntry
        const id = altId(e.id)
        altById.set(id, e)
        return { ...e, id, parentId: e.parentId == null ? null : altId(e.parentId) }
      })
    },
    displayPaths: {
      get: (id) => {
        const alt = altById.get(id)
        return alt ? altTree.displayPaths.get(alt.id) : mainTree.displayPaths.get(id)
      },
    },
  }
  return { entries: props.tocEntries, altById, searcher }
})

function onMergedSelect(entry: TocEntry) {
  const alt = merged.value?.altById.get(entry.id)
  if (alt) emit('altSelect', alt)
  else emit('select', entry)
}

// ── Persistence ─────────────────────────────────────────────────────────────
// This whole subtree is v-if'd away when the panel closes and rebuilt on every
// tab switch, so none of the state below survives on its own. The book view
// snapshots it on save and hands it back through restoreState on mount.

function snapshotState(): Omit<TocPersistState, 'visible'> {
  return {
    searchQuery: searchQuery.value,
    expanded: tocSectionRef.value?.getExpanded() ?? [],
    altExpanded: altSectionRef.value?.getExpanded() ?? [],
    scroll: tocSectionRef.value?.getScrollPosition() ?? null,
    altScroll: altSectionRef.value?.getScrollPosition() ?? null,
  }
}

function applyState(saved: TocPersistState) {
  if (saved.searchQuery) searchQuery.value = saved.searchQuery
  // The expanded ids and the first visible row go in together: the row index means
  // a row of the tree as expanded, and the tree re-opens at it.
  if (saved.expanded?.length || saved.scroll) tocSectionRef.value?.setExpanded(saved.expanded ?? [], saved.scroll)
  applyAltState(saved)
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
    if (saved.altExpanded?.length || saved.altScroll) altSectionRef.value?.setExpanded(saved.altExpanded ?? [], saved.altScroll)
  }
  if (altSectionRef.value) apply()
  // Re-apply on every (re)mount of the alt section until one sticks, then stop.
  let stop: (() => void) | undefined
  stop = trackRestoreWatch(
    watch(
      () => props.altTocPane?.key,
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
  // The trees were created from it already (initialState).
  if (saved === props.initialState) return
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

/** The search box's reset button: clear the query, collapse both trees, back to the top. */
function resetTrees() {
  searchQuery.value = ''
  tocSectionRef.value?.reset()
  altSectionRef.value?.reset()
}

defineExpose({ snapshotState, restoreState })
</script>

<template>
  <TocTreeLayout
    v-model:query="searchQuery"
    :loading="loading"
    :error="error"
    resettable
    @focus-list="focusTocList"
    @reset="resetTrees"
  >
    <TocTreeSection
      v-if="merged"
      ref="mergedSectionRef"
      class="toc-body"
      :title="null"
      :entries="merged.entries"
      :filter="searchQuery"
      :search-tree="merged.searcher"
      @select="onMergedSelect($event as TocEntry)"
    />
    <SplitPane v-show="!merged" :bottom-visible="hasToc && hasAlt" class="toc-body">
      <template #top>
        <TocTreeSection
          ref="tocSectionRef"
          v-if="hasToc"
          :title="null"
          :entries="tocEntries"
          :filter="merged ? '' : searchQuery"
          :active-entry-id="activeTocEntryId"
          :initial-expanded="initialState?.expanded"
          :initial-position="initialState?.scroll"
          :search-tree="tocSearchTree"
          @select="$emit('select', $event as TocEntry)"
        />
      </template>
      <template #bottom>
        <TocTreeSection
          ref="altSectionRef"
          v-if="altTocPane"
          :key="altTocPane.key"
          :title="null"
          :entries="altTocPane.entries"
          :active-entry-id="activeAltTocEntryId"
          :initial-expanded="initialState?.altExpanded"
          :initial-position="initialState?.altScroll"
          :level-offset="altTocPane.levelOffset"
          :filter="merged ? '' : searchQuery"
          :search-tree="altSearchTree ?? undefined"
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
