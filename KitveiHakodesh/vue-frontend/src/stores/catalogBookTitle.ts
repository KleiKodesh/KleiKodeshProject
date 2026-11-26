import { useLocalPathDisplayStore } from '@/stores/localPathDisplayStore'

/**
 * The title to SHOW for a tab / recent / history entry: the title stored when it was
 * recorded. A Torat Emet file shows its Hebrew title once Core has answered
 * (stores/localPathDisplayStore); the stored title is not rewritten.
 */
export function catalogBookTitle(entry: { route: string; bookId?: number; title: string; localFilePath?: string }): string {
  if (entry.route !== '/book-view' && entry.localFilePath) return useLocalPathDisplayStore().titleOf(entry.localFilePath, false) || entry.title
  return entry.title
}
