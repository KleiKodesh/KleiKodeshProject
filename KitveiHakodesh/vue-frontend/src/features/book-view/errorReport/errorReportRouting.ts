/**
 * Which inbox a report about a given corpus ends up in, and therefore whether a
 * machine-applicable correction may be offered at all.
 *
 * The collector routes on `source_folder` and mails the report onward to whoever
 * maintains that corpus. Only reports that reach Otzaria's own mailbox enter its
 * corrections system, so only those may carry a correction block — a correction sent
 * anywhere else is a payload in a schema its recipient never agreed to, addressed to
 * people who cannot act on it.
 *
 * The map mirrors the collector's own; matching is lowercase substring, which is what
 * absorbs the "...ToOtzaria" suffixes our `source.name` values carry.
 */

const OTZARIA_MAILBOX = 'otzaria.200@gmail.com'

/**
 * Corpus key to recipients. ORDER MATTERS: the first key found as a substring wins,
 * exactly as on the collector's side.
 */
const SOURCE_RECIPIENTS: ReadonlyArray<readonly [string, string]> = [
  ['sefariaToOtzaria', 'corrections@sefaria.org,jewishoffice@gmail.com'],
  ['sefaria', 'corrections@sefaria.org,jewishoffice@gmail.com'],
  ['wikiJewishBooks', `${OTZARIA_MAILBOX},WikiJewishBooks@gmail.com`],
  ['wikiSource', `${OTZARIA_MAILBOX},novartza@gmail.com`],
  ['Pninim', `${OTZARIA_MAILBOX},contact@pninim.org`],
  ['Tashma', `${OTZARIA_MAILBOX},jewishoffice@gmail.com`],
  ['Dicta', `${OTZARIA_MAILBOX},jewishoffice@gmail.com`],
  ['Ben-Yehuda', `${OTZARIA_MAILBOX},editor@benyehuda.org`],
  ['yam-HaHachma', `${OTZARIA_MAILBOX},y025837086@gmail.com`],
]

/** Who a report about this corpus is forwarded to. Unknown corpora reach Otzaria. */
export function emailRecipientsFor(sourceFolder: string | null | undefined): string {
  if (!sourceFolder) return OTZARIA_MAILBOX
  const normalized = sourceFolder.toLowerCase()
  const match = SOURCE_RECIPIENTS.find(([key]) => normalized.includes(key.toLowerCase()))
  return match ? match[1] : OTZARIA_MAILBOX
}

/**
 * Does a report about this corpus reach Otzaria's own mailbox?
 *
 * Only then may a correction be proposed. Sefaria-sourced books, for instance, route
 * solely to Sefaria's own maintainers — so those reports stay free text.
 */
export function reportReachesOtzaria(sourceFolder: string | null | undefined): boolean {
  return emailRecipientsFor(sourceFolder).split(',').includes(OTZARIA_MAILBOX)
}
