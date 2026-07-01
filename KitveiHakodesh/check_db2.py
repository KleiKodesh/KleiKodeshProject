import sqlite3

conn = sqlite3.connect(r'C:\ProgramData\otzaria\books\seforim.db')
cur = conn.cursor()

print("=== link table FULL schema ===")
cur.execute("PRAGMA table_info(link)")
for r in cur.fetchall():
    print(r)

print("\n=== line_toc FULL schema ===")
cur.execute("PRAGMA table_info(line_toc)")
for r in cur.fetchall():
    print(r)

print("\n=== link row count ===")
cur.execute("SELECT COUNT(*) FROM link")
print(cur.fetchone())

print("\n=== line_toc row count ===")
cur.execute("SELECT COUNT(*) FROM line_toc")
print(cur.fetchone())

# Test the exact _EnsureIndexes query
print("\n=== _EnsureIndexes test ===")
try:
    cur.execute("CREATE INDEX IF NOT EXISTS idx_link_type_target_line ON link(connectionTypeId, targetLineId)")
    print("_EnsureIndexes: OK (index created or already exists)")
except Exception as e:
    print("_EnsureIndexes ERROR:", e)

# Test all commentary queries from commentaryGroupBuilder
print("\n=== commentaryGroupBuilder link query test (sourceLineId-based) ===")
try:
    cur.execute("""
        SELECT l.id, l.sourceBookId, l.targetBookId, l.sourceLineId, l.targetLineId,
               l.connectionTypeId
        FROM link l
        WHERE l.sourceLineId = ?
        LIMIT 5
    """, (1,))
    print("sourceLineId link query: OK, rows:", len(cur.fetchall()))
except Exception as e:
    print("sourceLineId link query ERROR:", e)

print("\n=== commentaryGroupBuilder reverse link query test (targetLineId-based) ===")
try:
    cur.execute("""
        SELECT l.sourceBookId, l.sourceLineId, l.connectionTypeId
        FROM link l
        WHERE l.connectionTypeId = ? AND l.targetLineId = ?
        LIMIT 5
    """, (1, 1))
    print("targetLineId link query: OK, rows:", len(cur.fetchall()))
except Exception as e:
    print("targetLineId link query ERROR:", e)

print("\n=== connection_type schema ===")
cur.execute("PRAGMA table_info(connection_type)")
for r in cur.fetchall():
    print(r)

print("\n=== connection_type data ===")
cur.execute("SELECT * FROM connection_type")
for r in cur.fetchall():
    print(r)

conn.close()
