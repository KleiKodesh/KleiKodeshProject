import { ref, computed, type Ref } from 'vue'
import { useTabStore } from '@/data/stores/tabStore'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import { useSettingsStore } from '@/data/stores/settingsStore'
import { scrollToElement, scrollToElementTop } from '@/components/shared/useScrollToElement'
import { hasConnections } from '@/data/types/Book'
import type { CommentaryTreeNode } from './useCommentaryTree'

export function useCommentaryScroll(scrollContainer: Ref<HTMLElement | null>) {
    const tabStore = useTabStore()
    const categoryTreeStore = useCategoryTreeStore()
    const settingsStore = useSettingsStore()

    const currentZoom = computed(() => tabStore.activeTab?.bookState?.zoom || 100)

    const containerStyles = computed(() => ({
        fontSize: `calc(var(--commentary-font-size, 100%) * ${currentZoom.value / 100})`
    }))

    const intrinsicSize = computed(() => {
        const fontSize = settingsStore.commentaryFontSize / 100
        const lineHeight = settingsStore.commentaryLinePadding
        const zoom = currentZoom.value / 100
        const baseFontSize = 16
        const headerHeight = (baseFontSize * 1.1 * fontSize * zoom) + 8
        const contentHeight = 10 * baseFontSize * fontSize * lineHeight * zoom + 16
        return `auto ${Math.round(headerHeight + contentHeight)}px`
    })

    function saveScrollPosition() {
        if (!scrollContainer.value || !tabStore.activeTab?.bookState) return

        const containerRect = scrollContainer.value.getBoundingClientRect()
        const topY = containerRect.top + 50

        // Find which commentary group is at the top
        const groups = scrollContainer.value.querySelectorAll('[data-book-id]')
        for (let i = 0; i < groups.length; i++) {
            const groupElement = groups[i] as HTMLElement
            const rect = groupElement.getBoundingClientRect()
            if (rect.top <= topY && rect.bottom > topY) {
                tabStore.activeTab.bookState.commentaryScrollElementIndex = i

                // Find which link within this group is at the top
                const links = groupElement.querySelectorAll('.commentary-link')
                let linkIndex = 0
                let linkOffset = topY - rect.top

                for (let j = 0; j < links.length; j++) {
                    const linkRect = (links[j] as HTMLElement).getBoundingClientRect()
                    if (linkRect.top <= topY && linkRect.bottom > topY) {
                        linkIndex = j
                        linkOffset = topY - linkRect.top
                        break
                    }
                }

                tabStore.activeTab.bookState.commentaryScrollLinkIndex = linkIndex
                tabStore.activeTab.bookState.commentaryScrollOffset = linkOffset
                return
            }
        }
    }

    async function restoreScrollPosition(isFirstInit: boolean, queueGroupLoad?: (bookId: number, lineIndex: number, priority: boolean) => void) {
        if (!scrollContainer.value || isFirstInit) return

        const elementIndex = tabStore.activeTab?.bookState?.commentaryScrollElementIndex
        const linkIndex = tabStore.activeTab?.bookState?.commentaryScrollLinkIndex
        const offset = tabStore.activeTab?.bookState?.commentaryScrollOffset
        if (elementIndex === undefined) return

        // Stage 1: Find and scroll to the commentary group
        let targetElement: HTMLElement | null = null
        for (let attempt = 0; attempt < 10; attempt++) {
            await new Promise(resolve => requestAnimationFrame(resolve))
            const groups = scrollContainer.value.querySelectorAll('[data-book-id]')
            if (elementIndex < groups.length) {
                targetElement = groups[elementIndex] as HTMLElement
                break
            }
            if (attempt < 9) await new Promise(resolve => setTimeout(resolve, 50))
        }

        if (!targetElement) return

        const originalVisibility = targetElement.style.contentVisibility
        targetElement.style.contentVisibility = 'visible'

        await new Promise(resolve => requestAnimationFrame(resolve))
        await new Promise(resolve => requestAnimationFrame(resolve))
        await scrollToElement(targetElement, { block: 'nearest' })

        // Stage 2: Check if content is loaded, if not prioritize loading
        const links = targetElement.querySelectorAll('.commentary-link')
        const hasContent = links.length > 0 && links[0]?.textContent?.trim() !== 'טוען תוכן....'

        if (!hasContent && queueGroupLoad) {
            // Content not loaded yet - prioritize this group
            const bookId = parseInt(targetElement.getAttribute('data-book-id') || '0')
            if (bookId) {
                // Find the group's lineIndex from commentary groups
                // This will be passed from the component
                queueGroupLoad(bookId, 0, true) // Priority load

                // Wait for content to load (max 2 seconds)
                for (let i = 0; i < 40; i++) {
                    await new Promise(resolve => setTimeout(resolve, 50))
                    const updatedLinks = targetElement.querySelectorAll('.commentary-link')
                    const nowHasContent = updatedLinks.length > 0 && updatedLinks[0]?.textContent?.trim() !== 'טוען תוכן....'
                    if (nowHasContent) break
                }
            }
        }

        // Stage 3: Scroll to specific link within the group
        if (linkIndex !== undefined && linkIndex > 0) {
            const links = targetElement.querySelectorAll('.commentary-link')
            if (linkIndex < links.length) {
                const targetLink = links[linkIndex] as HTMLElement
                await new Promise(resolve => requestAnimationFrame(resolve))
                await scrollToElement(targetLink, { block: 'nearest' })
            }
        }

        // Stage 4: Apply fine-tuned offset
        if (offset !== undefined && offset !== 0) {
            for (let i = 0; i < 5; i++) {
                await new Promise(resolve => requestAnimationFrame(resolve))
                const containerRect = scrollContainer.value.getBoundingClientRect()

                // Get the target element (link if available, otherwise group)
                let targetRect
                if (linkIndex !== undefined && linkIndex > 0) {
                    const links = targetElement.querySelectorAll('.commentary-link')
                    if (linkIndex < links.length) {
                        targetRect = (links[linkIndex] as HTMLElement).getBoundingClientRect()
                    } else {
                        targetRect = targetElement.getBoundingClientRect()
                    }
                } else {
                    targetRect = targetElement.getBoundingClientRect()
                }

                const adjustment = offset - (containerRect.top + 50 - targetRect.top)
                if (Math.abs(adjustment) < 2) break
                scrollContainer.value.scrollTop += adjustment
                if (i < 4) await new Promise(resolve => setTimeout(resolve, 20))
            }
        }

        targetElement.style.contentVisibility = originalVisibility
    }

    async function scrollToGroup(bookId: number) {
        if (!scrollContainer.value) return
        const groupElement = scrollContainer.value.querySelector(`[data-book-id="${bookId}"]`) as HTMLElement
        if (!groupElement) return

        const originalVisibility = groupElement.style.contentVisibility
        groupElement.style.contentVisibility = 'visible'

        await new Promise(resolve => requestAnimationFrame(resolve))
        await scrollToElementTop(groupElement)

        await new Promise(resolve => requestAnimationFrame(resolve))
        const containerRect = scrollContainer.value.getBoundingClientRect()
        const elementRect = groupElement.getBoundingClientRect()
        if (Math.abs(elementRect.top - containerRect.top) >= 5) {
            await scrollToElementTop(groupElement)
        }

        groupElement.style.contentVisibility = originalVisibility
    }

    function detectVisibleGroup(emit: (event: 'visible-book-changed', bookId: number) => void) {
        if (!scrollContainer.value) return
        const topY = scrollContainer.value.getBoundingClientRect().top + 50
        const groups = scrollContainer.value.querySelectorAll('[data-book-id]')
        for (const group of groups) {
            const rect = group.getBoundingClientRect()
            if (rect.top <= topY && rect.bottom > topY) {
                const bookId = parseInt(group.getAttribute('data-book-id') || '0')
                if (bookId) emit('visible-book-changed', bookId)
                break
            }
        }
    }

    function handleGroupClick(node: CommentaryTreeNode) {
        if (node.bookId !== undefined && node.lineIndex !== undefined) {
            const targetBook = categoryTreeStore.allBooks.find(book => book.id === node.bookId)
            const targetHasConnections = targetBook ? hasConnections(targetBook) : false

            tabStore.openBookInNewTab(
                node.hebrewName,
                node.bookId,
                targetHasConnections,
                node.lineIndex,
                true
            )
        }
    }

    return {
        currentZoom,
        containerStyles,
        intrinsicSize,
        saveScrollPosition,
        restoreScrollPosition,
        scrollToGroup,
        detectVisibleGroup,
        handleGroupClick
    }
}
