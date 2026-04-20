// All SQL strings for the dictionary database (kezayit_dictionary.db).
// No inline SQL anywhere else in the dictionary query layer — import from here.

export const SQL_DICT_EXACT = `
  SELECT w.headword, s.nikud, s.text, sk.name AS source, s.source_id
  FROM word w
  JOIN sense s ON s.word_id = w.id
  LEFT JOIN source_kind sk ON sk.id = s.source_id
  WHERE w.headword = ? LIMIT 100`

export const SQL_DICT_PREFIX = `
  SELECT w.headword, s.nikud, s.text, sk.name AS source, s.source_id
  FROM word w
  JOIN sense s ON s.word_id = w.id
  LEFT JOIN source_kind sk ON sk.id = s.source_id
  WHERE w.headword LIKE ? AND w.headword != ? LIMIT 100`

export const SQL_DICT_CONTAINS = `
  SELECT w.headword, s.nikud, s.text, sk.name AS source, s.source_id
  FROM word w
  JOIN sense s ON s.word_id = w.id
  LEFT JOIN source_kind sk ON sk.id = s.source_id
  WHERE w.headword LIKE ? AND w.headword NOT LIKE ? LIMIT 100`

export const SQL_DICT_EXACT_IN_WORD = `
  SELECT 1 FROM word WHERE headword = ? LIMIT 1`

// kind values: נרדף | כתיב | ראו_גם | ניגוד | נגזרת
export const SQL_DICT_LINKS = `
  SELECT lk.name AS kind, w2.headword AS word
  FROM link l
  JOIN word w1 ON w1.id = l.word_id
  JOIN word w2 ON w2.id = l.target_id
  JOIN link_kind lk ON lk.id = l.kind_id
  WHERE w1.headword = ?
    AND lk.name != 'כתיב'
  ORDER BY lk.name, w2.headword`

export const SQL_DICT_SYNONYMS = `
  SELECT w2.headword AS word
  FROM link l
  JOIN word w1 ON w1.id = l.word_id
  JOIN word w2 ON w2.id = l.target_id
  JOIN link_kind lk ON lk.id = l.kind_id
  WHERE w1.headword = ? AND lk.name = 'נרדף'
  ORDER BY w2.headword`

export const SQL_DICT_VARIANTS = `
  SELECT w2.headword AS word
  FROM link l
  JOIN word w1 ON w1.id = l.word_id
  JOIN word w2 ON w2.id = l.target_id
  JOIN link_kind lk ON lk.id = l.kind_id
  WHERE w1.headword = ? AND lk.name = 'כתיב'
  ORDER BY w2.headword`

export const SQL_DICT_SPELL_CANDIDATES_FRAG2 = `
  SELECT headword FROM word WHERE headword LIKE ? LIMIT 400`

export const SQL_DICT_SPELL_CANDIDATES_FRAG3 = `
  SELECT headword FROM word WHERE headword LIKE ? LIMIT 200`
