import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'
import Database from 'better-sqlite3'

// Path to your SQLite database
const DB_PATH = 'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db'

// Ensure externalLibraryId column exists
function ensureExternalLibraryColumn() {
  try {
    const db = new Database(DB_PATH, { readonly: false })

    // Check if column exists
    const columns = db.prepare('PRAGMA table_info(book)').all() as Array<{ name: string }>
    const columnExists = columns.some(c => c.name === 'externalLibraryId')

    if (!columnExists) {
      console.log('[SQLite Plugin] Adding externalLibraryId column to book table...')
      db.prepare('ALTER TABLE book ADD COLUMN externalLibraryId INTEGER DEFAULT NULL').run()
      console.log('[SQLite Plugin] ✓ externalLibraryId column added successfully')
    }

    db.close()
  } catch (error: any) {
    console.warn('[SQLite Plugin] Warning: Could not ensure externalLibraryId column:', error.message)
  }
}

// SQLite Database Plugin for Vite Dev Server
function sqlitePlugin() {
  // Run migration once when plugin is initialized
  ensureExternalLibraryColumn()

  return {
    name: 'vite-plugin-sqlite',
    configureServer(server: any) {
      server.middlewares.use('/__db/query', async (req: any, res: any) => {
        if (req.method !== 'POST') {
          res.statusCode = 405
          res.end('Method Not Allowed')
          return
        }

        let body = ''
        req.on('data', (chunk: any) => body += chunk)
        req.on('end', () => {
          try {
            const { query, params = [] } = JSON.parse(body)

            const db = new Database(DB_PATH, { readonly: true })

            const stmt = db.prepare(query)
            const data = params.length > 0 ? stmt.all(...params) : stmt.all()

            db.close()

            res.setHeader('Content-Type', 'application/json')
            res.end(JSON.stringify({ success: true, data }))
          } catch (error: any) {
            res.setHeader('Content-Type', 'application/json')
            res.end(JSON.stringify({ success: false, error: error.message }))
          }
        })
      })
    }
  }
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    vueDevTools(),
    sqlitePlugin(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    },
  },
  optimizeDeps: {
    exclude: ['canvas', 'path2d-polyfill']
  },
})
