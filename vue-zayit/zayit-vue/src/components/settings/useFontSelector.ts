/**
 * Font Selection Composable
 * Handles font detection and dropdown management for settings
 */

import { ref, watch } from 'vue'
import { hebrewFonts } from '@/utils/hebrewFonts'
import { useDropdownPosition } from '@/components/shared/useDropdownPosition'

export function useFontSelector() {
    const availableFonts = ref<string[]>([])
    const isHeaderDropdownOpen = ref(false)
    const isTextDropdownOpen = ref(false)
    const headerDropdownRef = ref<HTMLElement>()
    const textDropdownRef = ref<HTMLElement>()

    // Dropdown positioning
    const { dropdownStyles: headerDropdownStyles, updatePosition: updateHeaderPosition } =
        useDropdownPosition(headerDropdownRef, isHeaderDropdownOpen)
    const { dropdownStyles: textDropdownStyles, updatePosition: updateTextPosition } =
        useDropdownPosition(textDropdownRef, isTextDropdownOpen)

    // Watch dropdown states to update positions
    watch(isHeaderDropdownOpen, () => {
        updateHeaderPosition()
    })

    watch(isTextDropdownOpen, () => {
        updateTextPosition()
    })

    /**
     * Check if a font is available in the system
     */
    const isFontAvailable = (fontName: string): boolean => {
        const canvas = document.createElement('canvas')
        const ctx = canvas.getContext('2d')
        if (!ctx) return false

        const str = 'אבגדהוזחטיכלמנסעפצקרשת'

        return ['monospace', 'sans-serif', 'serif'].some(base => {
            ctx.font = `72px ${base}`
            const w = ctx.measureText(str).width
            ctx.font = `72px '${fontName}', ${base}`
            return w !== ctx.measureText(str).width
        })
    }

    /**
     * Detect available Hebrew fonts
     */
    const detectFonts = () => {
        const detected = hebrewFonts.filter(isFontAvailable)
        availableFonts.value = detected.length > 0 ? detected : hebrewFonts
    }

    /**
     * Toggle header font dropdown
     */
    const toggleHeaderDropdown = () => {
        isHeaderDropdownOpen.value = !isHeaderDropdownOpen.value
        isTextDropdownOpen.value = false
    }

    /**
     * Toggle text font dropdown
     */
    const toggleTextDropdown = () => {
        isTextDropdownOpen.value = !isTextDropdownOpen.value
        isHeaderDropdownOpen.value = false
    }

    /**
     * Get display name from font value
     */
    const getDisplayName = (fontValue: string): string => {
        return fontValue.match(/'([^']+)'/)?.[1] ?? fontValue
    }

    return {
        availableFonts,
        isHeaderDropdownOpen,
        isTextDropdownOpen,
        headerDropdownRef,
        textDropdownRef,
        headerDropdownStyles,
        textDropdownStyles,
        detectFonts,
        toggleHeaderDropdown,
        toggleTextDropdown,
        getDisplayName
    }
}
