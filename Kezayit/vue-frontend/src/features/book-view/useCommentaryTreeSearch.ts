/**
 * Search logic for the commentary tree panel.
 * Builds a hierarchical search tree from the visibility list and provides
 * token-based search with union matching (any token matches).
 */
import { computed } from 'vue'
import { SegmentSearchTree } from '@/utils/segmentSearchTree'
import type { SearchableNode } from '@/utils/segmentSearchTree'
import { normalize } from '@/utils/normalizeText'
import type { CommentaryGroup } from './useCommentary'
import type { CommentaryTreeState, CommentaryVisibilityItem } from './bookViewTypes'
import type { CommentaryTreeNode } from './commentaryTreeTypes'

interface CommentarySearchNode extends SearchableNode {
  itemIndex: number // index into visibilityList; -1 for section/subsection nodes
}

export function useCommentaryTreeSearch(
  groups: () => CommentaryGroup[],
  treeState: CommentaryTreeState,
) {
  // ── Flat list sync ────────────────────────────────────────────────────────
  function itemKey(item: CommentaryVisibilityItem): string {
    return `${item.bookId}::${item.sectionLabel}::${item.subSectionLabel}`
  }

  function groupKey(group: CommentaryGroup): string {
    return `${group.bookId}::${group.sectionLabel ?? ''}::${group.subSectionLabel ?? ''}`
  }

  function syncVisibilityList() {
    const existing = new Map(treeState.visibilityList.map((item) => [itemKey(item), item]))
    treeState.visibilityList = groups().map((group) => {
      const found = existing.get(groupKey(group))
      return found ?? {
        bookId: group.bookId,
        sectionLabel: group.sectionLabel ?? '',
        subSectionLabel: group.subSectionLabel ?? '',
        bookTitle: group.bookTitle,
        isChecked: true,
        isInSearchResults: true,
      }
    })
  }

  // ── Search index ──────────────────────────────────────────────────────────
  const searchNodes = computed<CommentarySearchNode[]>(() => {
    const nodes: CommentarySearchNode[] = []
    let sectionIdCounter = -1
    const sectionIdMap = new Map<string, number>()

    for (let index = 0; index < treeState.visibilityList.length; index++) {
      const item = treeState.visibilityList[index]!
      const sectionLabel = normalize(item.sectionLabel || String(item.bookId))
      const subLabel = item.subSectionLabel ? normalize(item.subSectionLabel) : null

      const sectionKey = sectionLabel
      if (!sectionIdMap.has(sectionKey)) {
        sectionIdMap.set(sectionKey, sectionIdCounter--)
        nodes.push({
          id: sectionIdMap.get(sectionKey)!,
          parentId: null,
          text: sectionLabel,
          hasChildren: true,
          itemIndex: -1,
        })
      }
      const sectionId = sectionIdMap.get(sectionKey)!

      let parentId = sectionId
      if (subLabel) {
        const subKey = `${sectionLabel}::${subLabel}`
        if (!sectionIdMap.has(subKey)) {
          sectionIdMap.set(subKey, sectionIdCounter--)
          nodes.push({
            id: sectionIdMap.get(subKey)!,
            parentId: sectionId,
            text: subLabel,
            hasChildren: true,
            itemIndex: -1,
          })
        }
        parentId = sectionIdMap.get(subKey)!
      }

      const bookTitle = normalize(groups()[index]?.bookTitle ?? '')
      nodes.push({ id: index, parentId, text: bookTitle, hasChildren: false, itemIndex: index })
    }
    return nodes
  })

  const searchTree = computed(() => new SegmentSearchTree(searchNodes.value))
  const leafSearchNodes = computed(() => searchNodes.value.filter((n) => !n.hasChildren))
  const isSearching = computed(() =>
    treeState.searchQuery.trim().length > 0 || treeState.tokens.length > 0,
  )

  // Match a single query string against the leaf nodes — returns matched item indexes.
  function matchQuery(query: string): Set<number> {
    const trimmed = normalize(query.trim())
    if (!trimmed) return new Set()
    const words = trimmed.split(/\s+/).filter(Boolean)
    // Intersect per-word results so word order doesn't matter within one query
    const perWordSets = words.map((word) => {
      const results = searchTree.value.search(leafSearchNodes.value, word)
      return new Set(results.map((n) => (n as CommentarySearchNode).itemIndex))
    })
    return perWordSets.reduce((intersection, set) => {
      const result = new Set<number>()
      for (const index of intersection) {
        if (set.has(index)) result.add(index)
      }
      return result
    })
  }

  function applyFilter() {
    const allTokens = [
      ...treeState.tokens,
      ...(treeState.searchQuery.trim() ? [treeState.searchQuery] : []),
    ]
    if (!allTokens.length) {
      treeState.visibilityList.forEach((item) => { item.isInSearchResults = true })
      return
    }
    // Union: item matches if it appears in any token's result set
    const matched = new Set<number>()
    for (const token of allTokens) {
      for (const index of matchQuery(token)) matched.add(index)
    }
    treeState.visibilityList.forEach((item, index) => {
      item.isInSearchResults = matched.has(index)
    })
  }

  // ── Hierarchical tree ─────────────────────────────────────────────────────
  const tree = computed((): CommentaryTreeNode[] => {
    const root: CommentaryTreeNode[] = []
    let currentSection: CommentaryTreeNode | null = null
    let currentSubSection: CommentaryTreeNode | null = null

    for (const item of treeState.visibilityList) {
      const sectionLabel = item.sectionLabel || String(item.bookId)
      const subLabel = item.subSectionLabel || null

      if (!currentSection || currentSection.label !== sectionLabel) {
        currentSection = { label: sectionLabel, children: [] }
        currentSubSection = null
        root.push(currentSection)
      }

      if (subLabel) {
        if (!currentSubSection || currentSubSection.label !== subLabel) {
          currentSubSection = { label: subLabel, children: [] }
          currentSection.children.push(currentSubSection)
        }
        currentSubSection.children.push(item)
      } else {
        currentSubSection = null
        currentSection.children.push(item)
      }
    }
    return root
  })

  // ── Search results (flat list with display path) ──────────────────────────
  const searchResults = computed(() => {
    if (!isSearching.value) return []
    return treeState.visibilityList
      .map((item, index) => {
        const leafNode = searchNodes.value.find((n) => !n.hasChildren && (n as CommentarySearchNode).itemIndex === index)
        const parentPath = leafNode?.parentId != null
          ? (searchTree.value.displayPaths.get(leafNode.parentId) ?? '')
          : ''
        return { item, displayPath: parentPath }
      })
      .filter(({ item }) => item.isInSearchResults)
  })

  return {
    syncVisibilityList,
    applyFilter,
    isSearching,
    tree,
    searchResults,
  }
}
