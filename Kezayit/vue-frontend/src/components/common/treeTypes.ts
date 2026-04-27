export interface TreeNodeItem {
  id: number
  parentId: number | null
  level: number
  hasChildren: boolean | number
  text: string
}
