import { computed, ref, shallowRef, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useUserSpaceStore, userSpaceRootKey, type UserSpaceRoot } from '@/stores/userSpaceStore'
import { findCategoryChain, withoutHiddenCategories, type CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import type { FsItem } from '@/features/book-catalog/useBookCatalog'
import { listLocalFolder } from '@/webview-host/bridge'

/**
 * UserSpace as a catalog-shaped tree, so the catalog's own views render it.
 *
 * Its root's children are the user's roots. A catalog root IS the catalog's own category
 * node (already in memory - booksDataStore loads the whole catalog). A computer root is a
 * node built here whose subfolders and files are filled from the service the first time it
 * is opened (`ensureLoaded`), one level at a time.
 */

// Ids of computer-folder nodes: far below anything the catalog uses (its own categories are
// positive, user-defined ones small negatives), so the views' id-keyed state never mixes them.
const FIRST_COMPUTER_NODE_ID = -1_000_000_000
const AREA_ROOT_ID = Number.MIN_SAFE_INTEGER
// The whole-catalog root: below the computer-folder ids, which count down from FIRST_COMPUTER_NODE_ID.
const WHOLE_CATALOG_ID = Number.MIN_SAFE_INTEGER + 1

function joinPath(folder: string, name: string): string {
  return `${folder.replace(/[\\/]+$/, '')}\\${name}`
}

export function useUserSpace() {
  const booksData = useBooksDataStore()
  const { ROOT: CATALOG_ROOT } = storeToRefs(booksData)
  const store = useUserSpaceStore()
  const { roots } = storeToRefs(store)

  // ── Computer folders ──────────────────────────────────────────────────────
  // One node per folder path for the page's lifetime, so a folder keeps its id (and with it
  // the tree's open/closed state) however often the root list is rebuilt.
  let nextComputerId = FIRST_COMPUTER_NODE_ID
  const computerNodes = new Map<string, CategoryNode>()
  const computerPathById = new Map<number, string>()
  const loadedIds = new Set<number>()
  const loadingIds = new Set<number>()
  /** Folders that could not be read, by node id. */
  const folderErrors = shallowRef(new Map<number, string>())
  // Bumped whenever a computer folder's contents arrive. The nodes are filled in place, so
  // this is what tells the views to read them again.
  const version = ref(0)

  function computerNode(path: string, title: string, level: number): CategoryNode {
    const key = path.toLowerCase()
    let node = computerNodes.get(key)
    if (!node) {
      const id = nextComputerId--
      node = { id, parentId: null, title, level, children: [], books: [], files: [] }
      computerNodes.set(key, node)
      computerPathById.set(id, path)
    }
    return node
  }

  /** Reads a computer folder's contents when it is opened. Catalog nodes need nothing. */
  async function ensureLoaded(node: CategoryNode): Promise<void> {
    const path = computerPathById.get(node.id)
    if (path === undefined || loadedIds.has(node.id) || loadingIds.has(node.id)) return
    loadingIds.add(node.id)
    const listing = await listLocalFolder(path)
    // Closed (or forgotten) while the read was in flight: its contents are not wanted any more.
    if (!loadingIds.delete(node.id)) return
    // A Torat Emet folder or book shows its Hebrew name (Core ToratEmetNames); the listing arrives ordered by the shown name.
    node.children = listing.folders.map((name, i) => computerNode(joinPath(path, name), listing.folderTitles[i] || name, node.level + 1))
    node.files = listing.files.map((name, i) => ({ path: joinPath(path, name), name, title: listing.fileTitles[i] || undefined }))
    setError(node.id, listing.error)
    // A failed read is retried the next time the folder is opened.
    if (!listing.error) loadedIds.add(node.id)
    version.value++
  }

  function setError(id: number, error: string | undefined) {
    if (!error && !folderErrors.value.has(id)) return
    const errors = new Map(folderErrors.value)
    error ? errors.set(id, error) : errors.delete(id)
    folderErrors.value = errors
  }

  /**
   * Drops a computer folder's contents when it is closed, so only open folders are held and
   * the next open reads the disk afresh. Its subfolders are forgotten outright: their nodes
   * go, so a reopened folder's subfolders start closed with new ids.
   */
  function unload(node: CategoryNode) {
    if (!computerPathById.has(node.id)) return
    for (const child of node.children) forget(child)
    node.children = []
    node.files = []
    loadedIds.delete(node.id)
    loadingIds.delete(node.id)
    setError(node.id, undefined)
    version.value++
  }

  function forget(node: CategoryNode) {
    const path = computerPathById.get(node.id)
    if (path === undefined) return
    for (const child of node.children) forget(child)
    computerNodes.delete(path.toLowerCase())
    computerPathById.delete(node.id)
    loadedIds.delete(node.id)
    loadingIds.delete(node.id)
    setError(node.id, undefined)
  }

  /** Every root's contents - the bar's home button closes everything. */
  function unloadAll() {
    for (const node of ROOT.value.children) unload(node)
  }

  // ── The tree ──────────────────────────────────────────────────────────────
  /** Root node id → the stored root it shows, for removing it. */
  const rootKeyById = new Map<number, string>()

  function rootNode(root: UserSpaceRoot): CategoryNode {
    if (root.kind === 'computer') {
      const node = computerNode(root.path, store.rootTitle(root), 0)
      node.title = store.rootTitle(root)   // the Hebrew name may arrive after the node was made
      return node
    }
    // The catalog's own shelves, as the catalog page browses them (hidden ones left out).
    if (root.kind === 'catalogAll')
      return { ...CATALOG_ROOT.value, id: WHOLE_CATALOG_ID, title: root.title, level: 0, children: withoutHiddenCategories(CATALOG_ROOT.value.children) }
    const chain = findCategoryChain(CATALOG_ROOT.value, root.categoryId)
    // A category the current database no longer has stays listed (empty) so it can be removed.
    return chain?.[chain.length - 1] ?? { id: root.categoryId, parentId: null, title: root.title, level: 0, children: [], books: [] }
  }

  const ROOT = computed<CategoryNode>(() => {
    void version.value
    rootKeyById.clear()
    const children = roots.value.map((root) => {
      const node = rootNode(root)
      rootKeyById.set(node.id, userSpaceRootKey(root))
      return node
    })
    return { id: AREA_ROOT_ID, parentId: null, title: '', level: -1, children, books: [] }
  })

  const path = shallowRef<CategoryNode[]>([ROOT.value])
  watch(ROOT, (root) => {
    path.value = [root, ...path.value.slice(1)]
  })

  const currentNode = computed(() => path.value[path.value.length - 1]!)
  const currentError = computed(() => folderErrors.value.get(currentNode.value.id) ?? null)

  const treeItems = computed((): FsItem[] => {
    void version.value
    const node = currentNode.value
    return [
      ...node.children.map((n) => ({ uid: `f-${n.id}`, kind: 'folder' as const, node: n })),
      ...node.books.map((b) => ({ uid: `b-${b.id}`, kind: 'book' as const, book: b })),
      ...(node.files ?? []).map((f) => ({ uid: `p-${f.path}`, kind: 'file' as const, file: f })),
    ]
  })

  /** The stored root a node IS (a direct child of the UserSpace root), or null. Identity, not
   *  id: a category inside the whole-catalog root can share its id with a root of its own. */
  function rootKeyOf(node: CategoryNode): string | null {
    return ROOT.value.children.includes(node) ? rootKeyById.get(node.id) ?? null : null
  }

  // The flat views hold open exactly the folders on the path: one left behind is closed
  // (unloaded), and the one arrived at is read afresh.
  function setPath(next: CategoryNode[]) {
    for (const node of path.value) if (!next.includes(node)) unload(node)
    const target = next[next.length - 1]!
    unload(target)
    path.value = next
    void ensureLoaded(target)
  }

  function enter(node: CategoryNode) {
    setPath([...path.value, node])
  }
  function navigateTo(index: number) {
    setPath(path.value.slice(0, index + 1))
  }
  function navigateToSibling(index: number, node: CategoryNode) {
    setPath([...path.value.slice(0, index), node])
  }

  function removeRoot(key: string) {
    store.remove(key)
    setPath([ROOT.value])
  }

  return {
    ROOT,
    path,
    treeItems,
    currentError,
    rootKeyOf,
    ensureLoaded,
    unload,
    unloadAll,
    enter,
    navigateTo,
    navigateToSibling,
    removeRoot,
    load: () => Promise.all([booksData.ensureLoaded(), store.ensureLoaded()]),
  }
}
