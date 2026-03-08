/**
 * Line View Selection Management Composable
 * DEPRECATED: Use useScopedSelection from shared instead
 * Kept for backward compatibility during migration
 */

import { type Ref } from 'vue'
import { useScopedSelection } from '@/components/shared/useScopedSelection'

export function useLineViewSelection(scrollerElRef: Ref<HTMLElement | undefined>) {
    return useScopedSelection(scrollerElRef)
}
