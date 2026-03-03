import { useMagicKeys, whenever } from '@vueuse/core'
import { type MaybeRefOrGetter, toValue } from 'vue'

/**
 * Composable for handling keyboard shortcuts using VueUse's useMagicKeys
 * Provides a clean API for common keyboard patterns
 */
export function useKeyboardShortcuts() {
    const keys = useMagicKeys()

    return {
        keys,

        /**
         * Register a handler for Ctrl+Key (or Cmd+Key on Mac)
         */
        onCtrl(key: string, handler: () => void, options?: { enabled?: MaybeRefOrGetter<boolean> }) {
            const keyRef = keys[`Ctrl+${key}`] || keys[`Meta+${key}`]
            if (keyRef) {
                whenever(keyRef, () => {
                    if (options?.enabled !== undefined && !toValue(options.enabled)) return
                    handler()
                })
            }
        },

        /**
         * Register a handler for a simple key press
         */
        onKey(key: string, handler: () => void, options?: { enabled?: MaybeRefOrGetter<boolean> }) {
            const keyRef = keys[key]
            if (keyRef) {
                whenever(keyRef, () => {
                    if (options?.enabled !== undefined && !toValue(options.enabled)) return
                    handler()
                })
            }
        },

        /**
         * Register a handler for Shift+Key
         */
        onShift(key: string, handler: () => void, options?: { enabled?: MaybeRefOrGetter<boolean> }) {
            const keyRef = keys[`Shift+${key}`]
            if (keyRef) {
                whenever(keyRef, () => {
                    if (options?.enabled !== undefined && !toValue(options.enabled)) return
                    handler()
                })
            }
        },

        /**
         * Check if a key combination is currently pressed
         */
        isPressed(key: string): boolean {
            return toValue(keys[key]) ?? false
        }
    }
}
