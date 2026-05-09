/**
 * Dictionary lookup cache — persisted in app-dict-cache IDB under `dict:` prefix.
 * LRU-capped at 100 headwords.
 *
 * Each entry stores the full WordPageData for a headword so repeated lookups of the
 * same word skip all DB queries and return instantly.
 *
 * Results are NOT cached in memory — dictionary entries can be large (many senses,
 * links, synonyms). Only the LRU key list is kept in memory.
 */
import { idbDictionaryCacheGet, idbDictionaryCacheSet, idbDictionaryCacheDelete } from '@/utils/persistence'
import type { WordPageData } from './dictionaryTypes'

const PREFIX = 'dict:'
const LRU_KEY = `${PREFIX}lru`
const MAX = 100

function cacheKey(headword: string) {
  return `${PREFIX}${headword}`
}

async function getLru(): Promise<string[]> {
  return (await idbDictionaryCacheGet<string[]>(LRU_KEY)) ?? []
}

async function touchLru(headword: string): Promise<void> {
  const lru = await getLru()
  const updated = [...lru.filter((w) => w !== headword), headword]
  await idbDictionaryCacheSet(LRU_KEY, updated)
}

async function evictIfNeeded(headword: string): Promise<void> {
  const lru = await getLru()
  const without = lru.filter((w) => w !== headword)
  if (without.length < MAX) return
  const evict = without.shift()!
  await idbDictionaryCacheDelete(cacheKey(evict))
  await idbDictionaryCacheSet(LRU_KEY, without)
}

export async function dictionaryCacheGet(headword: string): Promise<WordPageData | null> {
  const entry = await idbDictionaryCacheGet<WordPageData>(cacheKey(headword))
  if (!entry) return null
  await touchLru(headword)
  return entry
}

export async function dictionaryCacheSet(headword: string, data: WordPageData): Promise<void> {
  await evictIfNeeded(headword)
  await idbDictionaryCacheSet(cacheKey(headword), data)
  await touchLru(headword)
}

export async function dictionaryCacheClear(): Promise<void> {
  const lru = await getLru()
  await Promise.all([...lru.map((w) => idbDictionaryCacheDelete(cacheKey(w))), idbDictionaryCacheDelete(LRU_KEY)])
}
