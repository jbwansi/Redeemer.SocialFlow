import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import {
  request as httpRequest,
  createServer,
  type IncomingMessage,
  type Server,
  type ServerResponse,
} from 'node:http'
import { PassThrough } from 'node:stream'
import { request } from 'node:https'
import { allowedRoute, isLocalRequest, visualGateway } from './visualGateway'
vi.mock('node:https', () => {
  const request = vi.fn()
  return { request, default: { request } }
})
beforeEach(() => vi.clearAllMocks())
const id = '11111111-1111-1111-1111-111111111111'
const path = '/posts/' + id + '/visuals'
const servers: Server[] = []
afterEach(async () => {
  await Promise.all(
    servers
      .splice(0)
      .map((server) => new Promise<void>((resolve) => server.close(() => resolve()))),
  )
})
function incoming(overrides: Record<string, unknown> = {}) {
  return {
    method: 'GET',
    socket: { remoteAddress: '127.0.0.1' },
    headers: { host: '127.0.0.1:5173', 'x-socialflow-local': '1' },
    ...overrides,
  } as unknown as IncomingMessage
}
async function start(enabled = true, secret = 'server-only-test-secret-0123456789012345') {
  let handler: (req: IncomingMessage, res: ServerResponse, next: () => void) => void
  const plugin = visualGateway(enabled, secret)
  const configure = plugin.configureServer as (server: unknown) => void
  configure({
    middlewares: {
      use: (fn: typeof handler) => {
        handler = fn
      },
    },
  })
  const server = createServer((req, res) =>
    handler(req, res, () => {
      res.statusCode = 404
      res.end()
    }),
  )
  servers.push(server)
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
  const address = server.address() as { port: number }
  return async (suffix: string, options: RequestInit = {}) =>
    new Promise<Response>((resolve, reject) => {
      const call = httpRequest(
        {
          hostname: '127.0.0.1',
          port: address.port,
          path: '/local-visuals' + suffix,
          method: options.method || 'GET',
          headers: {
            Host: '127.0.0.1:5173',
            'X-SocialFlow-Local': '1',
            ...(options.headers as Record<string, string>),
          },
        },
        (res) => {
          const chunks: Buffer[] = []
          res.on('data', (chunk) => chunks.push(chunk))
          res.on('end', () =>
            resolve(
              new Response(Buffer.concat(chunks), {
                status: res.statusCode,
                headers: res.headers as Record<string, string>,
              }),
            ),
          )
        },
      )
      call.on('error', reject)
      call.end(options.body as string | undefined)
    })
}
describe('local server gateway', () => {
  it.each([
    { headers: { host: 'evil.test:5173', 'x-socialflow-local': '1' } },
    { socket: { remoteAddress: '203.0.113.1' } },
    { headers: { host: '127.0.0.1:5173' } },
    { headers: { host: '127.0.0.1:5173', 'x-socialflow-local': '1', origin: 'https://evil.test' } },
    {
      headers: {
        host: '127.0.0.1:5173',
        'x-socialflow-local': '1',
        'sec-fetch-site': 'cross-site',
      },
    },
    {
      headers: {
        host: '127.0.0.1:5173',
        'x-socialflow-local': '1',
        'x-forwarded-for': '127.0.0.1',
      },
    },
    { method: 'POST' },
  ])('rejects non-local or CSRF request %#', (overrides) =>
    expect(isLocalRequest(incoming(overrides))).toBe(false),
  )
  it('accepts an exact same-origin mutation', () => {
    expect(
      isLocalRequest(
        incoming({
          method: 'POST',
          headers: {
            host: '127.0.0.1:5173',
            origin: 'http://127.0.0.1:5173',
            'x-socialflow-local': '1',
          },
        }),
      ),
    ).toBe(true)
  })
  it('only permits explicit visual route/method pairs', () => {
    for (const [suffix, method] of [
      ['', 'GET'],
      ['/upload', 'POST'],
      ['/generate', 'POST'],
      ['/' + id, 'GET'],
      ['/' + id, 'DELETE'],
      ['/' + id + '/select', 'POST'],
    ])
      expect(allowedRoute(path + suffix, method)).toBe(true)
    for (const suffix of ['/../secret', '?key=x', '/generate/select', '/upload/select', '/%2e%2e'])
      expect(allowedRoute(path + suffix, 'POST')).toBe(false)
    expect(allowedRoute('/posts/' + id + '/publish', 'POST')).toBe(false)
    expect(allowedRoute(path, 'DELETE')).toBe(false)
  })
  it('is disabled by default and exposes no secret in runtime status', async () => {
    const off = await start(false)
    expect((await off('/status')).status).toBe(404)
    const on = await start()
    const result = await on('/status')
    expect(await result.json()).toEqual({ enabled: true })
    expect(request).not.toHaveBeenCalled()
  })
  it('fails closed without a server secret, on CSRF and oversized bodies', async () => {
    const missing = await start(true, '')
    expect((await missing('/status')).status).toBe(503)
    const send = await start()
    expect(
      (
        await send(path + '/generate', {
          method: 'POST',
          body: '{}',
          headers: { 'Content-Type': 'application/json' },
        })
      ).status,
    ).toBe(403)
    const large = await send(path + '/generate', {
      method: 'POST',
      body: 'x'.repeat(65537),
      headers: { Origin: 'http://127.0.0.1:5173', 'Content-Type': 'application/json' },
    })
    expect(large.status).toBe(413)
    expect(request).not.toHaveBeenCalled()
  })
  it('uses fixed verified HTTPS, replaces client secrets, and proxies binary bytes only', async () => {
    const outgoing = new PassThrough() as PassThrough & { setTimeout: ReturnType<typeof vi.fn> }
    outgoing.setTimeout = vi.fn()
    vi.mocked(request).mockImplementation(((
      options: unknown,
      callback: (response: unknown) => void,
    ) => {
      queueMicrotask(() => {
        const response = Object.assign(new PassThrough(), {
          statusCode: 200,
          headers: { 'content-type': 'image/png', 'x-private': 'hidden' },
        })
        callback(response)
        response.end(Buffer.from([1, 2, 3]))
      })
      return outgoing
    }) as unknown as typeof request)
    const send = await start()
    const response = await send(path + '/' + id, {
      headers: { 'X-SocialFlow-Visual-Admin': 'attacker-value' },
    })
    expect([...new Uint8Array(await response.arrayBuffer())]).toEqual([1, 2, 3])
    expect(response.headers.get('x-private')).toBeNull()
    expect(response.headers.get('cache-control')).toBe('no-store')
    expect(request).toHaveBeenCalledWith(
      expect.objectContaining({
        hostname: '127.0.0.1',
        port: 65474,
        servername: 'localhost',
        rejectUnauthorized: true,
        headers: expect.objectContaining({
          'X-SocialFlow-Visual-Admin': 'server-only-test-secret-0123456789012345',
        }),
      }),
      expect.any(Function),
    )
  })
  it('does not retry TLS failures or return their internal details', async () => {
    const outgoing = new PassThrough() as PassThrough & { setTimeout: ReturnType<typeof vi.fn> }
    outgoing.setTimeout = vi.fn()
    vi.mocked(request).mockImplementation((() => {
      queueMicrotask(() => outgoing.emit('error', new Error('certificate invalid; secret payload')))
      return outgoing
    }) as unknown as typeof request)
    const send = await start()
    const response = await send(path)
    expect(response.status).toBe(502)
    expect(await response.text()).not.toContain('secret payload')
    expect(request).toHaveBeenCalledTimes(1)
  })
})

it('strictly allowlists identity and private logo routes', () => {
  const root = '/visual-identities'
  for (const method of ['GET', 'POST']) expect(allowedRoute(root, method)).toBe(true)
  for (const method of ['PUT', 'DELETE']) expect(allowedRoute(root + '/' + id, method)).toBe(true)
  expect(allowedRoute(root + '/' + id + '/logo', 'POST')).toBe(true)
  expect(allowedRoute(root + '/' + id + '/default', 'POST')).toBe(true)
  expect(allowedRoute(root + '/' + id + '/logos/' + id, 'GET')).toBe(true)
  for (const path of [
    root + '?secret=x',
    root + '/../files/a',
    root + '/' + id + '/logo?x=y',
    root + '/' + id + '/logos/not-a-guid',
  ])
    expect(allowedRoute(path, 'GET')).toBe(false)
  expect(allowedRoute(root + '/' + id + '/logos/' + id, 'DELETE')).toBe(false)
})

it('protects Facebook routes and does not proxy OAuth callbacks or arbitrary Graph paths', () => {
  expect(allowedRoute('/dev/facebook/runtime', 'GET')).toBe(true)
  for (const suffix of ['connect', 'page', 'disconnect']) {
    expect(allowedRoute('/dev/facebook/' + suffix, 'POST')).toBe(true)
    expect(allowedRoute('/dev/facebook/' + suffix, 'GET')).toBe(false)
  }
  expect(allowedRoute('/dev/facebook/posts/' + id + '/publish', 'POST')).toBe(true)
  for (const suffix of ['authorize', 'callback', 'me/accounts', 'runtime?access_token=x'])
    expect(allowedRoute('/dev/facebook/' + suffix, 'GET')).toBe(false)
  expect(
    isLocalRequest(
      incoming({
        method: 'POST',
        headers: {
          host: 'localhost:5173',
          'x-socialflow-local': '1',
          origin: 'https://attacker.example',
        },
      }),
    ),
  ).toBe(false)
})
