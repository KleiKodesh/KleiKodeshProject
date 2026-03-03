import { ref, nextTick } from 'vue';
import { useWorkspace } from '@/components/workspace/useWorkspace';

export function useWorkspaceEditor() {
    const {
        workspaces,
        currentWorkspace,
        tabs,
        createWorkspace,
        switchWorkspace,
        deleteWorkspace: deleteWorkspaceAction,
        renameWorkspace,
        getWorkspaceName
    } = useWorkspace();

    const newWorkspaceName = ref('');
    const editingId = ref<string | null>(null);
    const editingName = ref('');
    const editInput = ref<HTMLInputElement>();

    const getWorkspaceTabCount = (workspaceId: string): number => {
        if (workspaceId === currentWorkspace.value) {
            return tabs.value.filter(tab => tab.currentPage !== 'workspaces').length;
        }

        const storageKey = `tabStore_${workspaceId}`;
        const stored = localStorage.getItem(storageKey);
        if (stored) {
            try {
                const data = JSON.parse(stored);
                return data.tabs?.length || 0;
            } catch {
                return 0;
            }
        }
        return 0;
    };

    const createNew = () => {
        const name = newWorkspaceName.value.trim();
        if (!name) return;

        createWorkspace(name);
        newWorkspaceName.value = '';
    };

    const startEdit = (workspaceId: string) => {
        editingId.value = workspaceId;
        editingName.value = getWorkspaceName(workspaceId);
        nextTick(() => {
            editInput.value?.focus();
            editInput.value?.select();
        });
    };

    const saveEdit = () => {
        if (editingId.value && editingName.value.trim()) {
            renameWorkspace(editingId.value, editingName.value.trim());
        }
        cancelEdit();
    };

    const cancelEdit = () => {
        editingId.value = null;
        editingName.value = '';
    };

    const switchTo = (workspaceId: string) => {
        if (workspaceId !== currentWorkspace.value) {
            switchWorkspace(workspaceId);
        }
    };

    const deleteWorkspaceHandler = (workspaceId: string) => {
        deleteWorkspaceAction(workspaceId);
    };

    return {
        workspaces,
        currentWorkspace,
        newWorkspaceName,
        editingId,
        editingName,
        editInput,
        getWorkspaceTabCount,
        getWorkspaceName,
        createNew,
        startEdit,
        saveEdit,
        cancelEdit,
        switchTo,
        deleteWorkspaceHandler
    };
}
