export interface TocEntry {
    id: number
    bookId: number
    parentId?: number
    textId?: number
    level: number
    lineId: number
    lineIndex: number
    isLastChild: boolean
    hasChildren: boolean
    text: string
    path?: string
    children?: TocEntry[]
    isExpanded?: boolean
}
