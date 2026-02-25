import sqlite3 from 'sqlite3';
import * as path from 'path';

const dbPath = path.join(process.env.APPDATA || '', 'io.github.kdroidfilter.seforimapp', 'databases', 'seforim.db');

const db = new sqlite3.Database(dbPath);

const bookId = 6856; // First sample book

db.all(`
    SELECT DISTINCT
        te.id,
        te.parentId,
        te.level,
        te.lineId,
        te.hasChildren,
        tt.text,
        l.lineIndex
    FROM tocEntry AS te
    LEFT JOIN tocText AS tt ON te.textId = tt.id
    LEFT JOIN line AS l ON l.id = te.lineId
    WHERE te.bookId = ?
    ORDER BY te.id
    LIMIT 20
`, [bookId], (err, rows) => {
    if (err) {
        console.error('Error:', err);
        process.exit(1);
    }

    console.log(`TOC entries for book ${bookId}:`);
    console.log(JSON.stringify(rows, null, 2));

    // Also check line 0
    db.get('SELECT id, lineIndex, content FROM line WHERE bookId = ? AND lineIndex = 0', [bookId], (err, row) => {
        if (err) {
            console.error('Error checking line 0:', err);
        } else {
            console.log('\nLine 0:');
            console.log(JSON.stringify(row, null, 2));
        }
        db.close();
    });
});
