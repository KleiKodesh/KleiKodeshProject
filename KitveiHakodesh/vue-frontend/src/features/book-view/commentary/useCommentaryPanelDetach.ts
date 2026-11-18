/**
 * Whether ONE commentary panel is shown in its own window, and whether it follows
 * the book while it is there.
 *
 * Only the state lives here. Opening the window is BookViewPage's job (it renders
 * the panel inside a DetachedWindow), so this file knows nothing about windows.
 *
 * A panel in its place inside the book view always follows the book; the choice
 * exists only for a detached one, and returning it to its place resets it.
 */
import { ref } from 'vue'

export function useCommentaryPanelDetach() {
  const isDetached = ref(false)
  const followsBook = ref(true)

  function detach() {
    isDetached.value = true
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
  }

  return { isDetached, followsBook, detach, attach, toggleDetached, toggleFollowsBook }
}

export type CommentaryPanelDetach = ReturnType<typeof useCommentaryPanelDetach>
