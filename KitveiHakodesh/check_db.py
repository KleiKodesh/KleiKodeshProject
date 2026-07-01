import sqlite3

conn = sqlite3.connect(r'C:\ProgramData\otzaria\books\seforim.db')
cur = conn.cursor()

# Check the specific index the app tries to create in _EnsureIndexes
print("=== existing indexes on link table ===")
cur.execute("PRAGMA index_list(link)")
for r in cur.fetchall():
    print(r)

print("\n=== link table schema ===")
cur.execute("PRAGMA table_info(link)")
for r in cur.fetchall():
    print(r)

# Check what the FTS index builder needs - bloom_metadata
print("\n=== bloom_metadata? ===")
cur.execute("SELECT name FROM sqlite_master WHERE name='bloom_metadata'")
print("exists:", cur.fetchone() is not None)

# Check the tocEntry table - important for book opening
print("\n=== tocEntry schema ===")
cur.execute("PRAGMA table_info(tocEntry)")
for r in cur.fetchall():
    print(r)

# Check if isLastChild column exists (used in GET_ALL_TOC_ENTRIES?)  
print("\n=== GET_ALL_TOC_ENTRIES test (bookId=1) ===")
try:
    cur.execute("""
        SELECT te.id, te.parentId, te.level, te.lineId, te.hasChildren,
               tt.text, l.lineIndex
        FROM tocEntry te
        JOIN tocText tt ON tt.id = te.textId
        LEFT JOIN line l ON l.id = te.lineId
        WHERE te.bookId = ?
        ORDER BY te.id
        LIMIT 5
    """, (1,))
    for r in cur.fetchall():
        print(r)
    print("GET_ALL_TOC_ENTRIES: OK")
except Exception as e:
    print("GET_ALL_TOC_ENTRIES ERROR:", e)

# Check the line_toc table
print("\n=== line_toc schema ===")
cur.execute("PRAGMA table_info(line_toc)")
for r in cur.fetchall():
    print(r)

# Check default_commentator
print("\n=== default_commentator schema ===")
cur.execute("PRAGMA table_info(default_commentator)")
for r in cur.fetchall():
    print(r)

# Check book_acronym
print("\n=== book_acronym schema ===")
cur.execute("PRAGMA table_info(book_acronym)")
for r in cur.fetchall():
    print(r)

# Check if any critical index exists
print("\n=== all indexes ===")
cur.execute("SELECT name, tbl_name FROM sqlite_master WHERE type='index' ORDER BY tbl_name, name")
for r in cur.fetchall():
    print(r)

conn.close()
