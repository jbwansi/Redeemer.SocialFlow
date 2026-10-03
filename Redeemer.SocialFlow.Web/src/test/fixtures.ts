import type { Post, GeneratedDraftSuccess } from '../api/posts'
export function generatedDraft(
  overrides: Partial<GeneratedDraftSuccess> = {},
): GeneratedDraftSuccess {
  return {
    outcome: 1,
    postId: 'post-1',
    draft: { post: post(), warnings: [] },
    metadata: { provider: 'TestProvider', model: 'test-model' },
    referencePassages: [],
    ...overrides,
  }
}
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
