import { nextTick, type Ref } from 'vue'

// For regular (non-virtual) scrollers
interface RegularScrollOptions {
    behavior?: 'auto'
    block?: ScrollLogicalPosition
    inline?: ScrollLogicalPosition
    searchBarOffset?: number
}

// For virtual scrollers
interface VirtualScrollOptions {
    virtualScrollerRef: Ref<any>
    itemIndex: number
    block?: ScrollLogicalPosition
    inline?: ScrollLogicalPosition
    searchBarOffset?: number
}

/**
 * Scroll to an element in a regular (non-virtual) scroller
 * Two-tier approach: scrollIntoView → optional offset adjustment
 */
export async function scrollToElement(
    element: HTMLElement,
    options: RegularScrollOptions = {}
): Promise<void> {
    const {
        behavior = 'auto',
        block = 'nearest',
        inline = 'nearest',
        searchBarOffset = 0
    } = options

    // Tier 1: scrollIntoView
    element.scrollIntoView({ behavior, block, inline })

    // Tier 2: Apply search bar offset if needed
    if (searchBarOffset > 0) {
        await nextTick()
        const container = element.closest('.scroller') as HTMLElement
        if (container) {
            container.scrollTop -= searchBarOffset
        }
    }
}

/**
 * Scroll to an item in a virtual scroller (DynamicScroller)
 * Three-tier approach: scrollToItem → scrollIntoView → optional offset adjustment
 */
export async function scrollToVirtualItem(
    options: VirtualScrollOptions
): Promise<void> {
    const {
        virtualScrollerRef,
        itemIndex,
        block = 'nearest',
        inline = 'nearest',
        searchBarOffset = 0
    } = options

    // Tier 1: Use virtual scroller's API
    await virtualScrollerRef.value?.scrollToItem(itemIndex)
    await nextTick()

    // Tier 2: Fine-tune with scrollIntoView
    const element = virtualScrollerRef.value?.$el.querySelector(
        `[data-index="${itemIndex}"]`
    )
    if (element) {
        element.scrollIntoView({
            behavior: 'auto',
            block,
            inline
        })
    }

    // Tier 3: Apply search bar offset using virtual scroller's API
    if (searchBarOffset > 0) {
        await nextTick()
        const scrollerEl = virtualScrollerRef.value?.$el
        if (scrollerEl) {
            scrollerEl.scrollTop -= searchBarOffset
        }
    }
}
/**
 * Scroll to an element and center it in the viewport
 * Two-tier approach: scrollIntoView(nearest) → manual centering
 * This prevents parent container scrolling while achieving centering
 */
export async function scrollToElementCentered(
    element: HTMLElement
): Promise<void> {
    // Tier 1: Use nearest to get element into view without affecting parents
    element.scrollIntoView({ behavior: 'auto', block: 'nearest', inline: 'nearest' })
    await nextTick()

    // Tier 2: Manually center within the scrollable container
    const container = element.closest('.overflow-y, .scroller') as HTMLElement
    if (container) {
        const containerRect = container.getBoundingClientRect()
        const elementRect = element.getBoundingClientRect()

        // Calculate the offset needed to center the element
        const containerCenter = containerRect.height / 2
        const elementRelativeTop = elementRect.top - containerRect.top
        const elementCenter = elementRelativeTop + (elementRect.height / 2)

        // Adjust scroll to center the element
        const scrollAdjustment = elementCenter - containerCenter
        container.scrollTop += scrollAdjustment
    }
}
/**
 * Scroll to an item in a virtual scroller and center it in the viewport
 * Three-tier approach: scrollToItem → scrollIntoView(nearest) → manual centering
 * This prevents parent container scrolling while achieving centering
 */
export async function scrollToVirtualItemCentered(
    virtualScrollerRef: Ref<any>,
    itemIndex: number
): Promise<void> {
    // Tier 1: Use virtual scroller's API
    await virtualScrollerRef.value?.scrollToItem(itemIndex)
    await nextTick()

    // Tier 2: Use nearest to get element into view without affecting parents
    const element = virtualScrollerRef.value?.$el.querySelector(
        `[data-index="${itemIndex}"]`
    )
    if (!element) return

    element.scrollIntoView({ behavior: 'auto', block: 'nearest', inline: 'nearest' })
    await nextTick()

    // Tier 3: Manually center within the virtual scroller container
    const scrollerEl = virtualScrollerRef.value?.$el
    if (scrollerEl) {
        const containerRect = scrollerEl.getBoundingClientRect()
        const elementRect = element.getBoundingClientRect()

        // Calculate the offset needed to center the element
        const containerCenter = containerRect.height / 2
        const elementRelativeTop = elementRect.top - containerRect.top
        const elementCenter = elementRelativeTop + (elementRect.height / 2)

        // Adjust scroll to center the element
        const scrollAdjustment = elementCenter - containerCenter
        scrollerEl.scrollTop += scrollAdjustment
    }
}
