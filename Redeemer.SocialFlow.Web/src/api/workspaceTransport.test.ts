import { afterEach, expect, it, vi } from 'vitest'
import { apiRequest } from './posts'
import { setActiveWorkspace } from './workspaceTransport'

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
