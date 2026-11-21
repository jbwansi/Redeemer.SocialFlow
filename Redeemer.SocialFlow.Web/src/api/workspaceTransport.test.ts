import { afterEach, expect, it, vi } from 'vitest'
import { apiRequest } from './posts'
import { setActiveWorkspace, workspaceRequest } from './workspaceTransport'

afterEach(() => setActiveWorkspace(null))
it('sends workspace mutations with authenticated cookies and CSRF protection', async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ token: 'csrf-test' })))
    .mockResolvedValueOnce(new Response(null, { status: 204 }))
  vi.stubGlobal('fetch', fetcher)
  setActiveWorkspace('holding')
  await apiRequest('/posts/post-id/approve', { method: 'POST' })
  expect(fetcher).toHaveBeenCalledTimes(2)
  expect(fetcher).toHaveBeenLastCalledWith('/api/posts/post-id/approve', expect.objectContaining({
    method: 'POST', credentials: 'same-origin',
    headers: expect.objectContaining({ 'X-Workspace-Id': 'holding', 'X-CSRF-TOKEN': 'csrf-test' }),
  }))
})

it('expires the session and cancels business operations on 401 without verification', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 401 })))
  const events = vi.spyOn(window, 'dispatchEvent')
  setActiveWorkspace('holding')
  const previous = workspaceRequest()
  await expect(apiRequest('/posts')).rejects.toMatchObject({ problem: { status: 401 } })
  expect(previous.signal.aborted).toBe(true)
  expect(workspaceRequest().id).toBeNull()
  expect(events).toHaveBeenCalledWith(expect.objectContaining({ type: 'session-expired' }))
  expect(fetch).toHaveBeenCalledTimes(1)
})

it.each(['/posts', '/workspace-publication-settings'])(
  'keeps a valid Owner workspace after an operation-specific 403 on %s', async (path) => {
    const fetcher = vi.fn()
      .mockResolvedValueOnce(new Response('{"title":"Operation forbidden"}', { status: 403 }))
      .mockResolvedValueOnce(new Response('{"id":"owner"}'))
      .mockResolvedValueOnce(new Response('{"id":"holding","role":1}'))
    vi.stubGlobal('fetch', fetcher)
    const events = vi.spyOn(window, 'dispatchEvent')
    setActiveWorkspace('holding')
    await expect(apiRequest(path)).rejects.toMatchObject({ problem: { status: 403, title: 'Operation forbidden' } })
    expect(fetcher.mock.calls.map(call => call[0])).toEqual([`/api${path}`, '/api/auth/me', '/api/workspaces/holding'])
    expect(fetcher).toHaveBeenLastCalledWith('/api/workspaces/holding', expect.objectContaining({ credentials: 'same-origin', cache: 'no-store' }))
    expect(workspaceRequest().id).toBe('holding')
    expect(events).not.toHaveBeenCalled()
  },
)

it.each([
  [401, undefined, 'session-expired'],
  [200, 401, 'session-expired'],
  [200, 404, 'workspace-access-lost'],
])('verifies 403 with session %s and workspace %s', async (sessionStatus, workspaceStatus, event) => {
  const fetcher = vi.fn()
    .mockResolvedValueOnce(new Response('{}', { status: 403 }))
    .mockResolvedValueOnce(new Response('{}', { status: sessionStatus as number }))
  if (workspaceStatus) fetcher.mockResolvedValueOnce(new Response('{}', { status: workspaceStatus as number }))
  vi.stubGlobal('fetch', fetcher)
  const events = vi.spyOn(window, 'dispatchEvent')
  setActiveWorkspace('holding')
  const previous = workspaceRequest()
  await expect(apiRequest('/posts')).rejects.toMatchObject({ problem: { status: 403 } })
  expect(previous.signal.aborted).toBe(true)
  expect(workspaceRequest().id).toBeNull()
  expect(events).toHaveBeenCalledWith(expect.objectContaining({ type: event }))
  expect(fetcher).toHaveBeenCalledTimes(workspaceStatus ? 3 : 2)
})

it.each([403, 500, 503])('does not infer revocation or recurse when membership verification returns %s', async status => {
  vi.stubGlobal('fetch', vi.fn()
    .mockResolvedValueOnce(new Response('{}', { status: 403 }))
    .mockResolvedValueOnce(new Response('{}'))
    .mockResolvedValueOnce(new Response('{}', { status })))
  const events = vi.spyOn(window, 'dispatchEvent')
  setActiveWorkspace('holding')
  await expect(apiRequest('/posts')).rejects.toMatchObject({ problem: { status: 403 } })
  expect(workspaceRequest().id).toBe('holding')
  expect(fetch).toHaveBeenCalledTimes(3)
  expect(events).not.toHaveBeenCalled()
})

it('keeps the original operation error when the session probe cannot reach the API', async () => {
  vi.stubGlobal('fetch', vi.fn()
    .mockResolvedValueOnce(new Response('{}', { status: 403 }))
    .mockRejectedValueOnce(new TypeError('offline')))
  setActiveWorkspace('holding')
  await expect(apiRequest('/posts')).rejects.toMatchObject({ problem: { status: 403 } })
  expect(workspaceRequest().id).toBe('holding')
})

it.each([403, 503])('stops verification when the session endpoint returns %s', async status => {
  vi.stubGlobal('fetch', vi.fn()
    .mockResolvedValueOnce(new Response('{}', { status: 403 }))
    .mockResolvedValueOnce(new Response('{}', { status })))
  setActiveWorkspace('holding')
  await expect(apiRequest('/posts')).rejects.toMatchObject({ problem: { status: 403 } })
  expect(fetch).toHaveBeenCalledTimes(2)
  expect(workspaceRequest().id).toBe('holding')
})

it('does not deselect a workspace on an unavailable business API', async () => {
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')))
  setActiveWorkspace('holding')
  await expect(apiRequest('/posts')).rejects.toMatchObject({ problem: { title: 'Connection unavailable' } })
  expect(workspaceRequest().id).toBe('holding')
  expect(fetch).toHaveBeenCalledTimes(1)
})

it('expires a session rejected during CSRF retrieval without sending the mutation', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 401 })))
  const events = vi.spyOn(window, 'dispatchEvent')
  setActiveWorkspace('holding')
  await expect(apiRequest('/posts', { method: 'POST', body: '{}' })).rejects.toMatchObject({ status: 401 })
  expect(workspaceRequest().id).toBeNull()
  expect(events).toHaveBeenCalledWith(expect.objectContaining({ type: 'session-expired' }))
  expect(fetch).toHaveBeenCalledTimes(1)
})

it.each([401, 404])('ignores late membership response %s after changing workspace', async status => {
  let finish!: (value: Response) => void
  let entered!: () => void
  const probing = new Promise<void>(resolve => { entered = resolve })
  vi.stubGlobal('fetch', vi.fn()
    .mockResolvedValueOnce(new Response('{}', { status: 403 }))
    .mockResolvedValueOnce(new Response('{}'))
    .mockImplementationOnce(() => { entered(); return new Promise<Response>(resolve => { finish = resolve }) }))
  const events = vi.spyOn(window, 'dispatchEvent')
  setActiveWorkspace('old')
  const failure = expect(apiRequest('/posts')).rejects.toMatchObject({ name: 'AbortError' })
  await probing
  setActiveWorkspace('new')
  finish(new Response('{}', { status }))
  await failure
  expect(workspaceRequest().id).toBe('new')
  expect(events).not.toHaveBeenCalled()
})
it('binds requests to the workspace and rejects a late response even if fetch ignores cancellation', async () => {
  let finish!: (value: Response) => void
  vi.stubGlobal(
    'fetch',
    vi.fn(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    ),
  )
  setActiveWorkspace('alice')
  const result = apiRequest('/posts')
  const failure = expect(result).rejects.toMatchObject({ name: 'AbortError' })
  expect(fetch).toHaveBeenCalledWith(
    '/api/posts',
    expect.objectContaining({ headers: expect.objectContaining({ 'X-Workspace-Id': 'alice' }) }),
  )
  setActiveWorkspace('bob')
  finish(new Response(JSON.stringify([{ title: 'Alice secret' }])))
  await failure
})
it('does not send a mutation to the new workspace when switching during CSRF retrieval', async () => {
  let finish!: (value: Response) => void
  const fetcher = vi.fn(
    () =>
      new Promise((resolve) => {
        finish = resolve
      }),
  )
  vi.stubGlobal('fetch', fetcher)
  setActiveWorkspace('alice')
  const result = apiRequest('/posts', { method: 'POST', body: '{}' })
  const failure = expect(result).rejects.toMatchObject({ name: 'AbortError' })
  setActiveWorkspace('bob')
  finish(new Response(JSON.stringify({ token: 'test-token' })))
  await failure
  expect(fetcher).toHaveBeenCalledTimes(1)
})
