import sqlite3 from 'sqlite3';
import * as path from 'path';

const dbPath = path.join(process.env.APPDATA || '', 'io.github.kdroidfilter.seforimapp', 'databases', 'seforim.db');

const db = new sqlite3.Database(dbPath);

db.serialize(() => {
    db.run('BEGIN TRANSACTION');

    // Find and delete sample category and all its books
    db.get('SELECT Id FROM category WHERE Title = ?', ['קטגוריית דוגמה'], (err, row: any) => {
        if (err) {
            console.error('Error finding category:', err);
            db.run('ROLLBACK');
            db.close();
            return;
        }

        if (!row) {
            console.log('Sample category not found');
            db.run('ROLLBACK');
            db.close();
            return;
        }

        const categoryId = row.id;
        console.log(`Found sample category ID: ${categoryId}`);

        // Get all books in this category
        db.all('SELECT Id, Title FROM book WHERE CategoryId = ?', [categoryId], (err, books: any[]) => {
            if (err) {
                console.error('Error finding books:', err);
                db.run('ROLLBACK');
                db.close();
                return;
            }

            console.log(`Found ${books.length} books to delete`);

            books.forEach(book => {
                console.log(`  Deleting book: ${book.title} (ID: ${book.id})`);

                // Delete in correct order to respect foreign keys
                db.run('DELETE FROM line_toc WHERE lineId IN (SELECT id FROM line WHERE bookId = ?)', [book.id]);
                db.run('DELETE FROM tocEntry WHERE bookId = ?', [book.id]);
                db.run('DELETE FROM line WHERE bookId = ?', [book.id]);
                db.run('DELETE FROM book WHERE Id = ?', [book.id]);
            });

            // Delete category
            db.run('DELETE FROM category WHERE Id = ?', [categoryId], (err) => {
                if (err) {
                    console.error('Error deleting category:', err);
                    db.run('ROLLBACK');
                } else {
                    db.run('COMMIT', (err) => {
                        if (err) {
                            console.error('Error committing:', err);
                        } else {
                            console.log('\n✓ Sample data deleted successfully!');
                        }
                        db.close();
                    });
                }
            });
        });
    });
});
