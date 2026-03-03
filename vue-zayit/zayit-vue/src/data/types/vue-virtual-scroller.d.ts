declare module 'vue-virtual-scroller' {
    import type { ComponentPublicInstance } from 'vue'

    export interface DynamicScrollerInstance extends ComponentPublicInstance {
        scrollToItem(index: number): void
        scrollToPosition(position: number): void
        getScroll(): { start: number; end: number }
    }

    export const DynamicScroller: {
        new(): DynamicScrollerInstance
        __isFragment?: never
        __isTeleport?: never
        __isSuspense?: never
    }

    export const DynamicScrollerItem: {
        new(): ComponentPublicInstance
        __isFragment?: never
        __isTeleport?: never
        __isSuspense?: never
    }
}
