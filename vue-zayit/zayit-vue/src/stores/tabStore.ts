import { defineStore } from 'pinia';
import { ref, computed, watch } from 'vue';
import type { Tab, PageType } from '../types/Tab';
import { checkConnectivity } from '../utils/connectivity';
import { useSettingsStore } from './settingsStore';

const STORAGE_KEY = 'tabStore';

const PAGE_TITLES: Record<PageType, string> = {
    'homepage': 'דף הבית',
    'kezayit-landing': 'איתור',
    'bookview': 'תצוגת ספר',
    'pdfview': 'תצוגת PDF',
    'hebrewbooks-view': 'ספר עברי',
    'search': 'חיפוש',
    'settings': 'הגדרות',
    'about': 'אודות',
    'hebrewbooks': 'HebrewBooks',
    'kezayit-search': 'חיפוש כזית'
};

export const useTabStore = defineStore('tabs', () => {
    const tabs = ref<Tab[]>([]);
    const nextId = ref<number>(2);

    const loadFromStorage = () => {
        try {
            const stored = localStorage.getItem(STORAGE_KEY);
            if (stored) {
                const data = JSON.parse(stored);
                tabs.value = data.tabs || [];
                nextId.value = data.nextId || 2;

                // Migrate old 'landing' page type to 'kezayit-landing'
                tabs.value.forEach(tab => {
                    if ((tab.currentPage as string) === 'landing') {
                        tab.currentPage = 'kezayit-landing';
                        tab.title = PAGE_TITLES['kezayit-landing'];
                    }
                });

                // Clear blob URLs on page reload (they become invalid)
                tabs.value.forEach(tab => {
                    if (tab.pdfState?.fileUrl?.startsWith('blob:')) {
                        tab.pdfState.fileUrl = '';
                    }
                });

                // Ensure at least one tab is active
                const hasActiveTab = tabs.value.some(tab => tab.isActive);
                if (tabs.value.length > 0 && !hasActiveTab) {
                    const firstTab = tabs.value[0];
                    if (firstTab) {
                        firstTab.isActive = true;
                    }
                }
            }
        } catch (e) {
            console.error('Failed to load tabs from storage:', e);
        }
    };



    const saveToStorage = () => {
        try {
            // Only persist content tabs (bookview, pdfview, and hebrewbooks-view)
            const contentTabs = tabs.value.filter(tab =>
                tab.currentPage === 'bookview' ||
                tab.currentPage === 'pdfview' ||
                tab.currentPage === 'hebrewbooks-view'
            );

            localStorage.setItem(STORAGE_KEY, JSON.stringify({
                tabs: contentTabs,
                nextId: nextId.value
            }));
        } catch (e) {
            console.error('Failed to save tabs to storage:', e);
        }
    };

    loadFromStorage();
    if (tabs.value.length === 0) {
        // Create initial tab using centralized homepage logic
        createDefaultTab();
    }

    // Centralized function to determine appropriate homepage based on user preference and connectivity
    async function navigateToHomepage(): Promise<{ pageType: PageType, title: string }> {
        const settingsStore = useSettingsStore();

        // Check connectivity first
        const isOnline = await checkConnectivity();

        // If offline, always use kezayit-landing regardless of user preference
        if (!isOnline) {
            return {
                pageType: 'kezayit-landing',
                title: PAGE_TITLES['kezayit-landing']
            };
        }

        // If online, use user preference
        if (settingsStore.useOfflineHomepage) {
            // User prefers kezayit-landing page even when online
            return {
                pageType: 'kezayit-landing',
                title: PAGE_TITLES['kezayit-landing']
            };
        } else {
            // User prefers regular homepage when online
            return {
                pageType: 'homepage',
                title: PAGE_TITLES['homepage']
            };
        }
    }

    // Helper function to create default tab based on connectivity
    async function createDefaultTab() {
        const { pageType, title } = await navigateToHomepage();

        tabs.value.push({
            id: 1,
            title,
            isActive: true,
            currentPage: pageType
        });
    }

    watch(tabs, saveToStorage, { deep: true });
    watch(nextId, saveToStorage);

    const activeTab = computed(() => tabs.value.find(tab => tab.isActive));

    const addTab = async () => {
        tabs.value.forEach(tab => tab.isActive = false);

        // Find the lowest available ID
        const existingIds = new Set(tabs.value.map(t => t.id));
        let newId = 1;
        while (existingIds.has(newId)) {
            newId++;
        }

        // Create tab using centralized homepage logic
        const { pageType, title } = await navigateToHomepage();

        const newTab: Tab = {
            id: newId,
            title,
            isActive: true,
            currentPage: pageType
        };
        tabs.value.push(newTab);

        // Update nextId to be at least one more than the highest ID
        nextId.value = Math.max(newId + 1, nextId.value);
    };

    const closeTab = async () => {
        const currentIndex = tabs.value.findIndex(tab => tab.isActive);
        tabs.value = tabs.value.filter(tab => !tab.isActive);

        if (tabs.value.length === 0) {
            await addTab();
        } else {
            const newIndex = Math.min(currentIndex, tabs.value.length - 1);
            const newTab = tabs.value[newIndex];
            if (newTab) {
                newTab.isActive = true;
            }
        }
    };

    const setActiveTab = (id: number) => {
        tabs.value.forEach(tab => {
            tab.isActive = tab.id === id;
        });
    };

    const resetTab = async () => {
        const tab = tabs.value.find(t => t.isActive);
        if (tab) {
            // Reset to appropriate page using centralized homepage logic
            const { pageType, title } = await navigateToHomepage();

            tab.currentPage = pageType;
            tab.title = title;
            delete tab.bookState;
        }
    };

    const setPage = (pageType: PageType) => {
        const tab = tabs.value.find(t => t.isActive);
        if (tab) {
            tab.currentPage = pageType;
            tab.title = PAGE_TITLES[pageType];
        }
    };

    const openBookToc = (bookTitle: string, bookId: number, hasConnections?: boolean) => {
        const tab = tabs.value.find(t => t.isActive);
        if (tab) {
            // Open book if not already open
            if (tab.currentPage !== 'bookview' || tab.bookState?.bookId !== bookId) {
                openBook(bookTitle, bookId, hasConnections);
            }
            // Open TOC overlay
            if (tab.bookState) {
                tab.bookState.isTocOpen = true;
            }
        }
    };

    const closeToc = () => {
        const tab = tabs.value.find(t => t.isActive);
        if (tab?.bookState) {
            tab.bookState.isTocOpen = false;
        }
    };

    const openBook = (bookTitle: string, bookId: number, hasConnections?: boolean, initialLineIndex?: number) => {
        const tab = tabs.value.find(t => t.isActive);
        if (tab) {
            tab.currentPage = 'bookview';
            tab.title = bookTitle;

            // Create or update bookState
            if (!tab.bookState || tab.bookState.bookId !== bookId) {
                // New book - create fresh bookState
                tab.bookState = {
                    bookId,
                    bookTitle,
                    hasConnections,
                    initialLineIndex,
                    isLineDisplayInline: false // Default to block display (justified text)
                };
            } else {
                // Same book - update initialLineIndex if provided, otherwise clear it
                if (initialLineIndex !== undefined) {
                    tab.bookState.initialLineIndex = initialLineIndex;
                } else {
                    delete tab.bookState.initialLineIndex;
                }
            }
        }
    };

    const closeTabById = (id: number) => {
        const tab = tabs.value.find(t => t.id === id);
        if (tab?.isActive) {
            closeTab();
        } else {
            const currentActiveId = activeTab.value?.id;
            setActiveTab(id);
            closeTab();
            if (currentActiveId && tabs.value.find(t => t.id === currentActiveId)) {
                setActiveTab(currentActiveId);
            }
        }
    };

    const toggleSplitPane = () => {
        const tab = tabs.value.find(t => t.isActive);
        if (tab?.bookState) {
            tab.bookState.showBottomPane = !tab.bookState.showBottomPane;
        }
    };

    const toggleDiacritics = () => {
        const tab = tabs.value.find(t => t.isActive);
        if (tab?.bookState) {
            // Initialize diacritics state if not set
            if (tab.bookState.diacriticsState === undefined) {
                tab.bookState.diacriticsState = 0;
            }
            // Cycle through states: 0 -> 1 -> 2 -> 0
            tab.bookState.diacriticsState = (tab.bookState.diacriticsState + 1) % 3;
        }
    };

    const toggleLineDisplay = () => {
        const tab = tabs.value.find(t => t.isActive);
        if (tab?.bookState) {
            // Initialize to false if undefined, then toggle
            if (tab.bookState.isLineDisplayInline === undefined) {
                tab.bookState.isLineDisplayInline = false;
            }
            tab.bookState.isLineDisplayInline = !tab.bookState.isLineDisplayInline;
        }
    };

    const openSettings = () => {
        // Check if settings tab already exists
        const existingSettingsTab = tabs.value.find(t => t.currentPage === 'settings');
        if (existingSettingsTab) {
            // Switch to existing settings tab
            setActiveTab(existingSettingsTab.id);
            return;
        }

        // Create new settings tab
        tabs.value.forEach(tab => tab.isActive = false);

        const existingIds = new Set(tabs.value.map(t => t.id));
        let newId = 1;
        while (existingIds.has(newId)) {
            newId++;
        }

        const newTab: Tab = {
            id: newId,
            title: PAGE_TITLES.settings,
            isActive: true,
            currentPage: 'settings'
        };
        tabs.value.push(newTab);
        nextId.value = Math.max(newId + 1, nextId.value);
    };

    const openAbout = () => {
        // Check if about tab already exists
        const existingAboutTab = tabs.value.find(t => t.currentPage === 'about');
        if (existingAboutTab) {
            // Switch to existing about tab
            setActiveTab(existingAboutTab.id);
            return;
        }

        // Create new about tab
        tabs.value.forEach(tab => tab.isActive = false);

        const existingIds = new Set(tabs.value.map(t => t.id));
        let newId = 1;
        while (existingIds.has(newId)) {
            newId++;
        }

        const newTab: Tab = {
            id: newId,
            title: PAGE_TITLES.about,
            isActive: true,
            currentPage: 'about'
        };
        tabs.value.push(newTab);
        nextId.value = Math.max(newId + 1, nextId.value);
    };

    const openPdf = (fileName: string, fileUrl: string) => {
        // Create new PDF tab
        tabs.value.forEach(tab => tab.isActive = false);

        const existingIds = new Set(tabs.value.map(t => t.id));
        let newId = 1;
        while (existingIds.has(newId)) {
            newId++;
        }

        const newTab: Tab = {
            id: newId,
            title: fileName,
            isActive: true,
            currentPage: 'pdfview',
            pdfState: {
                fileName,
                fileUrl
            }
        };
        tabs.value.push(newTab);
        nextId.value = Math.max(newId + 1, nextId.value);
    };

    const openPdfViewer = () => {
        // Check if PDF viewer tab already exists
        const existingPdfTab = tabs.value.find(t => t.currentPage === 'pdfview');
        if (existingPdfTab) {
            // Switch to existing PDF viewer tab
            setActiveTab(existingPdfTab.id);
            return;
        }

        // Create new PDF viewer tab
        tabs.value.forEach(tab => tab.isActive = false);

        const existingIds = new Set(tabs.value.map(t => t.id));
        let newId = 1;
        while (existingIds.has(newId)) {
            newId++;
        }

        const newTab: Tab = {
            id: newId,
            title: PAGE_TITLES.pdfview,
            isActive: true,
            currentPage: 'pdfview'
        };
        tabs.value.push(newTab);
        nextId.value = Math.max(newId + 1, nextId.value);
    };

    const openPdfWithFile = (fileName: string, fileUrl: string) => {
        // Create new PDF tab with file already selected
        tabs.value.forEach(tab => tab.isActive = false);

        const existingIds = new Set(tabs.value.map(t => t.id));
        let newId = 1;
        while (existingIds.has(newId)) {
            newId++;
        }

        // Ensure filename has .pdf extension
        let displayName = fileName;
        if (!displayName.toLowerCase().endsWith('.pdf')) {
            displayName += '.pdf';
        }

        const newTab: Tab = {
            id: newId,
            title: displayName,
            isActive: true,
            currentPage: 'pdfview',
            pdfState: {
                fileName: displayName,
                fileUrl
            }
        };

        tabs.value.push(newTab);
        nextId.value = Math.max(newId + 1, nextId.value);
    };

    const openPdfWithFilePath = (fileName: string, filePath: string) => {
        // Create new PDF tab with file path for persistence
        tabs.value.forEach(tab => tab.isActive = false);

        const existingIds = new Set(tabs.value.map(t => t.id));
        let newId = 1;
        while (existingIds.has(newId)) {
            newId++;
        }

        // Ensure filename has .pdf extension
        let displayName = fileName;
        if (!displayName.toLowerCase().endsWith('.pdf')) {
            displayName += '.pdf';
        }

        const newTab: Tab = {
            id: newId,
            title: displayName,
            isActive: true,
            currentPage: 'pdfview',
            pdfState: {
                fileName: displayName,
                fileUrl: '', // Not needed when using file path
                filePath
            }
        };

        tabs.value.push(newTab);
        nextId.value = Math.max(newId + 1, nextId.value);
    };

    const openPdfWithFilePathAndBlobUrl = (fileName: string, filePath: string, blobUrl: string) => {
        // Create new PDF tab with both file path (persistence) and blob URL (viewing)
        tabs.value.forEach(tab => tab.isActive = false);

        const existingIds = new Set(tabs.value.map(t => t.id));
        let newId = 1;
        while (existingIds.has(newId)) {
            newId++;
        }

        // Ensure filename has .pdf extension
        let displayName = fileName;
        if (!displayName.toLowerCase().endsWith('.pdf')) {
            displayName += '.pdf';
        }

        const newTab: Tab = {
            id: newId,
            title: displayName,
            isActive: true,
            currentPage: 'pdfview',
            pdfState: {
                fileName: displayName,
                fileUrl: blobUrl, // Use blob URL for PDF.js viewing
                filePath // Store file path for persistence
            }
        };

        tabs.value.push(newTab);
        nextId.value = Math.max(newId + 1, nextId.value);
    };



    const closeAllTabs = async () => {
        // Clear all tabs and create a new default tab using centralized homepage logic
        const { pageType, title } = await navigateToHomepage();

        tabs.value = [{
            id: 1,
            title,
            isActive: true,
            currentPage: pageType
        }];
        nextId.value = 2;
    };

    const toggleBookSearch = (isOpen: boolean) => {
        const tab = tabs.value.find(t => t.isActive);
        if (tab?.bookState) {
            tab.bookState.isSearchOpen = isOpen;
        }
    };

    const openKezayitLanding = () => {
        tabs.value.forEach(tab => tab.isActive = false);

        const existingIds = new Set(tabs.value.map(t => t.id));
        let newId = 1;
        while (existingIds.has(newId)) {
            newId++;
        }

        const newTab: Tab = {
            id: newId,
            title: PAGE_TITLES['kezayit-landing'],
            isActive: true,
            currentPage: 'kezayit-landing'
        };

        tabs.value.push(newTab);
        nextId.value = Math.max(newId + 1, nextId.value);
    };

    const openHebrewBooks = () => {
        tabs.value.forEach(tab => tab.isActive = false);

        const existingIds = new Set(tabs.value.map(t => t.id));
        let newId = 1;
        while (existingIds.has(newId)) {
            newId++;
        }

        const newTab: Tab = {
            id: newId,
            title: PAGE_TITLES['hebrewbooks'],
            isActive: true,
            currentPage: 'hebrewbooks'
        };

        tabs.value.push(newTab);
        nextId.value = Math.max(newId + 1, nextId.value);
    };

    const openKezayitSearch = () => {
        tabs.value.forEach(tab => tab.isActive = false);

        const existingIds = new Set(tabs.value.map(t => t.id));
        let newId = 1;
        while (existingIds.has(newId)) {
            newId++;
        }

        const newTab: Tab = {
            id: newId,
            title: PAGE_TITLES['kezayit-search'],
            isActive: true,
            currentPage: 'kezayit-search'
        };

        tabs.value.push(newTab);
        nextId.value = Math.max(newId + 1, nextId.value);
    };

    return {
        tabs,
        activeTab,
        addTab,
        closeTab,
        closeTabById,
        closeAllTabs,
        setActiveTab,
        resetTab,
        setPage,
        openBookToc,
        closeToc,
        openBook,
        toggleSplitPane,
        toggleDiacritics,
        toggleLineDisplay,
        openSettings,
        openAbout,
        openPdf,
        openPdfViewer,
        openPdfWithFile,
        openPdfWithFilePath,
        openPdfWithFilePathAndBlobUrl,
        toggleBookSearch,
        openKezayitLanding,
        openHebrewBooks,
        openKezayitSearch
    };
});
