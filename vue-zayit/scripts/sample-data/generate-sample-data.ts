import sqlite3 from 'sqlite3';
import * as path from 'path';
import * as fs from 'fs';

// Hebrew letters for generating random content
const hebrewLetters = 'אבגדהוזחטיכלמנסעפצקרשת';

function generateRandomHebrewWord(): string {
    const length = Math.floor(Math.random() * 5) + 2; // 2-6 letters per word
    let word = '';
    for (let i = 0; i < length; i++) {
        word += hebrewLetters[Math.floor(Math.random() * hebrewLetters.length)];
    }
    return word;
}

function generateHebrewLine(): string {
    const wordCount = Math.floor(Math.random() * 8) + 5; // 5-12 words per line
    const words: string[] = [];
    for (let i = 0; i < wordCount; i++) {
        words.push(generateRandomHebrewWord());
    }
    return words.join(' ');
}

interface SampleBook {
    title: string;
    shortDesc: string;
    lineCount: number;
    tocStructure: {
        title: string;
        children?: {
            title: string;
            children?: { title: string }[];
        }[];
    }[];
}

const sampleBooks: SampleBook[] = [
    {
        title: 'ספר הדוגמה הראשון',
        shortDesc: 'ספר לדוגמה עם תוכן עברי',
        lineCount: 1000,
        tocStructure: [
            {
                title: 'ספר הדוגמה הראשון', // Root TOC matches book title
                children: [
                    {
                        title: 'פרק א',
                        children: [
                            { title: 'סעיף א' },
                            { title: 'סעיף ב' },
                            { title: 'סעיף ג' }
                        ]
                    },
                    {
                        title: 'פרק ב',
                        children: [
                            { title: 'סעיף א' },
                            { title: 'סעיף ב' }
                        ]
                    },
                    {
                        title: 'פרק ג',
                        children: [
                            { title: 'סעיף א' },
                            { title: 'סעיף ב' },
                            { title: 'סעיף ג' },
                            { title: 'סעיף ד' }
                        ]
                    }
                ]
            }
        ]
    },
    {
        title: 'ספר הדוגמה השני',
        shortDesc: 'עוד ספר לבדיקות',
        lineCount: 1500,
        tocStructure: [
            {
                title: 'חלק ראשון', // Root TOC does NOT match book title
                children: [
                    {
                        title: 'פרק א',
                        children: [
                            { title: 'סעיף א' },
                            { title: 'סעיף ב' }
                        ]
                    },
                    {
                        title: 'פרק ב',
                        children: [
                            { title: 'סעיף א' },
                            { title: 'סעיף ב' },
                            { title: 'סעיף ג' }
                        ]
                    }
                ]
            },
            {
                title: 'חלק שני',
                children: [
                    {
                        title: 'פרק א',
                        children: [
                            { title: 'סעיף א' }
                        ]
                    }
                ]
            }
        ]
    }
];

function findDatabasePath(): string {
    const possiblePaths = [
        path.join(process.env.APPDATA || '', 'io.github.kdroidfilter.seforimapp', 'databases', 'seforim.db'),
        path.join(process.env.APPDATA || '', 'Zayit', 'zayit.db'),
        path.join(process.env.LOCALAPPDATA || '', 'Zayit', 'zayit.db'),
        path.join(process.env.USERPROFILE || '', 'AppData', 'Roaming', 'Zayit', 'zayit.db'),
    ];

    for (const dbPath of possiblePaths) {
        if (fs.existsSync(dbPath)) {
            return dbPath;
        }
    }

    throw new Error('Database not found. Please provide the path as an argument.');
}

async function generateSampleData(dbPath: string): Promise<void> {
    console.log(`Opening database: ${dbPath}`);

    const db = new sqlite3.Database(dbPath);

    return new Promise((resolve, reject) => {
        db.serialize(() => {
            try {
                db.run('BEGIN TRANSACTION');

                // Create sample category
                const categoryTitle = 'קטגוריית דוגמה';
                db.run('INSERT INTO category (ParentId, Title, OrderIndex) VALUES (NULL, ?, 999)', [categoryTitle], function (err) {
                    if (err) throw err;

                    const categoryId = this.lastID;
                    console.log(`Created category: ${categoryTitle} (ID: ${categoryId})`);

                    let bookIndex = 0;

                    function processNextBook() {
                        if (bookIndex >= sampleBooks.length) {
                            db.run('COMMIT', (err) => {
                                if (err) {
                                    reject(err);
                                } else {
                                    console.log('\n✓ Sample data generated successfully!');
                                    console.log(`\nCategory ID: ${categoryId}`);
                                    console.log(`Books created: ${sampleBooks.length}`);
                                    db.close();
                                    resolve();
                                }
                            });
                            return;
                        }

                        const bookDef = sampleBooks[bookIndex];
                        console.log(`\nGenerating book: ${bookDef.title}`);

                        // Insert book
                        db.run(`
                            INSERT INTO book (
                                CategoryId, Title, HeShortDesc, OrderIndex, TotalLines, sourceId,
                                HasTargumConnection, HasReferenceConnection, HasCommentaryConnection,
                                HasOtherConnection, HasSourceConnection
                            )
                            VALUES (?, ?, ?, 999, ?, 0, 0, 0, 0, 0, 0)
                        `, [categoryId, bookDef.title, bookDef.shortDesc, bookDef.lineCount], function (err) {
                            if (err) throw err;

                            const bookId = this.lastID;
                            console.log(`  Created book ID: ${bookId}`);

                            // Generate lines
                            console.log(`  Generating ${bookDef.lineCount} lines...`);

                            const insertLineStmt = db.prepare('INSERT INTO line (bookId, lineIndex, content) VALUES (?, ?, ?)');

                            for (let i = 0; i < bookDef.lineCount; i++) {
                                const content = generateHebrewLine();
                                insertLineStmt.run(bookId, i, content);

                                if ((i + 1) % 100 === 0) {
                                    process.stdout.write(`\r  Progress: ${i + 1}/${bookDef.lineCount}`);
                                }
                            }

                            insertLineStmt.finalize((err) => {
                                if (err) throw err;
                                console.log(`\r  Progress: ${bookDef.lineCount}/${bookDef.lineCount} ✓`);

                                // Generate TOC
                                console.log(`  Generating table of contents...`);
                                generateTocForBook();
                            });

                            function generateTocForBook() {
                                let currentLineIndex = 0;
                                const linesPerSection = Math.floor(bookDef.lineCount / getTotalSections(bookDef.tocStructure));

                                function getTotalSections(structure: any[]): number {
                                    let count = 0;
                                    for (const item of structure) {
                                        count++;
                                        if (item.children) {
                                            count += getTotalSections(item.children);
                                        }
                                    }
                                    return count;
                                }

                                function insertTocEntry(
                                    parentId: number | null,
                                    level: number,
                                    text: string,
                                    lineIndex: number,
                                    hasChildren: boolean,
                                    callback: (tocEntryId: number) => void
                                ) {
                                    // Get or create tocText
                                    db.get('SELECT id FROM tocText WHERE text = ?', [text], (err, row: any) => {
                                        if (err) throw err;

                                        if (row) {
                                            processWithTextId(row.id);
                                        } else {
                                            db.run('INSERT INTO tocText (text) VALUES (?)', [text], function (err) {
                                                if (err) throw err;
                                                processWithTextId(this.lastID);
                                            });
                                        }
                                    });

                                    function processWithTextId(textId: number) {
                                        // Get lineId
                                        db.get('SELECT id FROM line WHERE bookId = ? AND lineIndex = ?', [bookId, lineIndex], (err, row: any) => {
                                            if (err) throw err;
                                            if (!row) throw new Error(`Line not found: bookId=${bookId}, lineIndex=${lineIndex}`);

                                            const lineId = row.id;

                                            // Insert tocEntry
                                            db.run(`
                                            INSERT INTO tocEntry (bookId, parentId, level, textId, lineId, hasChildren)
                                            VALUES (?, ?, ?, ?, ?, ?)
                                        `, [bookId, parentId, level, textId, lineId, hasChildren ? 1 : 0], function (err) {
                                                if (err) throw err;

                                                const tocEntryId = this.lastID;

                                                // Link line to toc
                                                db.run('INSERT INTO line_toc (lineId, tocEntryId) VALUES (?, ?)', [lineId, tocEntryId], (err) => {
                                                    if (err) throw err;
                                                    callback(tocEntryId);
                                                });
                                            });
                                        });
                                    }
                                }

                                function processTocLevel(items: any[], parentId: number | null, level: number, callback: () => void) {
                                    let itemIndex = 0;

                                    function processNextItem() {
                                        if (itemIndex >= items.length) {
                                            callback();
                                            return;
                                        }

                                        const item = items[itemIndex];
                                        const hasChildren = !!item.children && item.children.length > 0;

                                        insertTocEntry(parentId, level, item.title, currentLineIndex, hasChildren, (tocEntryId) => {
                                            currentLineIndex += linesPerSection;
                                            if (currentLineIndex >= bookDef.lineCount) {
                                                currentLineIndex = bookDef.lineCount - 1;
                                            }

                                            if (item.children) {
                                                processTocLevel(item.children, tocEntryId, level + 1, () => {
                                                    itemIndex++;
                                                    processNextItem();
                                                });
                                            } else {
                                                itemIndex++;
                                                processNextItem();
                                            }
                                        });
                                    }

                                    processNextItem();
                                }

                                processTocLevel(bookDef.tocStructure, null, 0, () => {
                                    console.log(`  TOC created ✓`);
                                    bookIndex++;
                                    processNextBook();
                                });
                            }
                        });
                    }

                    processNextBook();
                });

            } catch (error) {
                db.run('ROLLBACK');
                console.error('\n✗ Error generating sample data:', error);
                db.close();
                reject(error);
            }
        });
    });
}

// Main execution
const args = process.argv.slice(2);
const dbPath = args[0] || findDatabasePath();

generateSampleData(dbPath).catch(error => {
    console.error('Failed:', error);
    process.exit(1);
});
