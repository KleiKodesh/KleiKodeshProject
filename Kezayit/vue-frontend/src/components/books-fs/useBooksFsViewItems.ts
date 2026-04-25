import type { CategoryNode, BookRow } from './booksCategoryTree'
import type { FsItem } from './useBooksFs'

export function useBooksFsViewItems(
  getItems: () => FsItem[],
  handlers: {
    selectBook: (book: BookRow) => void
    enterFolder: (node: CategoryNode) => void
  },
) {
  function activateItem(item: FsItem) {
    item.kind === 'folder' ? handlers.enterFolder(item.node) : handlers.selectBook(item.book)
  }

  function activateIndex(index: number) {
    const item = getItems()[index]
    if (item) activateItem(item)
  }

  function getTitle(item: FsItem) {
    return item.kind === 'folder' ? item.node.title : item.book.title
  }

  return {
    activateItem,
    activateIndex,
    getTitle,
  }
}
