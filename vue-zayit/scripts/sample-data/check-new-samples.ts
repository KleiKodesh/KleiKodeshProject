import sqlite3 from 'sqlite3';
import * as path from 'path';

const dbPath = path.join(process.env.APPDATA || '', 'io.github.kdroidfilter.seforimapp', 'databases', 'seforim.db');

const db = new sqlite3.Database(dbPath);

const bookId = 6858;

db.serialize(() => {
    // Check book
    db.get('SELECT * FROM book WHERE id = ?', [bookId], (err, book) => {
        if (err) console.error('Error:', err);
        console.log('Book:', JSON.stringify(book, null, 2));
    });

    // Check line count
    db.get('SELECT COUNT(*) as count FROM line WHERE bookId = ?', [bookId], (err, result: any) => {
        if (err) console.error('Error:', err);
        console.log(`\nLine count: ${result?.count}`);
    });

    // Check first few lines
    db.all('SELECT id, lineIndex, content FROM line WHERE bookId = ? ORDER BY lineIndex LIMIT 5', [bookId], (err, lines) => {
        if (err) console.error('Error:', err);
        console.log('\nFirst 5 lines:', JSON.stringify(lines, null, 2));
    });

    // Check TOC count
    db.get('SELECT COUNT(*) as count FROM tocEntry WHERE bookId = ?', [bookId], (err, result: any) => {
        if (err) console.error('Error:', err);
        console.log(`\nTOC entry count: ${result?.count}`);
    });

    // Check TOC entries
    db.all(`
        SELECT te.id, te.parentId, te.level, tt.text, l.lineIndex
        FROM tocEntry te
        LEFT JOIN tocText tt ON te.textId = tt.id
        LEFT JOIN line l ON l.id = te.lineId
        WHERE te.bookId = ?
        ORDER BY te.id
        LIMIT 10
    `, [bookId], (err, tocs) => {
        if (err) console.error('Error:', err);
        console.log('\nTOC entries:', JSON.stringify(tocs, null, 2));

        setTimeout(() => db.close(), 100);
    });
});
