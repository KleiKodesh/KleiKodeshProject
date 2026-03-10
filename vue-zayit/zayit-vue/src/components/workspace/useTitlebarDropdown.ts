import { ref, computed, onMounted, onUnmounted } from 'vue';
import { onClickOutside } from '@vueuse/core';
import { useWorkspace } from '@/components/workspace/useWorkspace';
import { useBookViewer } from '@/components/book/useBookViewer';
import { useHome } from '@/components/home/useHome';
import { pdfService } from '@/data/services/pdfService';

export function useTitlebarDropdown() {
    const { activeTab } = useWorkspace();
    const { toggleLineDisplay, toggleAltTocDisplay } = useBookViewer();
    const {
        openSettings,
        openWorkspaceManager,
        openZayitOpenFilePage,
        openHebrewBooks,
        openKezayitSearch,
        openPdfWithFile,
        openPdfWithFilePathAndBlobUrl
    } = useHome();

    const isOpen = ref(false);
    const dropdownContainer = ref<HTMLElement>();

    // Check if WebView is available for popout functionality
    const isWebViewAvailable = computed(() => {
        return (window as any).chrome?.webview?.postMessage !== undefined;
    });

    // Check if current page is homepage (not open file page)
    const isHomepage = computed(() => {
        return activeTab.value?.currentPage === 'homepage';
    });

    // Check if current page is bookview
    const isBookViewPage = computed(() => {
        return activeTab.value?.currentPage === 'bookview';
    });

    // Line display state
    const isLineDisplayInline = computed(() => {
        return activeTab.value?.bookState?.isLineDisplayInline || false;
    });

    // Alt TOC visibility state
    const isAltTocVisible = computed(() => {
        const bookState = activeTab.value?.bookState;
        if (!bookState) return true;
        return bookState.showAltToc !== false;
    });

    // Toolbar visibility state
    const isToolbarVisible = computed(() => {
        const bookState = activeTab.value?.bookState;
        if (!bookState) return true;
        return bookState.showToolbar !== false;
    });

    const toggleDropdown = () => {
        isOpen.value = !isOpen.value;
    };

    const closeDropdown = () => {
        isOpen.value = false;
    };

    const handleSettingsClick = () => {
        openSettings();
        closeDropdown();
    };

    const handleWorkspaceManagerClick = () => {
        openWorkspaceManager();
        closeDropdown();
    };

    const handleOpenBookClick = () => {
        openZayitOpenFilePage();
        closeDropdown();
    };

    const handleHebrewBooksClick = () => {
        openHebrewBooks();
        closeDropdown();
    };

    const handleSearchPageClick = () => {
        openKezayitSearch();
        closeDropdown();
    };

    const handleLineDisplayClick = () => {
        toggleLineDisplay();
    };

    const handleAltTocToggleClick = () => {
        toggleAltTocDisplay();
        closeDropdown();
    };

    const handleOpenPdfClick = async () => {
        try {
            if (pdfService.isAvailable()) {
                const result = await pdfService.showFilePicker();

                if (result.fileName && result.dataUrl) {
                    if (result.originalPath) {
                        openPdfWithFilePathAndBlobUrl(result.fileName, result.originalPath, result.dataUrl);
                    } else {
                        openPdfWithFile(result.fileName, result.dataUrl);
                    }
                }
            } else {
                // Fallback to browser file picker if not in WebView2
                const input = document.createElement('input');
                input.type = 'file';
                input.accept = '.pdf';
                input.onchange = (e: Event) => {
                    const target = e.target as HTMLInputElement;
                    const file = target.files?.[0];
                    if (file && file.type === 'application/pdf') {
                        const fileUrl = URL.createObjectURL(file);
                        openPdfWithFile(file.name, fileUrl);
                    }
                };
                input.click();
            }
        } catch (error) {
            // Fallback to browser file picker on error
            const input = document.createElement('input');
            input.type = 'file';
            input.accept = '.pdf';
            input.onchange = (e: Event) => {
                const target = e.target as HTMLInputElement;
                const file = target.files?.[0];
                if (file && file.type === 'application/pdf') {
                    const fileUrl = URL.createObjectURL(file);
                    openPdfWithFile(file.name, fileUrl);
                }
            };
            input.click();
        }

        closeDropdown();
    };

    const handlePopoutClick = async () => {
        if (isWebViewAvailable.value) {
            try {
                const { webviewBridge } = await import('@/data/services/webviewBridge');
                await webviewBridge.call('TogglePopOut');
            } catch (error) {
                console.error('[TitlebarDropdownMenu] Failed to toggle popout:', error);
            }
        }
        closeDropdown();
    };

    const handleWindowBlur = () => {
        if (isOpen.value) {
            closeDropdown();
        }
    };

    const handleVisibilityChange = () => {
        if (document.hidden && isOpen.value) {
            closeDropdown();
        }
    };

    onMounted(() => {
        window.addEventListener('blur', handleWindowBlur);
        document.addEventListener('visibilitychange', handleVisibilityChange);
    });

    onUnmounted(() => {
        window.removeEventListener('blur', handleWindowBlur);
        document.removeEventListener('visibilitychange', handleVisibilityChange);
    });

    // Setup click outside handler
    const setupClickOutside = (container: HTMLElement) => {
        dropdownContainer.value = container;
        onClickOutside(dropdownContainer, () => {
            if (isOpen.value) {
                closeDropdown();
            }
        });
    };

    return {
        isOpen,
        isWebViewAvailable,
        isHomepage,
        isBookViewPage,
        isLineDisplayInline,
        isAltTocVisible,
        isToolbarVisible,
        toggleDropdown,
        closeDropdown,
        handleSettingsClick,
        handleWorkspaceManagerClick,
        handleOpenBookClick,
        handleHebrewBooksClick,
        handleSearchPageClick,
        handleLineDisplayClick,
        handleAltTocToggleClick,
        handleOpenPdfClick,
        handlePopoutClick,
        setupClickOutside
    };
}
