export const platforms = { 1: 'Facebook', 2: 'LinkedIn', 3: 'Instagram' } as const
export const statuses = {
  1: 'Draft',
  2: 'Ready for review',
  3: 'Approved',
  4: 'Scheduled',
  5: 'Published',
  6: 'Rejected',
  7: 'Failed',
  8: 'Cancelled',
} as const
export type Platform = keyof typeof platforms
export type Status = keyof typeof statuses
export interface Post {
  id: string
  title: string
  content: string
  platform: Platform
  status: Status
  callToAction: string | null
  visualBrief: string | null
  visualUrl: string | null
  scheduledAt: string | null
  publishedAt: string | null
  createdAt: string
  updatedAt: string
}
export interface CreatePost {
  title: string
  content: string | null
  platform: Platform
}
export interface UpdatePost {
  title: string
  content: string | null
  callToAction: string | null
  visualBrief: string | null
  visualUrl: string | null
}
export interface Filters {
  platform?: Platform
  status?: Status
  createdFrom?: string
  createdTo?: string
}
export type WorkflowAction = 'submit-for-review' | 'approve' | 'reject' | 'schedule' | 'cancel'
export interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
  traceId?: string
  errors?: Record<string, string[]>
}
export class ApiError extends Error {
  constructor(public problem: ProblemDetails) {
    super(problem.detail || problem.title || 'The request could not be completed.')
    this.name = 'ApiError'
  }
}
const baseUrl = (import.meta.env.VITE_API_BASE_URL || '/api').replace(/\/$/, '')
async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  let response: Response
  try {
    response = await fetch(`${baseUrl}/posts${path}`, {
      ...options,
      headers: {
        Accept: 'application/json, application/problem+json',
        ...(options.body ? { 'Content-Type': 'application/json' } : {}),
        ...options.headers,
      },
    })
  } catch (error) {
    if (
      typeof error === 'object' &&
      error !== null &&
      'name' in error &&
      error.name === 'AbortError'
    )
      throw error
    throw new ApiError({
      title: 'Connection unavailable',
      detail: 'We couldn’t reach SocialFlow. Check your connection and try again.',
    })
  }
  if (!response.ok) {
    let problem: ProblemDetails = { title: `Request failed (${response.status})` }
    try {
      problem = { ...problem, ...(await response.json()) }
    } catch {
      /* Proxy failures may not contain JSON. */
    }
    throw new ApiError({ ...problem, status: response.status })
  }
  return response.status === 204 ? (undefined as T) : (response.json() as Promise<T>)
}
export const postsApi = {
  list(filters: Filters = {}, signal?: AbortSignal) {
    const query = new URLSearchParams()
    Object.entries(filters).forEach(([key, value]) => {
      if (value !== undefined && value !== '') query.set(key, String(value))
    })
    return request<Post[]>(query.size ? `?${query}` : '', { signal })
  },
  get: (id: string, signal?: AbortSignal) =>
    request<Post>(`/${encodeURIComponent(id)}`, { signal }),
  create: (data: CreatePost) => request<Post>('', { method: 'POST', body: JSON.stringify(data) }),
  update: (id: string, data: UpdatePost) =>
    request<Post>(`/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(data) }),
  remove: (id: string) => request<void>(`/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  transition: (id: string, action: WorkflowAction, scheduledAt?: string) =>
    request<Post>(`/${encodeURIComponent(id)}/${action}`, {
      method: 'POST',
      ...(action === 'schedule' ? { body: JSON.stringify({ scheduledAt }) } : {}),
    }),
}
