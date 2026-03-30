import { query } from '@/host/db'
import { SQL } from '@/host/queries.sql'
import type { TocEntry } from '@/components/book-view/useToc'

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
    .filter((e) => e.level === currentTocEntry.level && e.lineIndex != null)
  for (const entry of candidates) {
    const sectionEnd = getSectionEnd(entry, allEntries)
    const rows = await query<{ 1: number }>(SQL.HAS_COMMENTARY_IN_RANGE, [
      mainBookId,
      commentaryBookId,
      entry.lineIndex!,
      sectionEnd,
    ])
    if (rows.length) return entry
  }
  return null
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
    .filter((e) => e.level === currentTocEntry.level && e.lineIndex != null)
    .reverse()
  for (const entry of candidates) {
    const sectionEnd = getSectionEnd(entry, allEntries)
    const rows = await query<{ 1: number }>(SQL.HAS_COMMENTARY_IN_RANGE, [
      mainBookId,
      commentaryBookId,
      entry.lineIndex!,
      sectionEnd,
    ])
    if (rows.length) return entry
  }
  return null
}
