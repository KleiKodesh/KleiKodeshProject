import sqlite3 from 'sqlite3';
import * as path from 'path';

const dbPath = path.join(process.env.APPDATA || '', 'io.github.kdroidfilter.seforimapp', 'databases', 'seforim.db');

const db = new sqlite3.Database(dbPath);

// Check a few different books
const bookIds = [1, 2, 3, 6856]; // Regular books + sample book

db.serialize(() => {
    bookIds.forEach(bookId => {
        db.get('SELECT Id, Title FROM book WHERE Id = ?', [bookId], (err, book: any) => {
            if (err || !book) return;

            db.all(`
                SELECT te.id, te.parentId, te.level, tt.text, l.lineIndex
                FROM tocEntry te
                LEFT JOIN tocText tt ON te.textId = tt.id
                LEFT JOIN line l ON l.id = te.lineId
                WHERE te.bookId = ? AND te.level = 0 AND (te.parentId IS NULL OR te.parentId = 0)
                ORDER BY te.id
                LIMIT 5
            `, [bookId], (err, tocs) => {
                if (err) return;

                console.log(`\n=== Book ${bookId}: "${book.Title}" ===`);
                console.log('Root TOC entries:');
                console.log(JSON.stringify(tocs, null, 2));
            });
        });
    });

    setTimeout(() => db.close(), 2000);
});
