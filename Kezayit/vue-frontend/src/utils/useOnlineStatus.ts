import { useOnline } from '@vueuse/core'

/** Reactive boolean — true when the browser has network connectivity. */
export function useOnlineStatus() {
  return useOnline()
}
