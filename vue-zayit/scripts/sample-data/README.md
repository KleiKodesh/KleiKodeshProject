# Sample Data Scripts

Scripts for generating and managing sample book data for testing.

## Available Scripts

Run from the `scripts` folder:

### Generate Sample Books

```bash
npm run generate-sample
```

Creates two sample books in the database:

- **ספר הדוגמה הראשון** (1000 lines) - Has root TOC matching book title
- **ספר הדוגמה השני** (1500 lines) - Has root TOC different from book title

### Delete Sample Books

```bash
npm run delete-sample
```

Removes all sample books and the sample category from the database.

### Check Sample Books

```bash
npm run check-sample
```

Displays information about the generated sample books (lines, TOC entries).

### Check TOC Structure

```bash
npm run check-toc
```

Shows TOC entries for a specific book.

### Check Root TOC

```bash
npm run check-root-toc
```

Compares root TOC entries across different books.

### Find Sample Category

```bash
npm run find-sample-category
```

Locates the sample category in the database.

## Sample Book Structure

### Book 1: ספר הדוגמה הראשון

- Root TOC: "ספר הדוגמה הראשון" (matches book title)
- Structure:
  - ספר הדוגמה הראשון (root)
    - פרק א
      - סעיף א, ב, ג
    - פרק ב
      - סעיף א, ב
    - פרק ג
      - סעיף א, ב, ג, ד

### Book 2: ספר הדוגמה השני

- Root TOC: "חלק ראשון" (different from book title)
- Structure:
  - חלק ראשון
    - פרק א
      - סעיף א, ב
    - פרק ב
      - סעיף א, ב, ג
  - חלק שני
    - פרק א
      - סעיף א

## Content

All lines contain random Hebrew letter combinations (gibberish) to avoid using actual holy text.
