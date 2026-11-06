// Fixtures for the Torat Emet scenario: a fake Documents folder with a Torat Emet install tree, MyBooks,
// and ordinary files, all inside the rig's temp folder. Hebrew is built from escapes (ASCII source).
import fs from 'node:fs'
import path from 'node:path'

/** Hebrew letters from a Latin spelling: a=alef b=bet g=gimel d=dalet h=he v=vav z=zayin x=het t=tet y=yod k=kaf l=lamed m=mem n=nun s=samekh e=ayin p=pe c=tsadi q=qof r=resh w=shin o=tav, space kept. */
const LETTERS = {
  a: 0x5d0, b: 0x5d1, g: 0x5d2, d: 0x5d3, h: 0x5d4, v: 0x5d5, z: 0x5d6, x: 0x5d7, t: 0x5d8, y: 0x5d9,
  M: 0x5dd, k: 0x5db, l: 0x5dc, m: 0x5de, n: 0x5e0, s: 0x5e1, e: 0x5e2, p: 0x5e4, c: 0x5e6, q: 0x5e7, r: 0x5e8, w: 0x5e9, o: 0x5ea,
}
export const he = (latin) => [...latin].map((ch) => (LETTERS[ch] ? String.fromCharCode(LETTERS[ch]) : ch)).join('')

/** windows-1255 bytes of a string made of Hebrew letters, ASCII and nothing else. */
export function cp1255(text) {
  return Buffer.from([...text].map((ch) => {
    const c = ch.charCodeAt(0)
    return c >= 0x5d0 && c <= 0x5ea ? 0xe0 + (c - 0x5d0) : c < 0x80 ? c : 0x3f
  }))
}

export const BOOKS_ROOT_TITLE = he('ovro amo')
export const MYBOOKS_ROOT_TITLE = he('ovro amo - spryM aywyyM')

/** The names table the product ships (source of truth): lower-cased book path -> title, folder token -> title. */
export function readNamesTable(backendDir) {
  const file = path.join(backendDir, 'KitveiHakodesh.Core', 'LocalFiles', 'ToratEmet', 'ToratEmetNamesData.cs')
  const folders = new Map(), books = new Map()
  for (const line of fs.readFileSync(file, 'utf8').split(/\r?\n/)) {
    const cells = line.split('\t')
    if (cells[0] === 'F' && cells.length >= 3) folders.set(cells[1].toLowerCase(), cells[2])
    else if (cells[0] === 'B' && cells.length >= 3) books.set(cells[1].toLowerCase(), cells[2])
  }
  return { folders, books }
}

const write = (file, data) => { fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, data) }

/** Builds the tree under <base>; returns every path the scenario needs. */
export function makeFixtures(base) {
  const docs = path.join(base, 'docs')
  const books = path.join(docs, 'ToratEmetInstall', 'Books')
  const bereshit = path.join(books, '001_TORA', '01_BERESHIT')
  const myBooks = path.join(docs, 'ToratEmetUserData', 'MyBooks')
  const other = path.join(base, 'other')
  const f = {
    base, docs, books, bereshit, myBooks, other,
    tora: path.join(books, '001_TORA'),
    genesis: path.join(bereshit, 'a01_genesis.txt'),
    rashi: path.join(bereshit, 'c_rashi_bereshit_l1.txt'),
    unlisted: path.join(bereshit, 'k_unlisted.txt'),
    myBook: path.join(myBooks, 'mybook.txt'),
    plain: path.join(other, 'plain.txt'),
    utf16: path.join(other, 'utf16.txt'),
    bookName: he('bravyt mvgm'),
  }
  fs.mkdirSync(path.join(books, '001_TORA', '02_SHEMOT'), { recursive: true })
  const head = `&ForcedBookName=${f.bookName}&RavMechaber=${he('wmh')}\r\n`
  const body = `$ ${f.bookName}\r\n~ ${he('pqv')} a\r\n${he('bravyt')} <b>${he('bra')}</b> ${he('alhym')}\r\n\r\n${he('vhargc')}\r\n`
  write(f.genesis, cp1255(head + body))
  write(f.rashi, cp1255(`$ ${he('rwy')}\r\n${he('bravyt')}\r\n`))
  write(f.unlisted, cp1255(`$ ${he('lla')}\r\n${he('abg')}\r\n`))
  write(f.myBook, Buffer.from(`﻿${he('spr')}\r\n`, 'utf8'))
  const plain = `<h1>${he('kvtrt')}</h1>\r\n\r\n${he('pskh')} a\r\n\r\n<h2>${he('mshne')}</h2>\r\n${he('pskh')} b\r\n\r\n<h3>${he('mshn')}</h3>\r\n${he('pskh')} g\r\n`
  write(f.plain, Buffer.from(`﻿${plain}`, 'utf8'))
  write(f.utf16, Buffer.concat([Buffer.from([0xff, 0xfe]), Buffer.from(`${he('shlvm')} ${he('evlm')}\r\n${he('shvb')}\r\n`, 'utf16le')]))
  return f
}
