import { ref, nextTick } from 'vue'

export interface DialogOptions {
    title?: string
    message?: string
    icon?: string
    iconType?: 'info' | 'warning' | 'error' | 'success'
    confirmText?: string
    cancelText?: string
    confirmVariant?: 'primary' | 'danger' | 'success'
    showConfirm?: boolean
    showCancel?: boolean
    showCloseButton?: boolean
    showActions?: boolean
    size?: 'small' | 'medium' | 'large'
    closeOnOverlay?: boolean
}

export function useDialog() {
    const dialogRef = ref<any>(null)
    const dialogOptions = ref<DialogOptions>({})
    let currentResolve: ((value: boolean) => void) | null = null

    const showDialog = (options: DialogOptions = {}) => {
        return new Promise<boolean>((resolve) => {
            if (!dialogRef.value) {
                console.error('Dialog ref not found')
                resolve(false)
                return
            }

            currentResolve = resolve
            dialogOptions.value = options

            // Show the dialog
            nextTick(() => {
                dialogRef.value?.show()
            })
        })
    }

    const handleConfirm = () => {
        if (currentResolve) {
            currentResolve(true)
            currentResolve = null
        }
    }

    const handleCancel = () => {
        if (currentResolve) {
            currentResolve(false)
            currentResolve = null
        }
    }

    const handleClose = () => {
        if (currentResolve) {
            currentResolve(false)
            currentResolve = null
        }
    }

    const confirm = (message: string, options: Omit<DialogOptions, 'message'> = {}) => {
        return showDialog({
            message,
            title: options.title || 'אישור',
            confirmText: options.confirmText || 'אישור',
            cancelText: options.cancelText || 'ביטול',
            confirmVariant: options.confirmVariant || 'primary',
            showConfirm: true,
            showCancel: true,
            ...options
        })
    }

    const alert = (message: string, options: Omit<DialogOptions, 'message'> = {}) => {
        return showDialog({
            message,
            title: options.title || 'הודעה',
            confirmText: options.confirmText || 'אישור',
            showConfirm: true,
            showCancel: false,
            ...options
        })
    }

    const error = (message: string, options: Omit<DialogOptions, 'message'> = {}) => {
        return alert(message, {
            title: 'שגיאה',
            confirmVariant: 'danger',
            ...options
        })
    }

    const warning = (message: string, options: Omit<DialogOptions, 'message'> = {}) => {
        return confirm(message, {
            title: 'אזהרה',
            confirmVariant: 'danger',
            ...options
        })
    }

    const success = (message: string, options: Omit<DialogOptions, 'message'> = {}) => {
        return alert(message, {
            title: 'הצלחה',
            confirmVariant: 'success',
            ...options
        })
    }

    return {
        dialogRef,
        dialogOptions,
        showDialog,
        confirm,
        alert,
        error,
        warning,
        success,
        handleConfirm,
        handleCancel,
        handleClose
    }
}