import { ref, shallowRef, watch, type Ref } from 'vue'
import { findPlaces } from './zmanimPlaces'

/**
 * The options of a place picker: every place while the search box is empty, else the
 * matches (prefix matches first). The names load only once the picker is open.
 */
export function usePlacePicker(open: Ref<boolean>) {
  const query = ref('')
  const options = shallowRef<Array<{ id: number, name: string }>>([])
  let generation = 0

  watch([open, query], async ([isOpen, text]) => {
    if (!isOpen) return
    const mine = ++generation
    const found = await findPlaces(text)
    if (mine === generation) options.value = found
  }, { immediate: true })

  watch(open, (isOpen) => { if (!isOpen) query.value = '' })

  return { query, options }
}
