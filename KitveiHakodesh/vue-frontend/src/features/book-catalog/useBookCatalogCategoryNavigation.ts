import { usePaneNavigation } from '@/composables/usePaneNavigation'

/**
 * Opens the book catalog at a given category — the usual '/books' destination
 * navigation, then the category id stamped on the tab it lands on.
 * BookCatalogPage consumes the stamp (tab.catalogCategoryId) and walks to it.
 */
export function useBookCatalogCategoryNavigation() {
  const paneNavigation = usePaneNavigation()

  function openCatalogCategory(categoryId: number, openInNewTab: boolean): void {
    paneNavigation.navigateToDestination('/books', openInNewTab)
    paneNavigation.updateActiveTab({ catalogCategoryId: categoryId })
  }

  return { openCatalogCategory }
}
