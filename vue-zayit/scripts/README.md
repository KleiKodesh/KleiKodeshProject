# Zayit Scripts

Utility scripts for maintaining the Zayit project.

## HebrewBooks CSV Updater

Updates the HebrewBooks catalog by fetching metadata from hebrewbooks.org.

### Features

- Incremental updates (starts from last book ID in CSV)
- Automatic backup before updating
- **Safe append-only approach** - original data never modified until final merge
- New data written to temporary file first
- Respectful rate limiting (1 second between requests)
- Stops after 10 consecutive empty results
- Automatic cleanup on failure

### Setup

```bash
cd scripts
npm install
```

### Usage

```bash
npm run update-hebrewbooks
```

### How It Works

1. Creates a timestamped backup in `backups/` folder
2. Reads the current CSV to find the highest book ID
3. Fetches metadata for each book starting from the next ID
4. **Writes new books to temporary file** `HebrewBooks_new.csv`
5. Stops when 10 consecutive books have no data
6. **Merges temporary file with original CSV** (only if successful)
7. Cleans up temporary file
8. Original CSV is never modified until all data is successfully fetched

### CSV Format

The script maintains the following CSV format:

```
id,title,author,printingPlace,printingYear,pages,tags
```

Example:

```
1,עדת יעקב - חלק א-ב,פיוטרקובסקי - יעקב,ירושלים,תרצד,57,שו"ת
```

### Configuration

Edit `update-hebrewbooks-puppeteer.ts` to adjust:

- `MAX_CONSECUTIVE_EMPTY`: Number of empty results before stopping (default: 10)
- `REQUEST_DELAY_MS`: Delay between requests in milliseconds (default: 1000)

### Backups

Backups are stored in `backups/` with timestamps:

```
backups/HebrewBooks_2026-02-15T12-30-45.csv
```

### Notes

- The script is respectful to hebrewbooks.org with 1-second delays between requests
- If your IP gets blocked, wait before trying again
- Commas in titles/authors are replaced with " -" to maintain CSV integrity
- Tags are semicolon-separated within the tags field
- **Original CSV is never modified during fetching** - only at the final merge step
- If the script fails or is interrupted, your original data remains intact
- Temporary file `HebrewBooks_new.csv` is automatically cleaned up
