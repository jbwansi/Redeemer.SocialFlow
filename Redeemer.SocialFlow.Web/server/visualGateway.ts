import type { IncomingMessage, ServerResponse } from 'node:http'
import { request as httpsRequest } from 'node:https'
import type { Plugin } from 'vite'

// This module is loaded by Vite's server process only. Never import it from src/.
const prefix = '/local-visuals'
const guid = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'
const route = new RegExp(
  '^/posts/(' + guid + ')/visuals(?:/(upload|generate|' + guid + ')(/select)?)?$',
)
export function allowedRoute(path: string, method: string): boolean {
  const match = route.exec(path)
  if (!match) return false
  const tail = match[2]
  return !tail
    ? method === 'GET'
    : tail === 'upload' || tail === 'generate'
      ? !match[3] && method === 'POST'
      : match[3]
        ? method === 'POST'
        : method === 'GET' || method === 'DELETE'
}
export function isLocalRequest(req: IncomingMessage): boolean {
  const host = req.headers.host
  if (host !== '127.0.0.1:5173' && host !== 'localhost:5173') return false
  if (!['127.0.0.1', '::1', '::ffff:127.0.0.1'].includes(req.socket.remoteAddress || ''))
    return false
  if (Object.keys(req.headers).some((k) => k === 'forwarded' || k.startsWith('x-forwarded-')))
    return false
  if (req.headers['x-socialflow-local'] !== '1') return false
  const site = req.headers['sec-fetch-site']
  if (site && site !== 'same-origin') return false
  const expected = 'http://' + host
  if (req.headers.origin && req.headers.origin !== expected) return false
  // Origin plus a non-simple header prevents CSRF; no CORS responses are emitted.
  if (req.method !== 'GET' && req.headers.origin !== expected) return false
  return true
}
function reply(res: ServerResponse, status: number, title: string) {
  if (res.headersSent) {
    res.destroy()
    return
  }
  res.writeHead(status, { 'Content-Type': 'application/problem+json', 'Cache-Control': 'no-store' })
  res.end(JSON.stringify({ status, title }))
}
export function visualGateway(enabled: boolean, secret: string | undefined): Plugin {
  return {
    name: 'local-visual-gateway',
    apply: 'serve',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        if (!req.url?.startsWith(prefix)) return next()
        if (!enabled) return reply(res, 404, 'Visual management is disabled')
        if (!isLocalRequest(req)) return reply(res, 403, 'Local visual access denied')
        if (!secret || secret.length < 32 || secret.length > 256 || !secret.trim())
          return reply(res, 503, 'Visual gateway is not configured')
        const path = req.url.slice(prefix.length)
        if (path === '/status' && req.method === 'GET') {
          res.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' })
          res.end('{"enabled":true}')
          return
        }
        if (!allowedRoute(path, req.method || '')) return reply(res, 404, 'Visual route not found')
        const maximum = path.endsWith('/generate') ? 64 * 1024 : 21 * 1024 * 1024
        const length = req.headers['content-length']
        if (length && (!/^\d+$/.test(length) || Number(length) > maximum))
          return reply(res, 413, 'Image or request is too large')
        const contentType = req.headers['content-type'] || ''
        if (
          req.method === 'POST' &&
          path.endsWith('/upload') &&
          !contentType.startsWith('multipart/form-data;')
        )
          return reply(res, 415, 'A PNG or JPEG upload is required')
        if (
          req.method === 'POST' &&
          path.endsWith('/generate') &&
          !contentType.startsWith('application/json')
        )
          return reply(res, 415, 'JSON is required')
        // Fixed IP prevents DNS/proxy redirection. SNI and certificate verification target localhost.
        // NODE_EXTRA_CA_CERTS may contain the trusted local development CA/certificate.
        const upstream = httpsRequest(
          {
            hostname: '127.0.0.1',
            port: 65474,
            servername: 'localhost',
            rejectUnauthorized: true,
            method: req.method,
            path: '/api' + path,
            headers: {
              Host: 'localhost:65474',
              'X-SocialFlow-Visual-Admin': secret,
              Accept: 'application/json, application/problem+json, image/png, image/jpeg',
              ...(contentType ? { 'Content-Type': contentType } : {}),
              ...(length ? { 'Content-Length': length } : {}),
            },
          },
          (incoming) => {
            const status = incoming.statusCode || 502
            // Never relay redirects, private paths or arbitrary upstream headers.
            if (status >= 300 && status < 400) {
              incoming.destroy()
              reply(res, 502, 'Unexpected API redirect')
              return
            }
            res.writeHead(status, {
              'Content-Type': incoming.headers['content-type'] || 'application/octet-stream',
              'Cache-Control': 'no-store',
              'X-Content-Type-Options': 'nosniff',
            })
            let received = 0
            incoming.on('data', (chunk) => {
              received += chunk.length
              if (received > 21 * 1024 * 1024) {
                incoming.destroy()
                res.destroy()
              }
            })
            incoming.on('error', () => res.destroy())
            incoming.pipe(res)
          },
        )
        upstream.setTimeout(180_000, () => upstream.destroy())
        upstream.on('error', () =>
          reply(res, 502, 'Visual API unavailable; check HTTPS trust and local configuration'),
        )
        let sent = 0
        req.on('data', (chunk) => {
          sent += chunk.length
          if (sent > maximum) {
            reply(res, 413, 'Image or request is too large')
            upstream.destroy()
            req.unpipe(upstream)
            req.resume()
          }
        })
        req.on('aborted', () => upstream.destroy())
        req.on('error', () => upstream.destroy())
        res.on('close', () => {
          if (!res.writableEnded) upstream.destroy()
        })
        req.pipe(upstream)
      })
    },
  }
}
