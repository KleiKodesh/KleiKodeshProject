import type { Ref } from 'vue'
import { watch, onUnmounted, nextTick } from 'vue'
import type { DynamicScroller } from 'vue-virtual-scroller'
import { LRUStorage } from '@/utils/lruStorage'

export interface VirtualScrollerPosition {
    itemIndex: number
    itemOffset: number
}

const STORAGE_KEY_PREFIX = 'vscroller-pos-'
const SAVE_DEBOUNCE_MS = 300
const MAX_STORED_POSITIONS = 1000

// Shared LRU storage instance for all virtual scrollers
const lruStorage = new LRUStorage(STORAGE_KEY_PREFIX, MAX_STORED_POSITIONS)

export function useVirtualScrollerPosition(
    scrollerRef: Ref<InstanceType<typeof DynamicScroller> | null>,
    positionId: Ref<string>,
    options?: {
        skipRestore?: Ref<boolean>
        onRestore?: (itemIndex: number) => Promise<void> | void
        onRestoreComplete?: () => void
    }
) {
    const skipRestore = options?.skipRestore
    const onRestore = options?.onRestore
    const onRestoreComplete = options?.onRestoreComplete

    let saveDebounce: ReturnType<typeof setTimeout> | undefined
    let removeScrollListener: (() => void) | undefined
    let restoreAbortController: AbortController | undefined
    let suppressSave = false
    let lastKnownGoodPosition: VirtualScrollerPosition | null = null

    // --- Storage ---

    function storageKey() {
        return positionId.value // LRU storage already has prefix
    }

    function saveToStorage(pos: VirtualScrollerPosition) {
        try {
            const key = storageKey()
            lruStorage.setItem(key, JSON.stringify(pos))
            console.log('[VirtualScroller] 💾 SAVED:', {
                positionId: positionId.value,
                storageKey: STORAGE_KEY_PREFIX + key,
                itemIndex: pos.itemIndex,
                itemOffset: pos.itemOffset,
                totalStored: lruStorage.getSize()
            })
        } catch (e) {
            console.warn('[VirtualScroller] Save failed:', e)
        }
    }

    function loadFromStorage(): VirtualScrollerPosition | null {
        try {
            const key = storageKey()
            const raw = lruStorage.getItem(key)
            const pos = raw ? (JSON.parse(raw) as VirtualScrollerPosition) : null
            console.log('[VirtualScroller] 📂 LOADED:', {
                positionId: positionId.value,
                storageKey: STORAGE_KEY_PREFIX + key,
                found: !!pos,
                itemIndex: pos?.itemIndex,
                itemOffset: pos?.itemOffset,
                totalStored: lruStorage.getSize()
            })
            return pos
        } catch {
            console.log('[VirtualScroller] 📂 LOAD FAILED:', {
                positionId: positionId.value,
                storageKey: STORAGE_KEY_PREFIX + storageKey()
            })
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
            console.log('[VirtualScroller] ⏭️ SAVE SUPPRESSED:', {
                positionId: positionId.value,
                reason: 'suppressSave flag is true'
            })
            return
        }
        const pos = capturePosition()
        if (pos) {
            saveToStorage(pos)
            lastKnownGoodPosition = pos
        } else {
            console.log('[VirtualScroller] ⚠️ SAVE SKIPPED:', {
                positionId: positionId.value,
                reason: 'Could not capture position'
            })
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
        console.log('[VirtualScroller] 🔄 RESTORE STARTED:', {
            positionId: positionId.value,
            skipRestore: skipRestore?.value
        })

        if (skipRestore?.value) {
            skipRestore.value = false
            console.log('[VirtualScroller] ⏭️ RESTORE SKIPPED:', {
                positionId: positionId.value,
                reason: 'skipRestore flag was true'
            })
            return
        }

        const saved = loadFromStorage()
        if (!saved) {
            console.log('[VirtualScroller] ⏭️ RESTORE SKIPPED:', {
                positionId: positionId.value,
                reason: 'No saved position found'
            })
            return
        }

        let el: HTMLElement
        try {
            el = await waitForItems(signal)
        } catch {
            console.log('[VirtualScroller] ❌ RESTORE ABORTED:', {
                positionId: positionId.value,
                reason: 'Wait for items failed or aborted'
            })
            return
        }

        if (signal.aborted) {
            console.log('[VirtualScroller] ❌ RESTORE ABORTED:', {
                positionId: positionId.value,
                reason: 'Signal aborted after waitForItems'
            })
            return
        }

        // Notify consumer to prioritize loading lines around target
        await onRestore?.(saved.itemIndex)
        if (signal.aborted) {
            console.log('[VirtualScroller] ❌ RESTORE ABORTED:', {
                positionId: positionId.value,
                reason: 'Signal aborted after onRestore callback'
            })
            return
        }

        // Scroll to the target item
        console.log('[VirtualScroller] 📍 SCROLLING TO ITEM:', {
            positionId: positionId.value,
            itemIndex: saved.itemIndex,
            savedOffset: saved.itemOffset
        })
        scrollerRef.value?.scrollToItem(saved.itemIndex)

        // Wait for scroll to complete and log actual position
        await nextTick()

        const actualPos = capturePosition()
        console.log('[VirtualScroller] ✅ RESTORE COMPLETE:', {
            positionId: positionId.value,
            requested: {
                itemIndex: saved.itemIndex,
                itemOffset: saved.itemOffset
            },
            actual: actualPos ? {
                itemIndex: actualPos.itemIndex,
                itemOffset: actualPos.itemOffset
            } : null,
            match: actualPos ?
                (actualPos.itemIndex === saved.itemIndex ? '✓ Index matches' : '✗ Index mismatch') :
                '✗ Could not capture'
        })

        // Notify component that restore is complete
        onRestoreComplete?.()
    }

    function triggerRestore() {
        console.log('[VirtualScroller] 🎯 TRIGGER RESTORE:', {
            positionId: positionId.value,
            abortingPrevious: !!restoreAbortController
        })
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
        console.log('[VirtualScroller] 🔀 POSITION ID CHANGED:', {
            oldId,
            newId,
            willRestore: !!oldId && newId !== oldId
        })
        if (!oldId || newId === oldId) {
            console.log('[VirtualScroller] ⏭️ SKIPPING ID CHANGE:', {
                reason: !oldId ? 'No old ID (first mount)' : 'IDs are the same',
                oldId,
                newId
            })
            return
        }

        // CRITICAL: Save current position immediately using OLD ID before switching
        // The debounced save might not have fired yet
        clearTimeout(saveDebounce)
        const pos = capturePosition()
        if (pos) {
            try {
                // Save using the OLD position ID (not the current one)
                lruStorage.setItem(oldId, JSON.stringify(pos))
                console.log('[VirtualScroller] 💾 IMMEDIATE SAVE on ID change:', {
                    oldId,
                    newId,
                    itemIndex: pos.itemIndex,
                    itemOffset: pos.itemOffset
                })
            } catch (e) {
                console.warn('[VirtualScroller] Failed to save on ID change:', e)
            }
        } else {
            console.log('[VirtualScroller] ⚠️ IMMEDIATE SAVE FAILED on ID change:', {
                oldId,
                newId,
                reason: 'Could not capture position'
            })
        }

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