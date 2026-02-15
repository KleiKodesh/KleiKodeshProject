<template>
    <div v-if="isVisible"
         class="dialog-overlay"
         @click="handleOverlayClick">
        <div class="dialog-container"
             @click.stop
             :class="dialogSizeClass">
            <!-- Header -->
            <div v-if="title || showCloseButton"
                 class="dialog-header">
                <h3 v-if="title"
                    class="dialog-title">{{ title }}</h3>
                <button v-if="showCloseButton"
                        @click="handleClose"
                        class="dialog-close-btn">
                    ✕
                </button>
            </div>

            <!-- Content -->
            <div class="dialog-content">
                <!-- Slot for custom content -->
                <slot>
                    <!-- Default content for simple dialogs -->
                    <div v-if="message"
                         class="dialog-message">
                        <div v-if="icon"
                             class="dialog-icon"
                             :class="iconClass">
                            {{ icon }}
                        </div>
                        <p>{{ message }}</p>
                    </div>
                </slot>
            </div>

            <!-- Actions -->
            <div v-if="showActions"
                 class="dialog-actions">
                <slot name="actions">
                    <!-- Default actions -->
                    <button v-if="showCancel"
                            @click="handleCancel"
                            class="dialog-btn dialog-btn-cancel">
                        {{ cancelText }}
                    </button>
                    <button v-if="showConfirm"
                            @click="handleConfirm"
                            class="dialog-btn dialog-btn-confirm"
                            :class="confirmVariantClass">
                        {{ confirmText }}
                    </button>
                </slot>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onUnmounted } from 'vue'
import { useEventListener } from '@vueuse/core'

interface Props {
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

const props = withDefaults(defineProps<Props>(), {
    title: '',
    message: '',
    icon: '',
    iconType: 'info',
    confirmText: 'אישור',
    cancelText: 'ביטול',
    confirmVariant: 'primary',
    showConfirm: true,
    showCancel: true,
    showCloseButton: false,
    showActions: true,
    size: 'medium',
    closeOnOverlay: true
})

const emit = defineEmits<{
    confirm: []
    cancel: []
    close: []
}>()

const isVisible = ref(false)

const dialogSizeClass = computed(() => `dialog-${props.size}`)
const iconClass = computed(() => `dialog-icon-${props.iconType}`)
const confirmVariantClass = computed(() => `dialog-btn-${props.confirmVariant}`)

// Handle keyboard events when dialog is visible
useEventListener('keydown', (event: KeyboardEvent) => {
    if (!isVisible.value) return

    // Escape key - cancel
    if (event.code === 'Escape') {
        handleCancel()
    }

    // Enter key - confirm or cancel
    if (event.code === 'Enter') {
        if (props.showConfirm) {
            handleConfirm()
        } else {
            handleCancel()
        }
    }
})

const show = () => {
    isVisible.value = true
}

const hide = () => {
    isVisible.value = false
}

const handleConfirm = () => {
    hide()
    emit('confirm')
}

const handleCancel = () => {
    hide()
    emit('cancel')
}

const handleClose = () => {
    hide()
    emit('close')
}

const handleOverlayClick = () => {
    if (props.closeOnOverlay) {
        handleCancel()
    }
}

defineExpose({
    show,
    hide
})
</script>

<style scoped>
.dialog-overlay {
    position: fixed;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    background: rgba(0, 0, 0, 0.5);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 2000;
    backdrop-filter: blur(2px);
    animation: fadeIn 0.2s ease-out;
}

@keyframes fadeIn {
    from {
        opacity: 0;
    }

    to {
        opacity: 1;
    }
}

.dialog-container {
    background: var(--bg-primary);
    border-radius: 8px;
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
    max-height: 80vh;
    overflow: hidden;
    border: 1px solid var(--border-color);
    animation: slideIn 0.3s ease-out;
    position: relative;
}

@keyframes slideIn {
    from {
        opacity: 0;
        transform: translateY(-20px) scale(0.95);
    }

    to {
        opacity: 1;
        transform: translateY(0) scale(1);
    }
}

.dialog-small {
    max-width: 300px;
    width: 90%;
}

.dialog-medium {
    max-width: 400px;
    width: 90%;
}

.dialog-large {
    max-width: 600px;
    width: 95%;
}

.dialog-header {
    padding: 20px 20px 0 20px;
    border-bottom: 1px solid var(--border-color);
    margin-bottom: 20px;
    display: flex;
    align-items: center;
    justify-content: space-between;
}

.dialog-title {
    margin: 0;
    font-size: 18px;
    font-weight: bold;
    color: var(--text-primary);
    flex: 1;
    text-align: center;
}

.dialog-close-btn {
    position: absolute;
    top: 15px;
    right: 15px;
    background: none;
    border: none;
    font-size: 18px;
    color: var(--text-secondary);
    cursor: pointer;
    padding: 5px;
    border-radius: 4px;
    transition: all 0.2s ease;
}

.dialog-close-btn:hover {
    background: var(--hover-bg);
    color: var(--text-primary);
}

.dialog-content {
    padding: 0 20px 20px 20px;
    overflow-y: auto;
    max-height: 60vh;
}

.dialog-message {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 12px;
}

.dialog-message p {
    margin: 0;
    font-size: 14px;
    color: var(--text-primary);
    line-height: 1.5;
    text-align: center;
}

.dialog-icon {
    font-size: 32px;
    width: 48px;
    height: 48px;
    border-radius: 50%;
    display: flex;
    align-items: center;
    justify-content: center;
}

.dialog-icon-info {
    background: rgba(13, 110, 253, 0.1);
    color: #0d6efd;
}

.dialog-icon-warning {
    background: rgba(255, 193, 7, 0.1);
    color: #ffc107;
}

.dialog-icon-error {
    background: rgba(220, 53, 69, 0.1);
    color: #dc3545;
}

.dialog-icon-success {
    background: rgba(25, 135, 84, 0.1);
    color: #198754;
}

.dialog-actions {
    padding: 20px;
    display: flex;
    gap: 12px;
    justify-content: center;
    border-top: 1px solid var(--border-color);
}

.dialog-btn {
    padding: 10px 20px;
    border: none;
    border-radius: 6px;
    font-size: 14px;
    font-weight: 500;
    cursor: pointer;
    transition: all 0.2s ease;
    min-width: 80px;
}

.dialog-btn-cancel {
    background: var(--bg-secondary);
    color: var(--text-primary);
    border: 1px solid var(--border-color);
}

.dialog-btn-cancel:hover {
    background: var(--hover-bg);
    border-color: var(--accent-color);
}

.dialog-btn-primary {
    background: var(--accent-color);
    color: white;
}

.dialog-btn-primary:hover {
    background: var(--accent-hover);
    transform: translateY(-1px);
}

.dialog-btn-danger {
    background: #dc3545;
    color: white;
}

.dialog-btn-danger:hover {
    background: #c82333;
    transform: translateY(-1px);
}

.dialog-btn-success {
    background: #198754;
    color: white;
}

.dialog-btn-success:hover {
    background: #157347;
    transform: translateY(-1px);
}
</style>