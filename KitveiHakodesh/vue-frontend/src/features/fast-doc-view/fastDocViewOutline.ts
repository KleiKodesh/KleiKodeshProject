import type { TreeNodeItem } from '@/components/treeTypes'
import type { FastDocViewHeading } from '@/webview-host/fastDocViewApi'

/** A TOC tree row for one heading; `id` is the heading's position in the list plus one. */
export interface FastDocViewOutlineNode extends TreeNodeItem {
  blockIndex: number
}

/**
 * The document's headings as TreeView rows: a heading's parent is the nearest earlier
 * heading of a lower outline level (levels may skip, e.g. 1 then 3). `level` on the
 * row is its tree depth.
 */
export function buildFastDocViewOutline(headings: readonly FastDocViewHeading[]): FastDocViewOutlineNode[] {
  const nodes: FastDocViewOutlineNode[] = []
  const open: { node: FastDocViewOutlineNode; outlineLevel: number }[] = []
  headings.forEach((heading, position) => {
    while (open.length && open[open.length - 1]!.outlineLevel >= heading.level) open.pop()
    const parent = open[open.length - 1]?.node ?? null
    const node: FastDocViewOutlineNode = {
      id: position + 1,
      parentId: parent ? parent.id : null,
      level: open.length,
      hasChildren: false,
      text: heading.text,
      blockIndex: heading.blockIndex,
    }
    if (parent) parent.hasChildren = true
    nodes.push(node)
    open.push({ node, outlineLevel: heading.level })
  })
  return nodes
}
