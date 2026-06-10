import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import type { TocEntry } from '@/features/book-view/toc/useBookViewToc'

export interface SectionNavResult {
  id: number
  lineIndex: number
}

export async function findNextCommentarySection(
  mainBookId: number,
  commentaryBookId: number,
  currentLineIndex: number,
): Promise<SectionNavResult | null> {
  const rows = await query<SectionNavResult>(SQL.GET_NEXT_SECTION_WITH_COMMENTARY, [
    mainBookId,
    commentaryBookId,
    currentLineIndex,
  ])
  return rows[0] ?? null
}

export async function findPrevCommentarySection(
  mainBookId: number,
  commentaryBookId: number,
  currentLineIndex: number,
): Promise<SectionNavResult | null> {
  const rows = await query<SectionNavResult>(SQL.GET_PREV_SECTION_WITH_COMMENTARY, [
    mainBookId,
    commentaryBookId,
    currentLineIndex,
  ])
  return rows[0] ?? null
}

function getSectionEnd(tocEntry: TocEntry, allEntries: TocEntry[]): number {
  const idx = allEntries.indexOf(tocEntry)
  const next = allEntries
    .slice(idx + 1)
    .find((e) => e.lineIndex != null && e.level <= tocEntry.level)
  return next?.lineIndex ?? 999999999
}

export async function findNextTocCommentarySection(
  mainBookId: number,
  commentaryBookId: number,
  currentTocEntry: TocEntry,
  allEntries: TocEntry[],
): Promise<TocEntry | null> {
  const idx = allEntries.indexOf(currentTocEntry)
  const candidates = allEntries
    .slice(idx + 1)
    .filter((e): e is TocEntry & { lineIndex: number } => e.level === currentTocEntry.level && e.lineIndex != null)
  if (!candidates.length) return null

  // Build flat (sectionStart, sectionEnd) pairs for all candidates in one batch query.
  // Bind order: interleaved pairs first, then mainBookId + commentaryBookId at the end
  // (matching the correlated subquery param order in the SQL).
  const rangePairs = candidates.flatMap((entry) => [entry.lineIndex, getSectionEnd(entry, allEntries)])
  const rows = await query<{ sectionStart: number }>(
    SQL.GET_NEXT_TOC_SECTION_WITH_COMMENTARY(candidates.length),
    [...rangePairs, mainBookId, commentaryBookId],
  )
  if (!rows.length) return null

  const matchingStart = rows[0]!.sectionStart
  return candidates.find((e) => e.lineIndex === matchingStart) ?? null
}

export async function findPrevTocCommentarySection(
  mainBookId: number,
  commentaryBookId: number,
  currentTocEntry: TocEntry,
  allEntries: TocEntry[],
): Promise<TocEntry | null> {
  const idx = allEntries.indexOf(currentTocEntry)
  const candidates = allEntries
    .slice(0, idx)
    .filter((e): e is TocEntry & { lineIndex: number } => e.level === currentTocEntry.level && e.lineIndex != null)
    .reverse()
  if (!candidates.length) return null

  // Same batch approach as findNextTocCommentarySection — single query for all candidates.
  const rangePairs = candidates.flatMap((entry) => [entry.lineIndex, getSectionEnd(entry, allEntries)])
  const rows = await query<{ sectionStart: number }>(
    SQL.GET_PREV_TOC_SECTION_WITH_COMMENTARY(candidates.length),
    [...rangePairs, mainBookId, commentaryBookId],
  )
  if (!rows.length) return null

  const matchingStart = rows[0]!.sectionStart
  return candidates.find((e) => e.lineIndex === matchingStart) ?? null
}
