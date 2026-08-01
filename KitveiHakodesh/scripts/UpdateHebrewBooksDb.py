#!/usr/bin/env python3
"""
Updates HebrewBooks.db SQLite database directly from beta.hebrewbooks.org
Incrementally scrapes and adds new books, bypassing CSV completely.
Usage: python UpdateHebrewBooksDb.py
"""

import sqlite3
import requests
from html.parser import HTMLParser
from datetime import datetime
from pathlib import Path
import shutil
import time
import sys
import re

# Configuration
PROJECT_ROOT = Path(__file__).parent.parent
DB_PATH = PROJECT_ROOT / "CSharpBackend" / "KitveiHakodeshLib" / "Resources" / "HebrewBooks.db"
BACKUP_DIR = DB_PATH.parent / "backups"

BASE_URL = "https://beta.hebrewbooks.org"
MAX_CONSECUTIVE_EMPTY = 10
REQUEST_DELAY_MS = 1000
# Set to 0 to disable the interval-based restriction (always update)
# No interval-based restriction; always update when run

# User agent to avoid blocking
USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"

class HebrewBooksParser(HTMLParser):
    """Parse HTML from hebrewbooks.org"""
    def __init__(self):
        super().__init__()
        self.data = {}
        self.current_id = None
        self.current_content = []
        self.target_ids = {
            'cpMstr_lblHebSefername': 'title',
            'cpMstr_lblHebAuth': 'author',
            'cpMstr_lblHebPlace': 'place',
            'cpMstr_lblHebDate': 'year',
            'cpMstr_lblPages': 'pages',
        }
    
    def handle_starttag(self, tag, attrs):
        attrs_dict = dict(attrs)
        element_id = attrs_dict.get('id', '')
        
        if element_id in self.target_ids:
            self.current_id = element_id
            self.current_content = []
    
    def handle_data(self, data):
        if self.current_id:
            self.current_content.append(data)
    
    def handle_endtag(self, tag):
        if self.current_id:
            content = ''.join(self.current_content).strip().replace('\n', ' ')
            if content:
                self.data[self.target_ids[self.current_id]] = content
            self.current_id = None
            self.current_content = []

def log(msg):
    """Log with timestamp"""
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    print(f"[{timestamp}] [HbDbUpdater] {msg}", flush=True)

def backup_database():
    """Create database backup"""
    if not DB_PATH.exists():
        return None
    
    BACKUP_DIR.mkdir(parents=True, exist_ok=True)
    backup_path = BACKUP_DIR / f"HebrewBooks_{datetime.now().strftime('%Y-%m-%d_%H-%M-%S')}.db"
    shutil.copy2(DB_PATH, backup_path)
    log(f"Database backed up to: {backup_path}")
    return backup_path

def ensure_schema(conn):
    """Create database schema if needed"""
    cursor = conn.cursor()
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS hebrewBooks (
        id INTEGER PRIMARY KEY,
        title TEXT NOT NULL,
        author TEXT,
        placeOfPublication TEXT,
        year TEXT,
        pageCount INTEGER,
        categories TEXT
    )
    """)
    
    # Create metadata table for tracking
    conn.commit()
    log("Schema verified")

# Interval metadata removed: script always updates when run

def get_max_id_from_db(conn):
    """Get max ID from database"""
    cursor = conn.cursor()
    cursor.execute("SELECT MAX(id) FROM hebrewBooks")
    result = cursor.fetchone()
    return result[0] if result[0] is not None else 0

def extract_tags(html):
    """Extract tags from HTML"""
    tags = []
    tag_matches = re.findall(r'<span[^>]*class=["\']tag["\'][^>]*>([^<]+)</span>', html, re.IGNORECASE)
    return ';'.join(tag_matches) if tag_matches else ""

def fetch_book(book_id, session):
    """Fetch book data from website"""
    try:
        url = f"{BASE_URL}/{book_id}"
        response = session.get(url, timeout=10, verify=False)
        response.raise_for_status()
        response.encoding = 'utf-8'
        
        parser = HebrewBooksParser()
        parser.feed(response.text)
        
        # Check if book has data
        if not parser.data.get('title'):
            return None
        
        # Extract tags from HTML
        parser.data['tags'] = extract_tags(response.text)
        
        return parser.data
        
    except requests.RequestException as e:
        log(f"HTTP error for {book_id}: {e}")
        return None
    except Exception as e:
        log(f"Parse error for {book_id}: {e}")
        return None

def upsert_book(conn, book_id, data):
    """Insert or replace book in database"""
    page_count = None
    if data.get('pages'):
        try:
            page_count = int(data['pages'])
        except ValueError:
            pass
    
    cursor = conn.cursor()
    cursor.execute("""
    INSERT OR REPLACE INTO hebrewBooks 
    (id, title, author, placeOfPublication, year, pageCount, categories)
    VALUES (?, ?, ?, ?, ?, ?, ?)
    """, (
        book_id,
        data.get('title', ''),
        data.get('author'),
        data.get('place'),
        data.get('year'),
        page_count,
        data.get('tags')
    ))

def run_update():
    """Main update logic"""
    log(f"Database: {DB_PATH}")
    log(f"Source: {BASE_URL}")
    
    # No interval check; always proceed with update
    
    backup_path = backup_database()
    
    try:
        conn = sqlite3.connect(str(DB_PATH))
        ensure_schema(conn)
        
        start_id = get_max_id_from_db(conn) + 1
        current_id = start_id
        consecutive_empty = 0
        added = 0
        
        log(f"Starting from ID {start_id}")
        
        session = requests.Session()
        session.headers.update({'User-Agent': USER_AGENT})
        
        while consecutive_empty < MAX_CONSECUTIVE_EMPTY:
            book_data = fetch_book(current_id, session)
            
            if book_data:
                upsert_book(conn, current_id, book_data)
                added += 1
                consecutive_empty = 0
                log(f"+ {current_id} {book_data.get('title', 'Unknown')[:60]}")
            else:
                consecutive_empty += 1
                log(f"- {current_id} (empty {consecutive_empty}/{MAX_CONSECUTIVE_EMPTY})")
            
            current_id += 1
            time.sleep(REQUEST_DELAY_MS / 1000.0)
        
        # Previously recorded last-update metadata removed
        conn.commit()
        conn.close()
        
        log(f"Done. Added {added} books. Last ID checked: {current_id - 1}")
        return True
        
    except Exception as e:
        log(f"ERROR: {e}")
        if backup_path and backup_path.exists():
            try:
                shutil.copy2(backup_path, DB_PATH)
                log("Database restored from backup")
            except:
                pass
        return False

if __name__ == "__main__":
    success = run_update()
    sys.exit(0 if success else 1)
