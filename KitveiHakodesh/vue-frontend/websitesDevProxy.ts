import http from 'node:http'
import fs from 'node:fs'
import type { Plugin } from 'vite'
import { SITE_FRAME_ACTIVITY_SCRIPT, SITE_FRAME_KEY_FORWARDER } from './src/features/websites/siteFrameShortcutKeys.js'

/**
 * DEV ONLY: the browser stand-in for the host's WebSitesFrameHeaderStripper.cs.
 *
 * Hosted, the host strips X-Frame-Options / CSP frame-ancestors so a whitelisted site
 * (hameshiv, responsa, tashma...) loads in the web-view iframe. A plain dev browser cannot
 * do that, so this runs a small proxy on (vite port + 1000) that does it instead:
 *
 *   http://<site host>.localhost:<proxy port>/<path>  ->  https://<site host>/<path>
 *
 * Chromium resolves every *.localhost to loopback, so each site keeps its own origin and its
 * root-relative links ("/static/x.js", "/api/...") stay on the proxy. Only hosts under a
 * domain listed in public/websites.json are proxied. Redirects and cookies are rewritten to
 * stay on the proxy. WebViewPage.vue builds these addresses.
 *
 * HTML pages get the app's key forwarder injected (siteFrameShortcutKeys.ts), so the app's
 * shortcuts keep working while a site has focus - the dev version of AppViewerSiteShortcuts.cs.
 */
export function websitesDevProxy(): Plugin {
  return {
    name: 'websites-dev-proxy',
    apply: 'serve',
    configureServer(server) {
      const listPath = new URL('./public/websites.json', import.meta.url)
      const allowedDomains = (): string[] => {
        try {
          const data = JSON.parse(fs.readFileSync(listPath, 'utf8'))
          return [
            ...data.sites.flatMap((s: { domains: string[] }) => s.domains),
            ...(data.loginDomains ?? []),
          ].map((d: string) => d.toLowerCase())
        } catch {
          return []
        }
      }

      let proxy: http.Server | null = null
      server.httpServer?.once('listening', () => {
        const address = server.httpServer!.address()
        const vitePort = typeof address === 'object' && address ? address.port : 5173
        const proxyPort = vitePort + 1000
        const suffix = `.localhost:${proxyPort}`

        proxy = http.createServer(async (req, res) => {
          const hostHeader = (req.headers.host ?? '').toLowerCase()
          const siteHost = hostHeader.endsWith(suffix) ? hostHeader.slice(0, -suffix.length) : ''
          if (!siteHost || !allowedDomains().some((d) => siteHost === d || siteHost.endsWith(`.${d}`))) {
            res.writeHead(403).end('not a whitelisted website')
            return
          }
          try {
            const chunks: Buffer[] = []
            for await (const chunk of req) chunks.push(chunk as Buffer)
            const headers: Record<string, string> = {}
            for (const [k, v] of Object.entries(req.headers)) {
              if (typeof v !== 'string' || ['host', 'origin', 'referer', 'accept-encoding', 'connection'].includes(k)) continue
              headers[k] = v
            }
            if (req.headers.origin) headers.origin = `https://${siteHost}`
            if (req.headers.referer) headers.referer = req.headers.referer.replace(`http://${hostHeader}`, `https://${siteHost}`)
            const upstream = await fetch(`https://${siteHost}${req.url ?? '/'}`, {
              method: req.method,
              headers,
              body: chunks.length && req.method !== 'GET' && req.method !== 'HEAD' ? Buffer.concat(chunks) : undefined,
              redirect: 'manual',
            })
            const out: Record<string, string | string[]> = {}
            upstream.headers.forEach((value, name) => {
              if (['x-frame-options', 'content-encoding', 'content-length', 'transfer-encoding', 'set-cookie', 'strict-transport-security'].includes(name)) return
              if (name === 'content-security-policy') value = value.split(';').filter((p) => !/^\s*frame-ancestors/i.test(p)).join(';')
              if (name === 'location') value = value.replace(/^https:\/\/([^/]+)/i, (_, h) => `http://${h}${suffix}`)
              out[name] = value
            })
            // Cookies: drop Secure/SameSite/Domain so the browser keeps them on the http proxy origin.
            const cookies = upstream.headers.getSetCookie?.() ?? []
            if (cookies.length) out['set-cookie'] = cookies.map((c) => c.replace(/;\s*(secure|samesite=[a-z]+|domain=[^;]+)/gi, ''))
            let body = Buffer.from(await upstream.arrayBuffer())
            if ((upstream.headers.get('content-type') ?? '').includes('text/html')) {
              // latin1 round-trips every byte, so a windows-1255 page is not damaged (the tag is ASCII).
              const html = body.toString('latin1')
              const at = html.search(/<head[^>]*>/i)
              const tag = `<script>${SITE_FRAME_KEY_FORWARDER}
${SITE_FRAME_ACTIVITY_SCRIPT}</script>`
              body = Buffer.from(at >= 0 ? html.replace(/<head[^>]*>/i, (m) => m + tag) : tag + html, 'latin1')
            }
            res.writeHead(upstream.status, out)
            res.end(body)
          } catch (e) {
            res.writeHead(502).end(String(e))
          }
        })
        proxy.on('error', (e) => server.config.logger.warn(`[websites-dev-proxy] ${e.message}`))
        proxy.listen(proxyPort, '127.0.0.1')
      })
      server.httpServer?.once('close', () => proxy?.close())
    },
  }
}
