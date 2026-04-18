import sqlite3
db = sqlite3.connect('vue-frontend/public/dictionary/kezayit_dictionary.db')

# Words added by seforim scan that are NOT in the original dictionary headwords
rows = db.execute("""
    SELECT e.headword, e.definition
    FROM entry e
    WHERE e.source_id = 5
      AND e.headword NOT IN (
          SELECT DISTINCT headword FROM entry WHERE source_id != 5
      )
    ORDER BY length(e.headword), e.headword
    LIMIT 40
""").fetchall()

print(f"New words from seforim scan: {len(rows)} samples")
for hw, defn in rows:
    print(f"  '{hw}' → {defn[:100]}")

db.close()
