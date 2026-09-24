import { describe, it, expect, vi } from 'vitest'
import { ApiError, postsApi } from './posts'
import { post } from '../test/fixtures'

describe('REST client', () => {
  it('encodes filters and forwards cancellation', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify([post()])))
    vi.stubGlobal('fetch', fetch)
    const controller = new AbortController()
    expect(
      await postsApi.list(
        {
          platform: 2,
          status: 4,
          createdFrom: '2026-01-01T12:00:00+02:00',
          createdTo: '2026-02-01T00:00:00Z',
        },
        controller.signal,
      ),
    ).toEqual([post()])
    const [url, options] = fetch.mock.calls[0]
    expect(url).toContain('createdFrom=2026-01-01T12%3A00%3A00%2B02%3A00')
    expect(url).toContain('platform=2&status=4')
    expect(options.signal).toBe(controller.signal)
  })
  it('uses the API verbs, bodies, and action routes', async () => {
    const fetch = vi
      .fn()
      .mockImplementation(() => Promise.resolve(new Response(JSON.stringify(post()))))
    vi.stubGlobal('fetch', fetch)
    await postsApi.create({ title: 'New', content: null, platform: 1 })
    expect(fetch).toHaveBeenLastCalledWith(
      '/api/posts',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ title: 'New', content: null, platform: 1 }),
      }),
    )
    await postsApi.update('a/b', {
      title: 'Edit',
      content: '',
      callToAction: null,
      visualBrief: null,
      visualUrl: null,
    })
    expect(fetch).toHaveBeenLastCalledWith(
      '/api/posts/a%2Fb',
      expect.objectContaining({ method: 'PUT' }),
    )
    await postsApi.get('a/b')
    expect(fetch).toHaveBeenLastCalledWith('/api/posts/a%2Fb', expect.anything())
    for (const action of ['submit-for-review', 'approve', 'reject', 'cancel'] as const) {
      await postsApi.transition('1', action)
      expect(fetch).toHaveBeenLastCalledWith(
        `/api/posts/1/${action}`,
        expect.objectContaining({ method: 'POST' }),
      )
    }
    await postsApi.transition('1', 'schedule', '2026-02-01T12:00:00Z')
    expect(fetch).toHaveBeenLastCalledWith(
      '/api/posts/1/schedule',
      expect.objectContaining({ body: '{"scheduledAt":"2026-02-01T12:00:00Z"}' }),
    )
    fetch.mockResolvedValueOnce(new Response(null, { status: 204 }))
    expect(await postsApi.remove('1')).toBeUndefined()
    expect(fetch).toHaveBeenLastCalledWith(
      '/api/posts/1',
      expect.objectContaining({ method: 'DELETE' }),
    )
  })
  it('preserves ProblemDetails including validation errors and trace IDs', async () => {
    const problem = {
      title: 'Domain validation failed',
      detail: 'Content is required.',
      traceId: 'trace-1',
      errors: { content: ['Required'] },
    }
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(JSON.stringify(problem), { status: 400 })),
    )
    await expect(postsApi.list()).rejects.toMatchObject({ problem: { ...problem, status: 400 } })
  })
  it('handles non-JSON proxy errors and network failures', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(new Response('Bad gateway', { status: 502 }))
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
    vi.stubGlobal('fetch', fetch)
    await expect(postsApi.list()).rejects.toMatchObject({
      problem: { title: 'Request failed (502)' },
    })
    await expect(postsApi.list()).rejects.toBeInstanceOf(ApiError)
  })
  it('does not turn aborted requests into connection errors', async () => {
    const error = new DOMException('Aborted', 'AbortError')
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(error))
    await expect(postsApi.list()).rejects.toBe(error)
  })
})
