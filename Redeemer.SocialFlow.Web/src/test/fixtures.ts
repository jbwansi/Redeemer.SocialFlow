import type { Post } from '../api/posts'
export function post(overrides: Partial<Post> = {}): Post {
  return {
    id: 'post-1',
    title: 'A shared vision',
    content: 'Building meaningful connections.',
    platform: 2,
    status: 1,
    callToAction: null,
    visualBrief: null,
    visualUrl: null,
    scheduledAt: null,
    publishedAt: null,
    createdAt: '2026-01-01T12:00:00Z',
    updatedAt: '2026-01-02T12:00:00Z',
    ...overrides,
  }
}
