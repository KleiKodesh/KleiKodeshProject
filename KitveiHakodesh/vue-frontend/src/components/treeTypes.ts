export interface TreeNodeItem {
  id: number
  parentId: number | null
  level: number
  hasChildren: boolean | number
  text: string
  /** Optional dim text at the row's end edge (e.g. a TOC entry's page number). */
  trailing?: string
}
