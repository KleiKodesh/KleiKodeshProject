import sqlite3

db_path = r'C:\Users\Admin\AppData\Roaming\io.github.kdroidfilter.seforimapp\databases\seforim.db'
db = sqlite3.connect(db_path)
cursor = db.cursor()

# Find the book
cursor.execute("SELECT id, title FROM book WHERE title LIKE '%שרשים%' OR title LIKE '%רד%ק%'")
books = cursor.fetchall()
print("Matching books:")
for b in books:
    print(f"  ID: {b[0]}, Title: {b[1]}")

print()

# Focus on ספר השרשים
cursor.execute("SELECT id, title FROM book WHERE title LIKE '%שרשים%'")
books = cursor.fetchall()
for book_id, book_title in books:
    print(f"\n=== {book_title} (ID: {book_id}) ===")
    cursor.execute("SELECT id, lineIndex, content FROM line WHERE bookId = ? ORDER BY lineIndex LIMIT 60", [book_id])
    lines = cursor.fetchall()
    for line in lines:
        print(f"  Line {line[1]}: {repr(line[2][:150])}")

db.close()
