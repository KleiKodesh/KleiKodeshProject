import type { Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { DynamicScroller } from 'vue-virtual-scroller'

/**
 * Reusable keyboard navigation for virtual scrollers
 * Handles Ctrl+Home (scroll to first) and Ctrl+End (scroll to last)
 * 
 * @param scrollerRef - Reference to DynamicScroller instance
 * @param totalItems - Computed total number of items
 * @param enabled - Optional ref to enable/disable shortcuts (default: true)
 */
export function useVirtualScrollerKeyboard(
    scrollerRef: Ref<InstanceType<typeof DynamicScroller> | null>,
    totalItems: Ref<number>,
    enabled: Ref<boolean> = { value: true } as Ref<boolean>
) {
    useEventListener('keydown', (event: KeyboardEvent) => {
        if (!enabled.value) return
        if (!scrollerRef.value) return

        const hasCtrlOrMeta = event.ctrlKey || event.metaKey

        // Ctrl+Home: Scroll to first item
        if (hasCtrlOrMeta && event.code === 'Home') {
            event.preventDefault()
            scrollToFirst()
        }

        // Ctrl+End: Scroll to last item
        if (hasCtrlOrMeta && event.code === 'End') {
            event.preventDefault()
            scrollToLast()
        }
    })

    /**
     * Scroll to first item
     */
    function scrollToFirst() {
        if (!scrollerRef.value) return
        scrollerRef.value.scrollToItem(0)
    }

    /**
     * Scroll to last item and adjust to actual end
     */
    function scrollToLast() {
        if (!scrollerRef.value) return
        if (totalItems.value === 0) return

        const lastIndex = totalItems.value - 1
        const scrollerEl = scrollerRef.value.$el as HTMLElement

        if (!scrollerEl) return

        // Hide scrolling during navigation
        scrollerEl.style.overflow = 'hidden'
        scrollerEl.style.pointerEvents = 'none'

        // Scroll to last item
        scrollerRef.value.scrollToItem(lastIndex)

        // Adjust to actual end after render
        setTimeout(() => {
            if (scrollerRef.value) {
                scrollerRef.value.scrollToItem(lastIndex)

                setTimeout(() => {
                    if (scrollerEl) {
                        // Scroll to absolute bottom
                        scrollerEl.scrollTop = scrollerEl.scrollHeight
                        scrollerEl.style.overflow = ''
                        scrollerEl.style.pointerEvents = ''
                    }
                }, 10)
            }
        }, 50)
    }

    return {
        scrollToFirst,
        scrollToLast
    }
}
