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
export function visualRequest<T>(url: string, options: RequestInit = {}, binary = false) {
  return apiRequest<T>(
    url,
    { ...options, headers: { ...options.headers, 'X-SocialFlow-Local': '1' } },
    { baseUrl: root, binary },
  )
}
function createVisualsApi(workspace: boolean) {
  const request = workspace
    ? <T>(url: string, options: RequestInit = {}, binary = false) => apiRequest<T>(url, options, { binary })
    : visualRequest
  const path = (post: string) =>
    '/posts/' + encodeURIComponent(post) + (workspace ? '/workspace-visuals' : '/visuals')
  return {
  runtime: (signal?: AbortSignal) => request<{ enabled: boolean }>(
    workspace ? '/posts/workspace-visuals/status' : '/status', { signal }),
  list: (post: string, signal?: AbortSignal) => request<Visual[]>(path(post), { signal }),
  image: (post: string, id: string, signal?: AbortSignal) =>
    request<Blob>(path(post) + '/' + encodeURIComponent(id), { signal }, true),
  generate: (post: string, identity: VisualIdentity, visualBrief: string, signal?: AbortSignal) =>
    request<Visual>(path(post) + '/generate', {
      method: 'POST',
      body: JSON.stringify({ ...identity, visualBrief }),
      signal,
    }),
  upload: (post: string, file: File, signal?: AbortSignal) => {
    const body = new FormData()
    body.append('file', file)
    return request<Visual>(path(post) + '/upload', { method: 'POST', body, signal })
  },
  select: (post: string, id: string, signal?: AbortSignal) =>
    request<Visual>(path(post) + '/' + encodeURIComponent(id) + '/select', {
      method: 'POST',
      signal,
    }),
  remove: (post: string, id: string, signal?: AbortSignal) =>
    request<void>(path(post) + '/' + encodeURIComponent(id), { method: 'DELETE', signal }),
}
}

export const visualsApi = createVisualsApi(false)
export const workspaceVisualsApi = createVisualsApi(true)
