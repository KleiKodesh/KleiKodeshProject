import sqlite3

# Connect to the actual seforim database
db_path = r'C:\Users\Admin\AppData\Roaming\io.github.kdroidfilter.seforimapp\databases\seforim.db'
db = sqlite3.connect(db_path)
cursor = db.cursor()

# Test with a simple word that should exist
test_word = "אב"
book_id = 473

print(f"Testing ספר הערוך lookup for word: {test_word}")
print("=" * 80)

# Try the exact query that the code uses
patterns = [f'%<big>{test_word}</big>%', f'%<big>{test_word} </big>%']

for pattern in patterns:
    print(f"\nPattern: {pattern}")
    cursor.execute(
        "SELECT id, lineIndex, content FROM line WHERE bookId = ? AND content LIKE ? ORDER BY lineIndex LIMIT 20",
        [book_id, pattern]
    )
    rows = cursor.fetchall()
    print(f"Found {len(rows)} rows")
    for row in rows[:5]:
        line_id, line_index, content = row
        print(f"  Line {line_index}: {content[:150]}")

# Also try a broader search to see what's in the book
print("\n" + "=" * 80)
print("Checking all lines with <big> tags:")
cursor.execute(
    "SELECT id, lineIndex, content FROM line WHERE bookId = ? AND content LIKE '%<big>%' ORDER BY lineIndex LIMIT 10",
    [book_id]
)
rows = cursor.fetchall()
print(f"Found {len(rows)} rows with <big> tags")
for row in rows:
    line_id, line_index, content = row
    print(f"  Line {line_index}: {content[:150]}")

db.close()
