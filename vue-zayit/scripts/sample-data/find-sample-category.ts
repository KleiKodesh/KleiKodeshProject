import sqlite3 from 'sqlite3';
import * as path from 'path';

const dbPath = path.join(process.env.APPDATA || '', 'io.github.kdroidfilter.seforimapp', 'databases', 'seforim.db');

const db = new sqlite3.Database(dbPath);

db.all('SELECT * FROM category WHERE Title LIKE "%דוגמה%" LIMIT 5', [], (err, rows) => {
    if (err) {
        console.error('Error:', err);
    } else {
        console.log('Sample categories:');
        console.log(JSON.stringify(rows, null, 2));
    }
    db.close();
});
