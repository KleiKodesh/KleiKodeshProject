// Cards show the path under the title, so "<commentary> על <book>" repeats what the
// path already says: keep only the commentary's name. "על התורה" is kept — it names
// the work itself, not the book it sits under.
const TARGET = /\s+על\s+(?!התורה(?:\s|$)).*$/

/** Title without its " על <book>" tail, unless the tail is "על התורה". */
export function stripCommentaryTarget(title: string): string {
  const stripped = title.replace(TARGET, '')
  return stripped || title
}
