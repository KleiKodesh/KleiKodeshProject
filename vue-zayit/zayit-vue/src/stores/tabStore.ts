import { defineStore } from 'pinia';
import { ref, computed, watch } from 'vue';
import type { Tab, PageType } from '../types/Tab';
import { webviewBridge } from '../services/webviewBridge';
import { handleHebrewBookTabClosed } from '../services/hebrewBooksHandlers';

const STORAGE_KEY = 'tabStore';
const CURRENT_WORKSPACE_KEY = 'zayit_current_workspace';
const DEFAULT_WORKSPACE_ID = 'default';

const PAGE_TITLES: Record<PageType, string> = {
    'homepage': 'דף הבית',
    'openfile': 'פתיחת קובץ',
    'bookview': 'תצוגת ספר',
    'pdfview': 'תצוגת PDF',
    'hebrewbooks-view': 'ספר עברי',
    'search': 'חיפוש',
    'settings': 'הגדרות',
    'hebrewbooks': 'HebrewBooks',
    'kezayit-search': 'חיפוש כזית',
    'workspaces': 'סביבות עבודה'
};

export const useTabStore = defineStore('tabs', () => {
    const tabs = ref<Tab[]>([]);
    const nextId = ref<number>(2);
    const currentWorkspaceId = ref<string>(DEFAULT_WORKSPACE_ID);
    const workspaces = ref<string[]>([DEFAULT_WORKSPACE_ID]);

    const getStorageKey = (workspaceId: string) => `${STORAGE_KEY}_${workspaceId}`;

    const loadFromStorage = async () => {
        try {
            // Load current workspace ID
            const storedWorkspaceId = localStorage.getItem(CURRENT_WORKSPACE_KEY);
            if (storedWorkspaceId) {
                currentWorkspaceId.value = storedWorkspaceId;
            }

            // Load workspaces list
            const workspacesData = localStorage.getItem('zayit_workspaces_list');
            if (workspacesData) {
                workspaces.value = JSON.parse(workspacesData);
            }

            // Load tabs for current workspace
            const storageKey = getStorageKey(currentWorkspaceId.value);
            const stored = localStorage.getItem(storageKey);
            if (stored) {
                const data = JSON.parse(stored);
                tabs.value = data.tabs || [];
                nextId.value = data.nextId || 2;

                // Handle PDF tabs with stored file paths
                for (const tab of tabs.value) {
                    if (tab.pdfState && tab.pdfState.filePath) {
                        // Always recreate virtual URL from file path (virtual URLs don't persist)
                        // Clear any existing URL since it's invalid after restart
                        tab.pdfState.fileUrl = '';

                        try {
                            // Import pdfService dynamically to avoid circular dependency
                            const { pdfService } = await import('../services/pdfService');
                            if (pdfService.isAvailable()) {
                                // Wait for PDF manager to be ready before recreating URLs
                                console.log('[TabStore] Waiting for PDF manager to be ready...');
                                const isReady = await pdfService.checkManagerReady();

                                if (isReady) {
                                    const virtualUrl = await pdfService.recreateVirtualUrl(tab.pdfState.filePath);
                                    if (virtualUrl) {
                                        tab.pdfState.fileUrl = virtualUrl;
                                        console.log('[TabStore] Recreated virtual URL for PDF tab:', tab.title, virtualUrl);
                                    } else {
                                        console.warn('[TabStore] Failed to recreate virtual URL for PDF tab:', tab.title);
                                    }
                                } else {
                                    console.warn('[TabStore] PDF manager not ready, cannot recreate virtual URL for:', tab.title);
                                }
                            }
                        } catch (error) {
                            console.warn('[TabStore] Failed to recreate virtual URL for PDF tab:', tab.title, error);
                        }
                    }
                }

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
            // Save current workspace ID
            localStorage.setItem(CURRENT_WORKSPACE_KEY, currentWorkspaceId.value);

            // Save workspaces list
            localStorage.setItem('zayit_workspaces_list', JSON.stringify(workspaces.value));

            // Only persist content tabs (bookview, pdfview, and hebrewbooks-view)
            const contentTabs = tabs.value.filter(tab =>
                tab.currentPage === 'bookview' ||
                tab.currentPage === 'pdfview' ||
                tab.currentPage === 'hebrewbooks-view'
            );

            // Clean up tabs before saving
            const cleanedTabs = contentTabs.map(tab => {
                let cleanedTab = { ...tab };

                // Clean up PDF state - don't persist virtual URLs
                if (tab.pdfState && tab.pdfState.filePath) {
                    cleanedTab.pdfState = {
                        fileName: tab.pdfState.fileName,
                        fileUrl: '', // Will be recreated on load
                        filePath: tab.pdfState.filePath
                        // Don't save actual fileUrl - it will be recreated on load
                    };
                }

                // Clean up book state - don't persist search state
                if (tab.bookState) {
                    cleanedTab.bookState = {
                        ...tab.bookState,
                        isSearchOpen: undefined // Don't persist search bar state
                    };
                }

                return cleanedTab;
            });

            const storageKey = getStorageKey(currentWorkspaceId.value);
            localStorage.setItem(storageKey, JSON.stringify({
                tabs: cleanedTabs,
                nextId: nextId.value
            }));
        } catch (e) {
            console.error('Failed to save tabs to storage:', e);
        }
    };

    // Initialize store
    (async () => {
        await loadFromStorage();
        if (tabs.value.length === 0) {
            // Create initial tab using centralized homepage logic
            await createDefaultTab();
        }
    })();

    // Centralized function to determine appropriate homepage - always returns homepage
    async function navigateToHomepage(): Promise<{ pageType: PageType, title: string }> {
        return {
            pageType: 'homepage',
            title: PAGE_TITLES['homepage']
        };
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

    // Helper function to handle tab cleanup (Hebrew books cache cleanup)
    const handleTabCleanup = async (tab: Tab) => {
        try {
            // Check if this is a Hebrew book tab that needs cleanup
            if (tab.pdfState?.source === 'hebrewbook' && tab.pdfState.fileName) {
                console.log('[TabStore] Cleaning up Hebrew book tab:', tab.pdfState.fileName);
                handleHebrewBookTabClosed(tab);
            }
        } catch (error) {
            console.error('[TabStore] Error during tab cleanup:', error);
        }
    };

    const addTab = async () => {
        // Check if a homepage tab already exists
        const existingHomepageTab = tabs.value.find(t =>
            t.currentPage === 'homepage'
        );

        if (existingHomepageTab) {
            // Switch to existing homepage tab instead of creating a new one
            setActiveTab(existingHomepageTab.id);
            return;
        }

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
        const tabToClose = tabs.value.find(tab => tab.isActive);

        // Handle Hebrew book cleanup before closing tab
        if (tabToClose) {
            await handleTabCleanup(tabToClose);
        }

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
            // Check if there's already another homepage tab
            const existingHomepageTab = tabs.value.find(t =>
                t.id !== tab.id && t.currentPage === 'homepage'
            );

            if (existingHomepageTab) {
                // Switch to existing homepage tab and close current tab
                setActiveTab(existingHomepageTab.id);
                closeTabById(tab.id);
                return;
            }

            // Reset current tab to appropriate page using centralized homepage logic
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
                // Track if this is the first time opening TOC for this book session
                if (tab.bookState.isFirstTocOpen === undefined) {
                    tab.bookState.isFirstTocOpen = true;
                }
                tab.bookState.isTocOpen = true;
            }
        }
    };

    const closeToc = () => {
        const tab = tabs.value.find(t => t.isActive);
        if (tab?.bookState) {
            tab.bookState.isTocOpen = false;
            // Mark that TOC has been opened before (no longer first time)
            if (tab.bookState.isFirstTocOpen === true) {
                tab.bookState.isFirstTocOpen = false;
            }
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

    const closeTabById = async (id: number) => {
        const tab = tabs.value.find(t => t.id === id);
        if (!tab) return;

        // Handle Hebrew book cleanup before closing tab
        await handleTabCleanup(tab);

        if (tab.isActive) {
            await closeTab();
        } else {
            const currentActiveId = activeTab.value?.id;
            setActiveTab(id);
            await closeTab();
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
        const currentTab = tabs.value.find(t => t.isActive);

        // If current tab is homepage, navigate in same tab
        if (currentTab?.currentPage === 'homepage') {
            currentTab.currentPage = 'settings';
            currentTab.title = PAGE_TITLES.settings;
            return;
        }

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
                fileUrl: blobUrl, // Use virtual URL for current session viewing
                filePath // Store file path for persistence (virtual URL will be recreated on restart)
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

    const toggleAltTocDisplay = () => {
        const tab = tabs.value.find(t => t.isActive);
        if (tab?.bookState) {
            // Initialize to true if undefined, then toggle
            if (tab.bookState.showAltToc === undefined) {
                tab.bookState.showAltToc = true;
            }
            tab.bookState.showAltToc = !tab.bookState.showAltToc;
        }
    };

    const openKezayitOpenFilePage = () => {
        const currentTab = tabs.value.find(t => t.isActive);

        // If current tab is homepage, navigate in same tab
        if (currentTab?.currentPage === 'homepage') {
            currentTab.currentPage = 'openfile';
            currentTab.title = PAGE_TITLES['openfile'];
            return;
        }

        tabs.value.forEach(tab => tab.isActive = false);

        const existingIds = new Set(tabs.value.map(t => t.id));
        let newId = 1;
        while (existingIds.has(newId)) {
            newId++;
        }

        const newTab: Tab = {
            id: newId,
            title: PAGE_TITLES['openfile'],
            isActive: true,
            currentPage: 'openfile'
        };

        tabs.value.push(newTab);
        nextId.value = Math.max(newId + 1, nextId.value);
    };

    const openHebrewBooks = () => {
        const currentTab = tabs.value.find(t => t.isActive);

        // If current tab is homepage, navigate in same tab
        if (currentTab?.currentPage === 'homepage') {
            currentTab.currentPage = 'hebrewbooks';
            currentTab.title = PAGE_TITLES['hebrewbooks'];
            return;
        }

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

    const openBookInNewTab = (bookTitle: string, bookId: number, hasConnections?: boolean, initialLineIndex?: number, shouldHighlight?: boolean, highlightTerms?: string, highlightSnippet?: string) => {
        // Deactivate all current tabs
        tabs.value.forEach(tab => tab.isActive = false)

        // Find the lowest available ID
        const existingIds = new Set(tabs.value.map(t => t.id))
        let newId = 1
        while (existingIds.has(newId)) {
            newId++
        }

        // Create new tab directly with book state to avoid homepage flash
        const newTab: Tab = {
            id: newId,
            title: bookTitle,
            isActive: true,
            currentPage: 'bookview',
            bookState: {
                bookId,
                bookTitle,
                hasConnections,
                initialLineIndex,
                shouldHighlight,
                isLineDisplayInline: false
            },
            searchState: highlightTerms ? {
                searchQuery: '',
                scrollPosition: 0,
                hasSearched: false,
                highlightTerms,
                highlightSnippet
            } : undefined
        }

        tabs.value.push(newTab)
        nextId.value = Math.max(newId + 1, nextId.value)
    };

    const openKezayitSearch = () => {
        const currentTab = tabs.value.find(t => t.isActive);

        // If current tab is homepage, navigate in same tab
        if (currentTab?.currentPage === 'homepage') {
            currentTab.currentPage = 'kezayit-search';
            currentTab.title = PAGE_TITLES['kezayit-search'];
            currentTab.searchState = {
                searchQuery: '',
                scrollPosition: 0,
                hasSearched: false
            };
            return;
        }

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
            currentPage: 'kezayit-search',
            searchState: {
                searchQuery: '',
                scrollPosition: 0,
                hasSearched: false
            }
        };

        tabs.value.push(newTab);
        nextId.value = Math.max(newId + 1, nextId.value);
    };

    // Workspace management functions
    const createWorkspace = (name: string): string => {
        const workspaceId = `workspace_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
        workspaces.value.push(workspaceId);

        // Save workspace name
        localStorage.setItem(`zayit_workspace_name_${workspaceId}`, name);
        localStorage.setItem(`zayit_workspace_created_${workspaceId}`, Date.now().toString());

        return workspaceId;
    };

    const deleteWorkspace = (workspaceId: string) => {
        if (workspaceId === DEFAULT_WORKSPACE_ID) {
            throw new Error('Cannot delete default workspace');
        }

        // Remove from workspaces list
        workspaces.value = workspaces.value.filter(id => id !== workspaceId);

        // Clean up storage
        localStorage.removeItem(getStorageKey(workspaceId));
        localStorage.removeItem(`zayit_workspace_name_${workspaceId}`);
        localStorage.removeItem(`zayit_workspace_created_${workspaceId}`);

        // Switch to default if current workspace is being deleted
        if (currentWorkspaceId.value === workspaceId) {
            switchWorkspace(DEFAULT_WORKSPACE_ID);
        }
    };

    const switchWorkspace = async (workspaceId: string) => {
        if (!workspaces.value.includes(workspaceId)) {
            throw new Error('Workspace not found');
        }

        // Check if workspace manager is currently active
        const isWorkspaceManagerActive = tabs.value.some(tab => tab.isActive && tab.currentPage === 'workspaces');

        // Save current workspace data
        saveToStorage();

        // Switch to new workspace
        currentWorkspaceId.value = workspaceId;

        // Load new workspace data
        const storageKey = getStorageKey(workspaceId);
        const stored = localStorage.getItem(storageKey);

        if (stored) {
            const data = JSON.parse(stored);
            tabs.value = data.tabs || [];
            nextId.value = data.nextId || 2;
        } else {
            // Create default tab for new workspace
            tabs.value = [];
            nextId.value = 2;
            await createDefaultTab();
        }

        // If workspace manager was active, keep it active in the new workspace
        if (isWorkspaceManagerActive) {
            // Deactivate all loaded tabs
            tabs.value.forEach(tab => tab.isActive = false);

            // Add workspace manager tab to new workspace
            const existingIds = new Set(tabs.value.map(t => t.id));
            let newId = 1;
            while (existingIds.has(newId)) {
                newId++;
            }

            const workspaceManagerTab: Tab = {
                id: newId,
                title: PAGE_TITLES.workspaces,
                isActive: true,
                currentPage: 'workspaces'
            };
            tabs.value.push(workspaceManagerTab);
            nextId.value = Math.max(newId + 1, nextId.value);
        } else {
            // Ensure at least one tab is active
            const hasActiveTab = tabs.value.some(tab => tab.isActive);
            if (tabs.value.length > 0 && !hasActiveTab) {
                const firstTab = tabs.value[0];
                if (firstTab) {
                    firstTab.isActive = true;
                }
            }
        }
    };

    const getWorkspaceName = (workspaceId: string): string => {
        if (workspaceId === DEFAULT_WORKSPACE_ID) {
            return 'ברירת מחדל';
        }
        return localStorage.getItem(`zayit_workspace_name_${workspaceId}`) || `סביבת עבודה ${workspaceId.slice(-4)}`;
    };

    const renameWorkspace = (workspaceId: string, newName: string) => {
        localStorage.setItem(`zayit_workspace_name_${workspaceId}`, newName);
    };

    const openWorkspaceManager = () => {
        const currentTab = tabs.value.find(t => t.isActive);

        // If current tab is homepage, navigate in same tab
        if (currentTab?.currentPage === 'homepage') {
            currentTab.currentPage = 'workspaces';
            currentTab.title = PAGE_TITLES.workspaces;
            return;
        }

        // Check if workspace manager tab already exists
        const existingWorkspaceTab = tabs.value.find(t => t.currentPage === 'workspaces');
        if (existingWorkspaceTab) {
            // Switch to existing workspace manager tab
            setActiveTab(existingWorkspaceTab.id);
            return;
        }

        // Create new workspace manager tab
        tabs.value.forEach(tab => tab.isActive = false);

        const existingIds = new Set(tabs.value.map(t => t.id));
        let newId = 1;
        while (existingIds.has(newId)) {
            newId++;
        }

        const newTab: Tab = {
            id: newId,
            title: PAGE_TITLES.workspaces,
            isActive: true,
            currentPage: 'workspaces'
        };
        tabs.value.push(newTab);
        nextId.value = Math.max(newId + 1, nextId.value);
    };

    return {
        tabs,
        activeTab,
        currentWorkspaceId,
        workspaces,
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
        openBookInNewTab,
        toggleSplitPane,
        toggleDiacritics,
        toggleLineDisplay,
        openSettings,
        openPdf,
        openPdfViewer,
        openPdfWithFile,
        openPdfWithFilePath,
        openPdfWithFilePathAndBlobUrl,
        toggleBookSearch,
        toggleAltTocDisplay,
        openKezayitOpenFilePage,
        openHebrewBooks,
        openKezayitSearch,
        createWorkspace,
        deleteWorkspace,
        switchWorkspace,
        getWorkspaceName,
        renameWorkspace,
        openWorkspaceManager
    };
});
