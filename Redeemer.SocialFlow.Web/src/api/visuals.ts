import { apiRequest } from './posts'
export interface Visual {
  id: string
  socialPostId: string
  origin: 1 | 2
  contentType: string
  width: number
  height: number
  byteLength: number
  sha256: string
  createdAt: string
  validatedAt: string | null
  selected: boolean
  generation: Record<string, unknown> | null
}
export interface VisualIdentity {
  identityId: string
  name: string
  style: string
  colors: string[]
  slogan?: string
  instructions?: string
  libraryId?: string
  revision?: number
  isActive?: boolean
}
const root = '/local-visuals'
const path = (post: string) => '/posts/' + encodeURIComponent(post) + '/visuals'
export function visualRequest<T>(url: string, options: RequestInit = {}, binary = false) {
  return apiRequest<T>(
    url,
    { ...options, headers: { ...options.headers, 'X-SocialFlow-Local': '1' } },
    { baseUrl: root, binary },
  )
}
export const visualsApi = {
  runtime: (signal?: AbortSignal) => visualRequest<{ enabled: boolean }>('/status', { signal }),
  list: (post: string, signal?: AbortSignal) => visualRequest<Visual[]>(path(post), { signal }),
  image: (post: string, id: string, signal?: AbortSignal) =>
    visualRequest<Blob>(path(post) + '/' + encodeURIComponent(id), { signal }, true),
  generate: (post: string, identity: VisualIdentity, visualBrief: string, signal?: AbortSignal) =>
    visualRequest<Visual>(path(post) + '/generate', {
      method: 'POST',
      body: JSON.stringify({ ...identity, visualBrief }),
      signal,
    }),
  upload: (post: string, file: File, signal?: AbortSignal) => {
    const body = new FormData()
    body.append('file', file)
    return visualRequest<Visual>(path(post) + '/upload', { method: 'POST', body, signal })
  },
  select: (post: string, id: string, signal?: AbortSignal) =>
    visualRequest<Visual>(path(post) + '/' + encodeURIComponent(id) + '/select', {
      method: 'POST',
      signal,
    }),
  remove: (post: string, id: string, signal?: AbortSignal) =>
    visualRequest<void>(path(post) + '/' + encodeURIComponent(id), { method: 'DELETE', signal }),
}
