import type { Ref } from 'vue'
import { ref, watch, onUnmounted, nextTick } from 'vue'
import type { DynamicScroller } from 'vue-virtual-scroller'

/**
 * Position state for virtual scroller restoration
 */
export interface VirtualScrollerPosition {
    scrollTop: number          // Raw scroll position (Layer 1)
    itemIndex: number          // Index of top visible item (Layer 2)
    itemOffset: number         // Pixel offset of item from viewport top (Layer 2)
}

/**
 * 100% Self-Sufficient Virtual Scroller Position Manager
 * 
 * Automatically saves scroll position to localStorage and restores it.
 * Just pass in your scroller ref and a unique position ID - that's it!
 * 
 * @param scrollerRef - Reference to DynamicScroller instance
 * @param positionId - Unique ID for this scroll position
 * @param skipRestore - Optional ref to skip next restore (for overrides)
 */
export function useVirtualScrollerPosition(
    scrollerRef: Ref<InstanceType<typeof DynamicScroller> | null>,
    positionId: Ref<string>,
    skipRestore?: Ref<boolean>
) {
    // Constants
    const STORAGE_KEY_PREFIX = 'vscroller-pos-'
    const SAVE_DEBOUNCE_MS = 300
    const RESTORE_DELAY_MS = 100
    const itemSelector = '[data-index]'
    const itemIndexAttribute = 'data-index'
    const minItemSize = 40

    // State
    let saveDebounce: number | undefined
    let scrollListener: (() => void) | undefined

    // Storage functions

    /**
     * Get storage key for current position ID
     */
    function getStorageKey(): string {
        return STORAGE_KEY_PREFIX + positionId.value
    }

    /**
     * Save position to localStorage
     */
    function saveToStorage(position: VirtualScrollerPosition) {
        try {
            const key = getStorageKey()
            localStorage.setItem(key, JSON.stringify(position))
            console.log('💾 [VirtualScroller] Saved position:', key, position)
        } catch (error) {
            console.warn('[VirtualScroller] Failed to save position:', error)
        }
    }

    /**
     * Load position from localStorage
     */
    function loadFromStorage(): VirtualScrollerPosition | null {
        try {
            const key = getStorageKey()
            const stored = localStorage.getItem(key)
            if (stored) {
                const position = JSON.parse(stored) as VirtualScrollerPosition
                console.log('📂 [VirtualScroller] Loaded position:', key, position)
                return position
            } else {
                console.log('📂 [VirtualScroller] No saved position for:', key)
            }
        } catch (error) {
            console.warn('[VirtualScroller] Failed to load position:', error)
        }
        return null
    }

    // Position capture

    /**
     * Capture current scroll position (both layers)
     */
    function capturePosition(): VirtualScrollerPosition | null {
        const scrollerEl = scrollerRef.value?.$el as HTMLElement | undefined
        if (!scrollerEl) return null

        // Layer 1: Raw scrollTop
        const scrollTop = scrollerEl.scrollTop

        // Layer 2: Top visible item and offset
        const topItem = getTopVisibleItem()

        if (!topItem) {
            // Fallback: estimate from scroll position
            const estimatedIndex = Math.floor(scrollTop / minItemSize)
            return {
                scrollTop,
                itemIndex: estimatedIndex,
                itemOffset: 0
            }
        }

        return {
            scrollTop,
            itemIndex: topItem.itemIndex,
            itemOffset: topItem.offset
        }
    }

    /**
     * Get the top visible item and its offset from viewport top
     */
    function getTopVisibleItem(): { itemIndex: number; offset: number } | null {
        const scrollerEl = scrollerRef.value?.$el as HTMLElement | undefined
        if (!scrollerEl) return null

        const scrollerRect = scrollerEl.getBoundingClientRect()
        const itemElements = scrollerEl.querySelectorAll(itemSelector)

        let topMostItem: { itemIndex: number; top: number } | null = null

        for (const itemEl of itemElements) {
            const itemRect = itemEl.getBoundingClientRect()

            // Check if this item is visible in the viewport
            if (itemRect.bottom > scrollerRect.top && itemRect.top < scrollerRect.bottom) {
                const itemIndex = parseInt(itemEl.getAttribute(itemIndexAttribute) || '-1')
                if (itemIndex >= 0) {
                    // Find the item that's closest to the top of the viewport
                    if (!topMostItem || itemRect.top < topMostItem.top) {
                        topMostItem = {
                            itemIndex: itemIndex,
                            top: itemRect.top
                        }
                    }
                }
            }
        }

        if (topMostItem) {
            const offset = topMostItem.top - scrollerRect.top
            return {
                itemIndex: topMostItem.itemIndex,
                offset: offset
            }
        }

        return null
    }

    // Automatic save

    /**
     * Save current position (debounced)
     */
    function savePosition() {
        const position = capturePosition()
        if (position) {
            saveToStorage(position)
        }
    }

    // Automatic restore

    /**
     * Restore position using item-based approach
     */
    async function restorePosition(position: VirtualScrollerPosition): Promise<void> {
        const scrollerEl = scrollerRef.value?.$el as HTMLElement | undefined
        if (!scrollerEl) return

        // Wait for items to render
        await new Promise(resolve => setTimeout(resolve, 50))

        const isItemVisible = checkItemVisibility(position.itemIndex)

        if (!isItemVisible) {
            // Item not visible - use scrollToItem
            await scrollToItem(position.itemIndex, position.itemOffset)
        } else {
            // Item is visible - fine-tune with offset if needed
            if (position.itemOffset !== undefined && position.itemOffset !== 0) {
                const currentPosition = getItemPosition(position.itemIndex)
                if (currentPosition !== null) {
                    const offsetDiff = currentPosition.offset - position.itemOffset
                    if (Math.abs(offsetDiff) > 5) {
                        scrollerEl.scrollTop = scrollerEl.scrollTop + offsetDiff
                    }
                }
            }
        }
    }

    /**
     * Check if a specific item is currently visible
     */
    function checkItemVisibility(itemIndex: number): boolean {
        const scrollerEl = scrollerRef.value?.$el as HTMLElement | undefined
        if (!scrollerEl) return false

        const scrollerRect = scrollerEl.getBoundingClientRect()
        const itemEl = scrollerEl.querySelector(`${itemSelector}[${itemIndexAttribute}="${itemIndex}"]`)

        if (!itemEl) return false

        const itemRect = itemEl.getBoundingClientRect()
        return itemRect.bottom > scrollerRect.top && itemRect.top < scrollerRect.bottom
    }

    /**
     * Get the current position of a specific item
     */
    function getItemPosition(itemIndex: number): { offset: number } | null {
        const scrollerEl = scrollerRef.value?.$el as HTMLElement | undefined
        if (!scrollerEl) return null

        const scrollerRect = scrollerEl.getBoundingClientRect()
        const itemEl = scrollerEl.querySelector(`${itemSelector}[${itemIndexAttribute}="${itemIndex}"]`)

        if (!itemEl) return null

        const itemRect = itemEl.getBoundingClientRect()
        const offset = itemRect.top - scrollerRect.top

        return { offset }
    }

    /**
     * Scroll to a specific item with optional pixel offset
     */
    async function scrollToItem(itemIndex: number, pixelOffset?: number): Promise<void> {
        if (!scrollerRef.value) return

        await nextTick()

        const scrollerEl = scrollerRef.value.$el as HTMLElement | undefined
        if (!scrollerEl) return

        // Check if scroller is ready (has items rendered)
        const hasItems = scrollerEl.querySelectorAll(itemSelector).length > 0
        if (!hasItems) {
            console.warn('[VirtualScroller] Scroller not ready, skipping scrollToItem')
            return
        }

        // Hide scrolling during the double-call hack
        scrollerEl.style.overflow = 'hidden'
        scrollerEl.style.pointerEvents = 'none'

        try {
            // First call
            scrollerRef.value.scrollToItem(itemIndex)

            // Second call after delay (the hack!)
            setTimeout(() => {
                if (scrollerRef.value && scrollerEl) {
                    scrollerRef.value.scrollToItem(itemIndex)

                    // Apply pixel offset if provided
                    if (pixelOffset !== undefined && pixelOffset !== 0) {
                        setTimeout(() => {
                            if (scrollerEl) {
                                scrollerEl.scrollTop = scrollerEl.scrollTop - pixelOffset
                            }
                        }, 20)
                    }

                    // Re-enable scrolling
                    setTimeout(() => {
                        if (scrollerEl) {
                            scrollerEl.style.overflow = ''
                            scrollerEl.style.pointerEvents = ''
                        }
                    }, pixelOffset !== undefined ? 30 : 10)
                }
            }, 50)

        } catch (error) {
            console.warn('[VirtualScroller] scrollToItem failed:', error)
            // Fallback: manual scroll
            const scrollTop = itemIndex * minItemSize
            scrollerEl.scrollTop = scrollTop
            setTimeout(() => {
                scrollerEl.style.overflow = ''
                scrollerEl.style.pointerEvents = ''
            }, 100)
        }
    }

    /**
     * Setup scroll listener for auto-saving
     */
    function setupScrollListener() {
        const scrollerEl = scrollerRef.value?.$el as HTMLElement | undefined
        if (!scrollerEl) return

        const handleScroll = () => {
            if (saveDebounce) {
                clearTimeout(saveDebounce)
            }

            saveDebounce = window.setTimeout(() => {
                savePosition()
            }, SAVE_DEBOUNCE_MS)
        }

        scrollerEl.addEventListener('scroll', handleScroll, { passive: true })

        // Return cleanup function
        return () => {
            scrollerEl.removeEventListener('scroll', handleScroll)
            if (saveDebounce) {
                clearTimeout(saveDebounce)
            }
        }
    }

    /**
     * Auto-restore position when scroller becomes available
     * @returns true if a position was restored, false otherwise
     */
    async function autoRestore(): Promise<boolean> {
        console.log('🔄 [VirtualScroller] autoRestore called')
        console.log('🔄 [VirtualScroller] Scroller ref:', !!scrollerRef.value)
        console.log('🔄 [VirtualScroller] Skip restore:', skipRestore?.value)

        if (!scrollerRef.value) {
            console.log('🔄 [VirtualScroller] No scroller ref, aborting restore')
            return false
        }

        // Check if restore should be skipped (for overrides)
        if (skipRestore?.value) {
            console.log('🔄 [VirtualScroller] Skipping restore due to flag')
            skipRestore.value = false
            return false
        }

        // Wait for items to be rendered
        await nextTick()
        await new Promise(resolve => setTimeout(resolve, RESTORE_DELAY_MS))

        // Check if scroller has items before attempting restore
        const scrollerEl = scrollerRef.value.$el as HTMLElement | undefined
        if (!scrollerEl) {
            console.log('🔄 [VirtualScroller] No scroller element, aborting restore')
            return false
        }

        const hasItems = scrollerEl.querySelectorAll(itemSelector).length > 0
        if (!hasItems) {
            console.warn('[VirtualScroller] No items rendered yet, skipping restore')
            return false
        }

        console.log('🔄 [VirtualScroller] Scroller has items, attempting restore')
        const savedPosition = loadFromStorage()
        if (savedPosition) {
            console.log('🔄 [VirtualScroller] Restoring position:', savedPosition)
            await restorePosition(savedPosition)
            return true
        } else {
            console.log('🔄 [VirtualScroller] No saved position to restore')
        }

        return false
    }

    // Lifecycle management

    let hasInitialized = false

    // Watch for scroller becoming available
    watch(scrollerRef, (newScroller) => {
        // Cleanup old listener
        if (scrollListener) {
            scrollListener()
            scrollListener = undefined
        }

        // Setup new listener
        if (newScroller) {
            scrollListener = setupScrollListener()

            // Only auto-restore on first mount, not on every scroller update
            if (!hasInitialized) {
                hasInitialized = true
                autoRestore()
            }
        }
    }, { immediate: true })

    // Watch for position ID changes (e.g., switching filters)
    watch(positionId, async (newId, oldId) => {
        if (!oldId) return // Skip initial mount

        console.log('🔄 [VirtualScroller] Position ID changed:', oldId, '→', newId)

        // Scroller stays mounted, just restore the new position
        await nextTick()

        // Wait a bit for data to update
        await new Promise(resolve => setTimeout(resolve, 100))

        const restored = await autoRestore()

        console.log('🔄 [VirtualScroller] Auto-restore result:', restored)
    })

    // Cleanup on unmount
    onUnmounted(() => {
        // Final save
        savePosition()

        // Cleanup listener
        if (scrollListener) {
            scrollListener()
        }

        if (saveDebounce) {
            clearTimeout(saveDebounce)
        }
    })

    // Composable is fully self-sufficient
    return {
        hasSavedPosition: () => loadFromStorage() !== null
    }
}
