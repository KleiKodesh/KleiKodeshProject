import { nextTick, type Ref } from 'vue'

// For regular (non-virtual) scrollers
interface RegularScrollOptions {
    behavior?: ScrollBehavior
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

/**
 * Scroll to an element and position it at the top of the container
 * Two-tier approach: scrollIntoView(nearest) → manual positioning to top
 * This prevents parent container scrolling while achieving top positioning
 */
export async function scrollToElementTop(
    element: HTMLElement,
    options: { behavior?: ScrollBehavior; topOffset?: number } = {}
): Promise<void> {
    const { behavior = 'instant', topOffset = 0 } = options

    // Tier 1: Use nearest to get element into view without affecting parents
    element.scrollIntoView({ behavior, block: 'nearest', inline: 'nearest' })
    await nextTick()

    // Tier 2: Manually position at top of the scrollable container
    // Find the scrollable container by looking for overflow-y or specific class
    let container: HTMLElement | null = element.parentElement

    while (container) {
        const style = window.getComputedStyle(container)
        const overflowY = style.overflowY

        // Check if this element is scrollable
        if (overflowY === 'auto' || overflowY === 'scroll' ||
            container.classList.contains('commentary-content') ||
            container.classList.contains('scroller')) {
            break
        }

        container = container.parentElement
    }

    if (container) {
        const containerRect = container.getBoundingClientRect()
        const elementRect = element.getBoundingClientRect()

        // Calculate the offset needed to position element at top
        const elementRelativeTop = elementRect.top - containerRect.top

        // Adjust scroll to position element at top (with optional offset)
        container.scrollTop += elementRelativeTop - topOffset
    }
}

/**
 * Scroll to an element and center it in the container
 * Two-tier approach: scrollIntoView(nearest) → manual centering
 * This prevents parent container scrolling while achieving centering
 */
export async function scrollToElementCenter(
    element: HTMLElement,
    options: { behavior?: ScrollBehavior } = {}
): Promise<void> {
    const { behavior = 'instant' } = options

    // Tier 1: Use nearest to get element into view without affecting parents
    element.scrollIntoView({ behavior, block: 'nearest', inline: 'nearest' })
    await nextTick()

    // Tier 2: Manually center within the scrollable container
    // Find the scrollable container by looking for overflow-y or specific class
    let container: HTMLElement | null = element.parentElement

    while (container) {
        const style = window.getComputedStyle(container)
        const overflowY = style.overflowY

        // Check if this element is scrollable
        if (overflowY === 'auto' || overflowY === 'scroll' ||
            container.classList.contains('combobox-dropdown') ||
            container.classList.contains('scroller')) {
            break
        }

        container = container.parentElement
    }

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
