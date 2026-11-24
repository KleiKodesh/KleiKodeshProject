import type { CommentaryVisibilityItem } from '../bookViewTypes'

/** Internal tree node — sections and subsections only. Books are leaves (CommentaryVisibilityItem). */
export interface CommentaryTreeNode {
  label: string
  children: (CommentaryTreeNode | CommentaryVisibilityItem)[]
}

export function isTreeNode(
  node: CommentaryTreeNode | CommentaryVisibilityItem,
): node is CommentaryTreeNode {
  return 'children' in node
}
