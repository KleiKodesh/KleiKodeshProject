declare module 'vue3-virtual-scroller' {
    import { DefineComponent } from 'vue'

    interface DynamicScrollerSlotProps {
        item: any
        index: number
        active: boolean
    }

    interface DynamicScrollerMethods {
        scrollToItem: (index: number) => void
        scrollToBottom: () => void
        getScroll: () => { start: number; end: number }
    }

    export const DynamicScroller: DefineComponent<{
        items: any[]
        minItemSize: number
        buffer?: number
        keyField?: string
    }, DynamicScrollerMethods, {}, {}, {}, {}, {}, {
        default: (props: DynamicScrollerSlotProps) => any
    }>

    export const DynamicScrollerItem: DefineComponent<{
        item: any
        active: boolean
        sizeDependencies?: any[]
    }>

    export const RecycleScroller: DefineComponent<any, any, any>
}