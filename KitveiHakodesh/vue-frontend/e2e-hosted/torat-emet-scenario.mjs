// The Torat Emet scenario of the hosted rig: the service calls added for Torat Emet, driven through the real
// relay -> service -> Core path. Only fixtures from the rig's temp folder are read (see torat-emet-fixtures.mjs).
import path from 'node:path'
import { he, readNamesTable, BOOKS_ROOT_TITLE, MYBOOKS_ROOT_TITLE } from './torat-emet-fixtures.mjs'

const same = (a, b) => JSON.stringify(a) === JSON.stringify(b)
const must = (cond, msg) => { if (!cond) throw new Error(msg) }
const asc = (s) => [...String(s)].map((c) => (c >= ' ' && c <= '~' ? c : 'U+' + c.charCodeAt(0).toString(16))).join('')
const SEP = String.fromCharCode(92)
const lc = (s) => String(s).toLowerCase()

export function toratEmetEnv(f) {
  return { KHS_TEST_INDEX_FOLDER: f.base, KHS_TEST_DOCUMENTS_DIR: f.docs }
}

export async function runToratEmetChecks({ page, check, fx, backendDir, waitFor }) {
  const call = (op, args = {}) => page.evaluate(([o, a]) => window.__khTest.serviceCall(o, a), [op, args])
  const names = readNamesTable(backendDir)
  const rel = (p) => lc(path.relative(fx.books, p))
  const toraHe = names.folders.get('tora'), bereshitHe = names.folders.get('bereshit')
  const genesisHe = names.books.get(rel(fx.genesis)), rashiHe = names.books.get(rel(fx.rashi))
  must(toraHe && bereshitHe && genesisHe && rashiHe, 'names table lacks the fixture books')
  const hits = (r) => (r?.results ?? []).map((h) => ({ ...h, p: lc(path.join(h.path, h.fileName)) }))

  await check('te: locateDocuments max=-1 returns every match (alias phrase)', async () => {
    const r = await waitFor('index', () => call('locateDocuments', { query: he('ovro amo'), max: -1 }).then((x) => (x?.total >= 4 ? x : false)), 60_000, 500)
    const h = hits(r)
    must(h.length === r.total, `results ${h.length} != total ${r.total}`)
    for (const p of [fx.genesis, fx.rashi, fx.unlisted, fx.myBook]) must(h.some((x) => x.p === lc(p)), `missing ${asc(path.basename(p))} among ${h.map((x) => path.basename(x.p)).join(',')}`)
    must(!h.some((x) => x.p === lc(fx.plain)), 'plain file matched the phrase')
    return `${h.length} hits`
  })

  await check('te: locateDocuments Hebrew name finds the book, DisplayName/DisplayFolder set', async () => {
    const r = await call('locateDocuments', { query: genesisHe, max: -1 })
    const g = hits(r).find((x) => x.p === lc(fx.genesis))
    must(g, 'genesis not found by its Hebrew name')
    must(g.displayName === genesisHe, `displayName ${asc(g.displayName)}`)
    must(String(g.displayFolder).endsWith(`${SEP}${toraHe}${SEP}${bereshitHe}`), `displayFolder ${asc(g.displayFolder)}`)
    return 'ok'
  })

  await check('te: locateDocuments default max still works', async () => {
    const r = await call('locateDocuments', { query: he('ovro amo') })
    must(hits(r).length >= 4, `only ${hits(r).length}`)
    return `${hits(r).length} hits`
  })

  await check('te: claimDefaultUserSpaceFolders finds Books and MyBooks (fake Documents)', async () => {
    const r = await call('claimDefaultUserSpaceFolders')
    must(same((r?.folders ?? []).map(lc), [lc(fx.books), lc(fx.myBooks)]), `folders ${asc(JSON.stringify(r))}`)
    return '2 folders'
  })

  await check('te: userSpaceRootTitles names the default roots', async () => {
    const r = await call('userSpaceRootTitles', { values: [fx.books, fx.myBooks, fx.other] })
    must(same(r?.titles, [BOOKS_ROOT_TITLE, MYBOOKS_ROOT_TITLE, '']), `titles ${asc(JSON.stringify(r))}`)
    return '3 titles'
  })

  await check('te: displayLocalPaths (book, folder, roots, MyBooks, ordinary file)', async () => {
    const items = [[fx.genesis, false], [fx.bereshit, true], [fx.books, true], [fx.myBooks, true], [fx.myBook, false], [fx.plain, false]]
    const r = await call('displayLocalPaths', { paths: items.map((i) => i[0]), isFolder: items.map((i) => i[1]) })
    must(r?.titles?.length === 6 && r?.paths?.length === 6, `shape ${asc(JSON.stringify(r))}`)
    must(r.titles[0] === genesisHe, 'book title')
    must(r.paths[0] === `${BOOKS_ROOT_TITLE}${SEP}${toraHe}${SEP}${bereshitHe}${SEP}${genesisHe}`, `book path ${asc(r.paths[0])} want ${asc(`${BOOKS_ROOT_TITLE}${SEP}${toraHe}${SEP}${bereshitHe}${SEP}${genesisHe}`)}`)
    must(r.titles[1] === bereshitHe && r.paths[1] === `${BOOKS_ROOT_TITLE}${SEP}${toraHe}${SEP}${bereshitHe}`, 'folder')
    must(r.titles[2] === BOOKS_ROOT_TITLE && r.paths[2] === BOOKS_ROOT_TITLE, 'Books root')
    must(r.titles[3] === MYBOOKS_ROOT_TITLE && r.paths[3] === MYBOOKS_ROOT_TITLE, 'MyBooks root')
    must(r.titles[4] === '' && r.paths[4] === `${MYBOOKS_ROOT_TITLE}${SEP}mybook.txt`, `MyBooks file ${asc(r.paths[4])}`)
    must(r.titles[5] === '' && r.paths[5] === '', 'ordinary file')
    return '6 answers'
  })

  await check('te: listLocalFolder titles and on-disk order', async () => {
    const r = await call('listLocalFolder', { value: fx.bereshit })
    must(same(r.files, ['a01_genesis.txt', 'c_rashi_bereshit_l1.txt', 'k_unlisted.txt']), `files ${asc(JSON.stringify(r.files))}`)
    must(same(r.fileTitles, [genesisHe, rashiHe, '']), `fileTitles ${asc(JSON.stringify(r.fileTitles))}`)
    const t = await call('listLocalFolder', { value: fx.tora })
    must(same(t.folders, ['01_BERESHIT', '02_SHEMOT']), `folders ${asc(JSON.stringify(t.folders))}`)
    must(t.folderTitles[0] === bereshitHe && t.folderTitles[1] === names.folders.get('shemot'), 'folderTitles')
    return `${r.files.length} files, ${t.folders.length} folders`
  })

  await check('te: userSpaceSearch over the Books root', async () => {
    const r = await call('userSpaceSearch', { query: genesisHe, roots: [{ folder: fx.books }] })
    const g = (r?.hits ?? []).find((h) => lc(h.filePath) === lc(fx.genesis))
    must(g, `no hit: ${asc(JSON.stringify(r).slice(0, 300))}`)
    must(g.title === genesisHe && String(g.displayFolder).endsWith(`${SEP}${toraHe}${SEP}${bereshitHe}`), `title/displayFolder ${asc(g.title)} | ${asc(g.displayFolder)} want ${asc(genesisHe)}`)
    must(!(r.hits ?? []).some((h) => lc(h.filePath) === lc(fx.plain)), 'a file outside the root')
    return `${r.hits.length} hits`
  })

  const openDoc = async (file, openId) => {
    const s = await call('fastDocViewOpen', { path: file, openId })
    must(s?.documentId, 'no documentId')
    const done = s.isIndexComplete ? s : await waitFor('index', async () => { const x = await call('fastDocViewScan', { documentId: s.documentId, blocks: 500 }); return x?.isIndexComplete ? x : false }, 20_000)
    const count = done.knownBlockCount ?? s.blockCount
    const blocks = await call('fastDocViewBlocks', { documentId: s.documentId, first: 0, count: Math.max(count, 1) })
    const headings = await call('fastDocViewHeadings', { documentId: s.documentId })
    return { s, blocks: blocks ?? [], headings: headings ?? [] }
  }
  const text = (b) => String(b.html).replace(/<[^>]*>/g, '')

  await check('te: FastDocView opens a Torat Emet .txt (title, blocks, headings, header)', async () => {
    const { s, blocks, headings } = await openDoc(fx.genesis, 'rig-te-1')
    must(s.title === fx.bookName, `title ${asc(s.title)}`)
    must(blocks.length >= 3 && blocks.every((b) => typeof b.html === 'string'), `blocks ${blocks.length}`)
    must(headings.length >= 1, 'no headings')
    must(blocks.some((b) => text(b).includes(he('bravyt'))), 'text missing')
    await call('fastDocViewClose', { documentId: s.documentId })
    return `${blocks.length} blocks, ${headings.length} headings, kinds ${blocks.map((b) => b.kind).join(',')}`
  })

  await check('te: FastDocView plain .txt drops blank lines, keeps h1-h3 headings', async () => {
    const { s, blocks, headings } = await openDoc(fx.plain, 'rig-te-2')
    must(blocks.length > 0 && blocks.every((b) => text(b).trim().length > 0), `empty block among ${blocks.length}`)
    must(headings.length === 3 && same(headings.map((h) => h.level), [1, 2, 3]), `headings ${JSON.stringify(headings.map((h) => h.level))}`)
    await call('fastDocViewClose', { documentId: s.documentId })
    return `${blocks.length} blocks`
  })

  await check('te: FastDocView opens a UTF-16 .txt', async () => {
    const { s, blocks } = await openDoc(fx.utf16, 'rig-te-3')
    must(blocks.some((b) => text(b).includes(he('shlvm'))), 'Hebrew text not decoded')
    await call('fastDocViewClose', { documentId: s.documentId })
    return `${blocks.length} blocks`
  })

  await check('te: fastDocViewCancelOpen answers', async () => {
    await call('fastDocViewCancelOpen', { openId: 'rig-te-unknown' })
    return 'ok'
  })
}
