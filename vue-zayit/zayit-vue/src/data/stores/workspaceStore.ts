import { defineStore } from 'pinia';
import { ref, computed, watch } from 'vue';
import type { Tab, PageType } from '@/data/types/Tab';

export interface WorkspaceData {
    tabs: Tab[];
    nextId: number;
}

export interface Workspace {
    id: string;
    name: string;
    createdAt: number;
    lastAccessedAt: number;
    data: WorkspaceData;
}

const WORKSPACES_KEY = 'zayit_workspaces';
const CURRENT_WORKSPACE_KEY = 'zayit_currentWorkspaceId';
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

export const useWorkspaceStore = defineStore('workspace', () => {
    const workspaces = ref<Workspace[]>([]);
    const currentWorkspaceId = ref<string>(DEFAULT_WORKSPACE_ID);

    const currentWorkspace = computed(() =>
        workspaces.value.find(w => w.id === currentWorkspaceId.value)
    );

    // Use computed with proper reactivity tracking
    const tabs = computed(() => {
        const workspace = currentWorkspace.value;
        if (!workspace) return [];
        // Return a shallow copy to ensure reactivity
        return workspace.data.tabs;
    });

    const activeTab = computed(() => tabs.value.find(tab => tab.isActive));

    const loadFromStorage = () => {
        try {
            console.log('[WorkspaceStore] Loading...');
            const stored = localStorage.getItem(WORKSPACES_KEY);

            if (stored) {
                console.log('[WorkspaceStore] Found stored data');
                workspaces.value = JSON.parse(stored) as Workspace[];
            } else {
                console.log('[WorkspaceStore] Checking for old tabStore...');
                const oldTabStore = localStorage.getItem('tabStore');

                if (oldTabStore) {
                    console.log('[WorkspaceStore] Migrating from old tabStore');
                    const oldData = JSON.parse(oldTabStore);

                    // Migrate tabs, preserving all state
                    const migratedTabs = (oldData.tabs || []).map((tab: any) => tab);

                    workspaces.value = [{
                        id: DEFAULT_WORKSPACE_ID,
                        name: 'ברירת מחדל',
                        createdAt: Date.now(),
                        lastAccessedAt: Date.now(),
                        data: {
                            tabs: migratedTabs,
                            nextId: oldData.nextId || 2
                        }
                    }];

                    console.log('[WorkspaceStore] Migrated', migratedTabs.length, 'tabs');
                } else {
                    console.log('[WorkspaceStore] Creating default workspace');
                    workspaces.value = [{
                        id: DEFAULT_WORKSPACE_ID,
                        name: 'ברירת מחדל',
                        createdAt: Date.now(),
                        lastAccessedAt: Date.now(),
                        data: {
                            tabs: [{
                                id: 1,
                                title: PAGE_TITLES['openfile'],
                                isActive: true,
                                currentPage: 'openfile'
                            }],
                            nextId: 2
                        }
                    }];
                }
            }

            const storedCurrentId = localStorage.getItem(CURRENT_WORKSPACE_KEY);
            if (storedCurrentId && workspaces.value.some(w => w.id === storedCurrentId)) {
                currentWorkspaceId.value = storedCurrentId;
            } else {
                currentWorkspaceId.value = DEFAULT_WORKSPACE_ID;
            }

            // Ensure active tab
            const current = currentWorkspace.value;
            if (current && current.data.tabs.length > 0) {
                const hasActiveTab = current.data.tabs.some(tab => tab.isActive);
                if (!hasActiveTab && current.data.tabs[0]) {
                    current.data.tabs[0].isActive = true;
                }
            }

            console.log('[WorkspaceStore] Loaded:', workspaces.value.length, 'workspaces');
            console.log('[WorkspaceStore] Current workspace:', currentWorkspaceId.value);
            console.log('[WorkspaceStore] Tabs in current workspace:', tabs.value.length);

            // Log tab details for debugging
            if (tabs.value.length > 0) {
                console.log('[WorkspaceStore] Tab details:', tabs.value.map(t => ({
                    id: t.id,
                    title: t.title,
                    page: t.currentPage,
                    hasBookState: !!t.bookState,
                    bookState: t.bookState,
                    hasPdfState: !!t.pdfState
                })));
            }
        } catch (e) {
            console.error('[WorkspaceStore] Load error:', e);
            // Create default workspace on error
            workspaces.value = [{
                id: DEFAULT_WORKSPACE_ID,
                name: 'ברירת מחדל',
                createdAt: Date.now(),
                lastAccessedAt: Date.now(),
                data: {
                    tabs: [{
                        id: 1,
                        title: PAGE_TITLES['openfile'],
                        isActive: true,
                        currentPage: 'openfile'
                    }],
                    nextId: 2
                }
            }];
            currentWorkspaceId.value = DEFAULT_WORKSPACE_ID;
        }
    };

    const saveToStorage = () => {
        try {
            // Clean workspaces before saving - only persist content tabs
            const cleanedWorkspaces = workspaces.value.map(workspace => {
                const contentTabs = workspace.data.tabs.filter(tab =>
                    tab.currentPage === 'bookview' ||
                    tab.currentPage === 'pdfview' ||
                    tab.currentPage === 'hebrewbooks-view'
                );

                const cleanedTabs = contentTabs.map(tab => {
                    let cleanedTab = { ...tab };

                    // Clean up PDF state
                    if (tab.pdfState) {
                        // Only clear blob: and virtual: URLs (they don't persist across sessions)
                        // Keep regular URLs (like http:// or file://)
                        const shouldClearUrl = tab.pdfState.fileUrl &&
                            (tab.pdfState.fileUrl.startsWith('blob:') ||
                                tab.pdfState.fileUrl.startsWith('virtual:'));

                        cleanedTab.pdfState = {
                            fileName: tab.pdfState.fileName,
                            fileUrl: shouldClearUrl ? '' : tab.pdfState.fileUrl,
                            filePath: tab.pdfState.filePath,
                            source: tab.pdfState.source,
                            bookId: tab.pdfState.bookId,
                            bookTitle: tab.pdfState.bookTitle
                        };
                    }

                    // Clean up book state - don't persist search state
                    if (tab.bookState) {
                        cleanedTab.bookState = {
                            ...tab.bookState,
                            isSearchOpen: undefined
                        };
                    }

                    return cleanedTab;
                });

                return {
                    ...workspace,
                    data: {
                        tabs: cleanedTabs,
                        nextId: workspace.data.nextId
                    }
                };
            });

            localStorage.setItem(WORKSPACES_KEY, JSON.stringify(cleanedWorkspaces));
            localStorage.setItem(CURRENT_WORKSPACE_KEY, currentWorkspaceId.value);
        } catch (e) {
            console.error('[WorkspaceStore] Save error:', e);
        }
    };

    const updateLastAccessed = (workspaceId: string) => {
        const workspace = workspaces.value.find(w => w.id === workspaceId);
        if (workspace) {
            workspace.lastAccessedAt = Date.now();
        }
    };

    // Initialize synchronously
    loadFromStorage();

    // Ensure at least one tab exists
    const current = currentWorkspace.value;
    if (current && current.data.tabs.length === 0) {
        current.data.tabs.push({
            id: 1,
            title: PAGE_TITLES['openfile'],
            isActive: true,
            currentPage: 'openfile'
        });
        current.data.nextId = 2;
    }

    console.log('[WorkspaceStore] Initialization complete');

    // Restore PDF virtual URLs asynchronously (doesn't block UI)
    (async () => {
        try {
            for (const workspace of workspaces.value) {
                for (const tab of workspace.data.tabs) {
                    if (tab.pdfState && tab.pdfState.filePath && !tab.pdfState.fileUrl) {
                        try {
                            const { pdfService } = await import('../services/pdfService');
                            const isReady = await pdfService.checkManagerReady();
                            if (isReady) {
                                const virtualUrl = await pdfService.recreateVirtualUrl(tab.pdfState.filePath);
                                if (virtualUrl) {
                                    tab.pdfState.fileUrl = virtualUrl;
                                    console.log('[WorkspaceStore] Restored PDF virtual URL for:', tab.title);
                                }
                            }
                        } catch (error) {
                            console.warn('[WorkspaceStore] Failed to restore PDF virtual URL:', tab.title, error);
                        }
                    }
                }
            }
        } catch (error) {
            console.error('[WorkspaceStore] PDF restoration error:', error);
        }
    })();

    // Set up watch AFTER initial load to avoid saving immediately
    watch([workspaces, currentWorkspaceId], saveToStorage, { deep: true });

    const createWorkspace = (name: string): string => {
        const id = `workspace_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;

        const newWorkspace: Workspace = {
            id,
            name,
            createdAt: Date.now(),
            lastAccessedAt: Date.now(),
            data: {
                tabs: [{
                    id: 1,
                    title: PAGE_TITLES['openfile'],
                    isActive: true,
                    currentPage: 'openfile'
                }],
                nextId: 2
            }
        };
        workspaces.value.push(newWorkspace);
        return id;
    };

    const deleteWorkspace = (workspaceId: string) => {
        if (workspaceId === DEFAULT_WORKSPACE_ID) {
            throw new Error('Cannot delete default workspace');
        }

        workspaces.value = workspaces.value.filter(w => w.id !== workspaceId);

        if (currentWorkspaceId.value === workspaceId) {
            currentWorkspaceId.value = DEFAULT_WORKSPACE_ID;
            updateLastAccessed(DEFAULT_WORKSPACE_ID);
        }
    };

    const renameWorkspace = (workspaceId: string, newName: string) => {
        const workspace = workspaces.value.find(w => w.id === workspaceId);
        if (workspace) {
            workspace.name = newName;
        }
    };

    const switchWorkspace = (workspaceId: string) => {
        const workspace = workspaces.value.find(w => w.id === workspaceId);
        if (!workspace) {
            throw new Error('Workspace not found');
        }

        currentWorkspaceId.value = workspaceId;
        updateLastAccessed(workspaceId);
    };

    return {
        workspaces,
        currentWorkspaceId,
        currentWorkspace,
        tabs,
        activeTab,
        createWorkspace,
        deleteWorkspace,
        renameWorkspace,
        switchWorkspace
    };
});
