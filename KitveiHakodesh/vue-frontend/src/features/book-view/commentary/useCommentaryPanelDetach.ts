/**
 * Whether ONE commentary panel is shown in its own window, and whether it follows
 * the book while it is there.
 *
 * Only the state lives here. Opening the window is BookViewPage's job (it renders
 * the panel inside a DetachedWindow), so this file knows nothing about windows.
 *
 * A panel in its place inside the book view always follows the book; the choice
 * applies only while it is detached. The reader's last choice is remembered per
 * panel (bottom / side / left side) and applied the next time that panel detaches.
 */
import { ref } from 'vue'
import { lsGet, lsSet } from '@/utils/persistence'
import type { CommentarySlot } from '../bookViewTypes'

const followsBookKey = (slot: CommentarySlot) => `commentary-detached-follows-book.${slot}`

export function useCommentaryPanelDetach(slot: CommentarySlot) {
  const isDetached = ref(false)
  const followsBook = ref(true)

  function detach() {
    isDetached.value = true
    followsBook.value = lsGet<boolean>(followsBookKey(slot)) ?? true
  }

  function attach() {
    isDetached.value = false
    followsBook.value = true
  }

  function toggleDetached() {
    if (isDetached.value) attach()
    else detach()
  }

  function toggleFollowsBook() {
    followsBook.value = !followsBook.value
    lsSet(followsBookKey(slot), followsBook.value)
  }

  return { isDetached, followsBook, detach, attach, toggleDetached, toggleFollowsBook }
}

export type CommentaryPanelDetach = ReturnType<typeof useCommentaryPanelDetach>
