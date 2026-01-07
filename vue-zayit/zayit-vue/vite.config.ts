import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'
import Database from 'better-sqlite3'

// SQLite Database Plugin for Vite Dev Server
function sqlitePlugin() {
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

            // Path to your SQLite database
            const dbPath = 'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db'
            const db = new Database(dbPath, { readonly: true })

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
