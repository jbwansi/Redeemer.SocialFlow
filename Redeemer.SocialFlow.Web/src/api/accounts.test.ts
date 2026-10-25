import { expect, it, vi } from 'vitest'
import { accountRequest } from './accounts'

it('gets a fresh CSRF token for each mutation and uses same-origin cookies', async () => {
  const fetcher = vi
    .fn()
    .mockResolvedValueOnce(new Response(JSON.stringify({ token: 'form-proof' })))
    .mockResolvedValueOnce(new Response(null, { status: 204 }))
  vi.stubGlobal('fetch', fetcher)
  await accountRequest('logout', {})
  expect(fetcher).toHaveBeenNthCalledWith(
    1,
    '/api/auth/csrf',
    expect.objectContaining({ credentials: 'same-origin', cache: 'no-store' }),
  )
  expect(fetcher).toHaveBeenNthCalledWith(
    2,
    '/api/auth/logout',
    expect.objectContaining({
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'X-CSRF-TOKEN': 'form-proof', 'Content-Type': 'application/json' },
    }),
  )
})

it('preserves normalized authentication errors', async () => {
  vi.stubGlobal(
    'fetch',
    vi
      .fn()
      .mockResolvedValue(
        new Response(JSON.stringify({ title: 'Session expirée' }), { status: 401 }),
      ),
  )
  await expect(accountRequest('me')).rejects.toMatchObject({
    status: 401,
    message: 'Session expirée',
  })
})
