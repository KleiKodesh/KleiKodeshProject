import { ref, computed } from 'vue';
import { useEventListener } from '@vueuse/core';

interface DialogProps {
    confirmVariant?: 'primary' | 'danger' | 'success';
    iconType?: 'info' | 'warning' | 'error' | 'success';
    showConfirm?: boolean;
    closeOnOverlay?: boolean;
}

export function useCustomDialog(
    props: DialogProps,
    emit: any
) {
    const isVisible = ref(false);

    const dialogSizeClass = computed(() => `dialog-${props.confirmVariant || 'primary'}`);
    const iconClass = computed(() => `dialog-icon-${props.iconType || 'info'}`);
    const confirmVariantClass = computed(() => `dialog-btn-${props.confirmVariant || 'primary'}`);

    // Handle keyboard events when dialog is visible
    useEventListener('keydown', (event: KeyboardEvent) => {
        if (!isVisible.value) return;

        // Escape key - cancel
        if (event.code === 'Escape') {
            handleCancel();
        }

        // Enter key - confirm or cancel
        if (event.code === 'Enter') {
            if (props.showConfirm) {
                handleConfirm();
            } else {
                handleCancel();
            }
        }
    });

    const show = () => {
        isVisible.value = true;
    };

    const hide = () => {
        isVisible.value = false;
    };

    const handleConfirm = () => {
        hide();
        emit('confirm');
    };

    const handleCancel = () => {
        hide();
        emit('cancel');
    };

    const handleClose = () => {
        hide();
        emit('close');
    };

    const handleOverlayClick = () => {
        if (props.closeOnOverlay) {
            handleCancel();
        }
    };

    return {
        isVisible,
        dialogSizeClass,
        iconClass,
        confirmVariantClass,
        show,
        hide,
        handleConfirm,
        handleCancel,
        handleClose,
        handleOverlayClick
    };
}
