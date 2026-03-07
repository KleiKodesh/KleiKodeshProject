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

        const groups = scrollContainer.value.querySelectorAll('[data-book-id]')
        for (let i = 0; i < groups.length; i++) {
            const rect = (groups[i] as HTMLElement).getBoundingClientRect()
            if (rect.top <= topY && rect.bottom > topY) {
                tabStore.activeTab.bookState.commentaryScrollElementIndex = i
                tabStore.activeTab.bookState.commentaryScrollOffset = topY - rect.top
                return
            }
        }
    }

    async function restoreScrollPosition(isFirstInit: boolean) {
        if (!scrollContainer.value || isFirstInit) return

        const elementIndex = tabStore.activeTab?.bookState?.commentaryScrollElementIndex
        const offset = tabStore.activeTab?.bookState?.commentaryScrollOffset
        if (elementIndex === undefined) return

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

        if (offset !== undefined && offset !== 0) {
            for (let i = 0; i < 5; i++) {
                await new Promise(resolve => requestAnimationFrame(resolve))
                const containerRect = scrollContainer.value.getBoundingClientRect()
                const elementRect = targetElement.getBoundingClientRect()
                const adjustment = offset - (containerRect.top + 50 - elementRect.top)
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
