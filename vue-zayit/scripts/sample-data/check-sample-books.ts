import sqlite3 from 'sqlite3';
import * as path from 'path';

const dbPath = path.join(process.env.APPDATA || '', 'io.github.kdroidfilter.seforimapp', 'databases', 'seforim.db');

const db = new sqlite3.Database(dbPath);

db.all('SELECT Id, Title, HeShortDesc FROM book WHERE Title LIKE "%דוגמה%" ORDER BY Id DESC LIMIT 5', [], (err, rows) => {
    if (err) {
        console.error('Error:', err);
        process.exit(1);
    }

    console.log('Sample books in database:');
    console.log(JSON.stringify(rows, null, 2));

    db.close();
});
