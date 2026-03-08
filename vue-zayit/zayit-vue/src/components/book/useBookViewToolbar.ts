/**
 * Line View Toolbar Actions Composable
 * Handles toolbar button actions and computed states
 */

import { computed } from 'vue'
import { useTabStore } from '@/data/stores/tabStore'
import DiacriticsFullIcon from '@/components/icons/DiacriticsFullIcon.vue'
import DiacriticsNikkudOnlyIcon from '@/components/icons/DiacriticsNikkudOnlyIcon.vue'
import DiacriticsNoneIcon from '@/components/icons/DiacriticsNoneIcon.vue'

export function useBookViewToolbarActions() {
    const tabStore = useTabStore()

    const isVisible = computed(() => {
        return tabStore.activeTab?.bookState?.showToolbar !== false
    })

    const currentZoom = computed(() => {
        return tabStore.activeTab?.bookState?.zoom || 100
    })

    const hasConnections = computed(() => {
        const bookState = tabStore.activeTab?.bookState
        if (!bookState) return false
        return bookState.hasConnections || false
    })

    const isSplitPaneOpen = computed(() => {
        const bookState = tabStore.activeTab?.bookState
        if (!bookState) return false
        return bookState.showBottomPane || false
    })

    const isAltTocVisible = computed(() => {
        const bookState = tabStore.activeTab?.bookState
        if (!bookState) return true
        return bookState.showAltToc !== false
    })

    const diacriticsState = computed(() => tabStore.currentDiacriticsState)

    const diacriticsStateClass = computed(() => {
        if (diacriticsState.value === 1) return 'state-1'
        if (diacriticsState.value === 2) return 'state-2'
        return ''
    })

    const diacriticsIcon = computed(() => {
        if (diacriticsState.value === 1) return DiacriticsNikkudOnlyIcon
        if (diacriticsState.value === 2) return DiacriticsNoneIcon
        return DiacriticsFullIcon
    })

    const diacriticsTooltip = computed(() => {
        if (diacriticsState.value === 0) return 'הסר טעמים'
        if (diacriticsState.value === 1) return 'הסר גם ניקוד'
        return 'שחזר טעמים וניקוד'
    })

    const handleZoomIn = () => {
        tabStore.zoomIn()
    }

    const handleZoomOut = () => {
        tabStore.zoomOut()
    }

    const handleSearchClick = () => {
        tabStore.toggleBookSearch(true)
    }

    const handleToggleSplitPane = () => {
        tabStore.toggleSplitPane()
    }

    const handleAltTocToggle = () => {
        tabStore.toggleAltTocDisplay()
    }

    const handleDiacriticsClick = () => {
        tabStore.toggleDiacritics()
    }

    return {
        isVisible,
        currentZoom,
        hasConnections,
        isSplitPaneOpen,
        isAltTocVisible,
        diacriticsState,
        diacriticsStateClass,
        diacriticsIcon,
        diacriticsTooltip,
        handleZoomIn,
        handleZoomOut,
        handleSearchClick,
        handleToggleSplitPane,
        handleAltTocToggle,
        handleDiacriticsClick
    }
}
