import { describe, expect, it, vi } from 'vitest'
vi.unmock('./linkedin')
import { linkedInApi } from './linkedin'

describe('LinkedIn API contract', () => {
  it('posts only the opaque server confirmation, never a destination or token', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValue({ ok: true, status: 200, json: async () => ({ state: 2 }) })
    vi.stubGlobal('fetch', fetch)
    await linkedInApi.publish('post/id', 'confirmation')
    expect(fetch).toHaveBeenCalledWith(
      '/api/dev/linkedin/posts/post%2Fid/publish',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ confirmation: 'confirmation' }),
      }),
    )
  })
})
