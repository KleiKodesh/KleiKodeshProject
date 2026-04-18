"""
export_terms_without_nikud.py
─────────────────────────────
Exports all entry terms that have no nikud row to text files,
split into chunks of ~1000 characters each for pasting into Dicta nakdan.

Output: Misc/scripts/dictionary/terms_without_nikud_*.txt

Usage:
    python Misc/scripts/dictionary/export_terms_without_nikud.py
"""

import sqlite3
import os

DB  = "vue-frontend/public/dictionary/kezayit_dictionary.db"
OUT_DIR = "Misc/scripts/dictionary"
OUT_PREFIX = "terms_without_nikud"

db = sqlite3.connect(DB)

rows = db.execute("""
    SELECT e.term
    FROM entry e
    LEFT JOIN nikud n ON n.term_id = e.id
    WHERE n.id IS NULL
    ORDER BY e.term
""").fetchall()

db.close()

# Split into files of ~1000 chars each
files = []
current_file = []
current_size = 0
chunk_num = 1

for (term,) in rows:
    line = term + "\n"
    line_size = len(line.encode("utf-8"))
    
    # If adding this line would exceed 1000 chars, start a new file
    if current_size + line_size > 1000 and current_file:
        files.append((chunk_num, current_file, current_size))
        current_file = []
        current_size = 0
        chunk_num += 1
    
    current_file.append(line)
    current_size += line_size

# Don't forget the last chunk
if current_file:
    files.append((chunk_num, current_file, current_size))

# Write files
for chunk_num, lines, size in files:
    filename = f"{OUT_PREFIX}_{chunk_num:03d}.txt"
    filepath = os.path.join(OUT_DIR, filename)
    with open(filepath, "w", encoding="utf-8") as f:
        f.writelines(lines)
    print(f"{filename}: {len(lines)} terms, {size} bytes")

print(f"\nTotal: {len(rows):,} terms split into {len(files)} files")
