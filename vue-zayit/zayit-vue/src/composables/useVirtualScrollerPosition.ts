import type { Ref } from 'vue'
import { watch, onUnmounted } from 'vue'
import type { DynamicScroller } from 'vue-virtual-scroller'

export interface VirtualScrollerPosition {
    itemIndex: number
    itemOffset: number
}

const STORAGE_KEY_PREFIX = 'vscroller-pos-'
const SAVE_DEBOUNCE_MS = 300

export function useVirtualScrollerPosition(
    scrollerRef: Ref<InstanceType<typeof DynamicScroller> | null>,
    positionId: Ref<string>,
    options?: {
        skipRestore?: Ref<boolean>
        onRestore?: (itemIndex: number) => Promise<void> | void
    }
) {
    const skipRestore = options?.skipRestore
    const onRestore = options?.onRestore

    let saveDebounce: ReturnType<typeof setTimeout> | undefined
    let removeScrollListener: (() => void) | undefined
    let restoreAbortController: AbortController | undefined
    let suppressSave = false
    let lastKnownGoodPosition: VirtualScrollerPosition | null = null

    // --- Storage ---

    function storageKey() {
        return STORAGE_KEY_PREFIX + positionId.value
    }

    function saveToStorage(pos: VirtualScrollerPosition) {
        try {
            localStorage.setItem(storageKey(), JSON.stringify(pos))
        } catch (e) {
            console.warn('[VirtualScroller] Save failed:', e)
        }
    }

    function loadFromStorage(): VirtualScrollerPosition | null {
        try {
            const raw = localStorage.getItem(storageKey())
            const pos = raw ? (JSON.parse(raw) as VirtualScrollerPosition) : null
            return pos
        } catch {
            return null
        }
    }

    // --- Position capture ---

    function capturePosition(): VirtualScrollerPosition | null {
        const el = scrollerRef.value?.$el as HTMLElement | undefined
        if (!el) return null

        const scrollerRect = el.getBoundingClientRect()

        for (const itemEl of el.querySelectorAll('[data-index]')) {
            const rect = itemEl.getBoundingClientRect()
            if (rect.bottom > scrollerRect.top && rect.top < scrollerRect.bottom) {
                const index = parseInt(itemEl.getAttribute('data-index') ?? '-1')
                if (index >= 0) {
                    const pos = {
                        itemIndex: index,
                        itemOffset: rect.top - scrollerRect.top
                    }
                    return pos
                }
            }
        }

        return null
    }

    function savePosition() {
        if (suppressSave) {
            return
        }
        const pos = capturePosition()
        if (pos) {
            saveToStorage(pos)
            lastKnownGoodPosition = pos
        }
    }

    // --- Restore ---

    function waitForItems(signal: AbortSignal): Promise<HTMLElement> {
        return new Promise((resolve, reject) => {
            const check = () => {
                if (signal.aborted) return reject(new DOMException('Aborted', 'AbortError'))
                const el = scrollerRef.value?.$el as HTMLElement | undefined
                if (el && el.querySelector('[data-index]')) return resolve(el)
                requestAnimationFrame(check)
            }
            requestAnimationFrame(check)
        })
    }

    async function restorePosition(signal: AbortSignal): Promise<void> {
        if (skipRestore?.value) {
            skipRestore.value = false
            return
        }

        const saved = loadFromStorage()
        if (!saved) return

        let el: HTMLElement
        try {
            el = await waitForItems(signal)
        } catch {
            return
        }

        if (signal.aborted) return

        // Notify consumer to prioritize loading lines around target
        await onRestore?.(saved.itemIndex)
        if (signal.aborted) return

        // Scroll to the target item
        scrollerRef.value?.scrollToItem(saved.itemIndex)

        // After the scroller paints, apply the sub-item pixel offset
        requestAnimationFrame(() => {
            if (signal.aborted || !scrollerRef.value) return

            const itemEl = el.querySelector(`[data-index="${saved.itemIndex}"]`)
            if (!itemEl) return

            const scrollerRect = el.getBoundingClientRect()
            const itemRect = itemEl.getBoundingClientRect()
            const currentOffset = itemRect.top - scrollerRect.top
            const delta = currentOffset - saved.itemOffset

            if (Math.abs(delta) > 2) {
                el.scrollTop += delta
            }
        })
    }

    function triggerRestore() {
        restoreAbortController?.abort()
        restoreAbortController = new AbortController()
        restorePosition(restoreAbortController.signal)
    }

    // --- Scroll listener ---

    function setupScrollListener(el: HTMLElement) {
        const onScroll = () => {
            clearTimeout(saveDebounce)
            saveDebounce = setTimeout(savePosition, SAVE_DEBOUNCE_MS)
        }
        el.addEventListener('scroll', onScroll, { passive: true })
        return () => {
            el.removeEventListener('scroll', onScroll)
            clearTimeout(saveDebounce)
        }
    }

    // --- Lifecycle ---

    let firstMount = true

    watch(scrollerRef, (scroller) => {
        removeScrollListener?.()
        removeScrollListener = undefined

        if (!scroller) return

        const el = scroller.$el as HTMLElement
        removeScrollListener = setupScrollListener(el)

        if (firstMount) {
            firstMount = false
            triggerRestore()
        }
    }, { immediate: true })

    watch(positionId, (newId, oldId) => {
        if (!oldId || newId === oldId) return
        triggerRestore()
    })

    onUnmounted(() => {
        restoreAbortController?.abort()
        clearTimeout(saveDebounce)
        if (suppressSave && lastKnownGoodPosition) {
            // Mid-restore: trust lastKnownGoodPosition over drifting scrollTop
            saveToStorage(lastKnownGoodPosition)
        } else {
            suppressSave = false
            savePosition()
        }
        removeScrollListener?.()
    })

    return {
        hasSavedPosition: () => loadFromStorage() !== null
    }
}