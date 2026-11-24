import { defineStore } from 'pinia'
import { ref } from 'vue'
import { addUserEdition, listUserEditions, removeUserEdition } from '@/webview-host/seforimApi'
import type { UserEdition } from '@/webview-host/queries.types'
import { useSettingsStore } from './settingsStore'

/**
 * The reader's own scanned editions per book - PDFs from their computer and HebrewBooks books
 * they added in the book view's editions dropdown. Saved in the library's user_settings.db
 * (service side); this store only holds what was last loaded per book, so the dropdown and
 * the side panel read one list.
 */
export const useUserEditionsStore = defineStore('userEditions', () => {
  const byBook = ref<Record<number, UserEdition[]>>({})

  function editionsOf(bookId: number): UserEdition[] {
    return byBook.value[bookId] ?? []
  }

  async function load(bookId: number): Promise<void> {
    if (bookId <= 0) return
    const rows = await listUserEditions(bookId, useSettingsStore().hebrewBooksLocalFolder || null)
    byBook.value = { ...byBook.value, [bookId]: rows }
  }

  async function addFile(bookId: number, filePath: string, title: string): Promise<UserEdition | null> {
    const row = await addUserEdition({ bookId, kind: 'file', filePath, hbId: null, title })
    await load(bookId)
    return row
  }

  async function addHebrewBook(bookId: number, hbId: string, title: string): Promise<UserEdition | null> {
    const row = await addUserEdition({ bookId, kind: 'hb', filePath: null, hbId, title })
    await load(bookId)
    return row
  }

  async function remove(bookId: number, id: number): Promise<void> {
    // Off the list at once; a failed delete comes back with the reload.
    byBook.value = { ...byBook.value, [bookId]: editionsOf(bookId).filter((e) => e.id !== id) }
    await removeUserEdition(id)
    await load(bookId)
  }

  return { editionsOf, load, addFile, addHebrewBook, remove }
})
