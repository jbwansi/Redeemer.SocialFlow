import { describe, expect, it, vi } from 'vitest'
import { visualsApi } from './visuals'
vi.unmock('./visuals')
describe('visual transport', () => {
  it('uses same-origin gateway, multipart without a JSON content type and no credentials', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response('{}'))
    vi.stubGlobal('fetch', fetch)
    const file = new File(['png'], 'test.png', { type: 'image/png' })
    const controller = new AbortController()
    await visualsApi.upload('post', file, controller.signal)
    const [url, options] = fetch.mock.calls[0]
    expect(url).toBe('/local-visuals/posts/post/visuals/upload')
    expect(options.body).toBeInstanceOf(FormData)
    expect(options.headers['Content-Type']).toBeUndefined()
    expect(options.headers['X-SocialFlow-Local']).toBe('1')
    expect(options.headers['X-SocialFlow-Visual-Admin']).toBeUndefined()
    controller.abort()
    expect(options.signal.aborted).toBe(true)
  })
  it('reads binary images and sends generation only on explicit invocation', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(new Response('png', { headers: { 'Content-Type': 'image/png' } }))
      .mockResolvedValueOnce(new Response('{}'))
    vi.stubGlobal('fetch', fetch)
    const blob = await visualsApi.image('p', 'v')
    expect(blob.type).toBe('image/png')
    expect(fetch).toHaveBeenCalledTimes(1)
    await visualsApi.generate(
      'p',
      { identityId: 'a', name: 'A', style: 'Flat', colors: ['#123456'] },
      'Garden',
    )
    expect(JSON.parse(fetch.mock.calls[1][1].body).visualBrief).toBe('Garden')
  })
  it('preserves API errors and abort signals', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(new Response('{"title":"Denied"}', { status: 403 }))
        .mockRejectedValueOnce(new DOMException('aborted', 'AbortError')),
    )
    await expect(visualsApi.list('p')).rejects.toMatchObject({
      problem: { status: 403, title: 'Denied' },
    })
    await expect(visualsApi.list('p')).rejects.toMatchObject({ name: 'AbortError' })
  })
})
