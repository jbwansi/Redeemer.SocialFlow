import { apiRequest } from './posts'
export interface VisualTextOptions {
  display: 'Illustration' | 'Title' | 'Messages'
  title?: string
  subtitle?: string
  messages?: string[]
  layout: 'Landscape' | 'Square'
}
export interface VisualTextReview {
  canonicalCorrectionsRequired?: boolean
  text: VisualTextOptions
  fingerprint: string
  terminology?: { warnings: string[] } | null
}
export interface VisualConcept {
  text?: VisualTextOptions
  warnings?: string[]
  subject: string
  message: string
  scene: string
  brief: string
  style: string
  exclusions: string[]
  identityName?: string
  identityStatus?: string
  conceptToken: string
}
export interface VisualAvailability {
  enabled: boolean
  uploadEnabled?: boolean
  generationEnabled?: boolean
  reason?: string | null
}
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
  identityMode?: 'Approved' | 'Temporary'
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
  runtime: (signal?: AbortSignal) => request<VisualAvailability>(
    workspace ? '/posts/workspace-visuals/status' : '/status', { signal }),
  list: (post: string, signal?: AbortSignal) => request<Visual[]>(path(post), { signal }),
  image: (post: string, id: string, signal?: AbortSignal) =>
    request<Blob>(path(post) + '/' + encodeURIComponent(id), { signal }, true),
  prepare: (post: string, identity: VisualIdentity, visualBrief: string, signal?: AbortSignal) =>
    request<VisualConcept>(path(post) + '/concept', {
      method: 'POST', body: JSON.stringify({ ...identity, visualBrief }), signal,
    }),
  generate: (post: string, identity: VisualIdentity, visualBrief: string, signal?: AbortSignal, conceptToken?: string) =>
    request<Visual>(path(post) + '/generate', {
      method: 'POST',
      body: JSON.stringify({ ...identity, visualBrief, conceptToken }),
      signal,
    }),
  upload: (post: string, file: File, signal?: AbortSignal) => {
    const body = new FormData()
    body.append('file', file)
    return request<Visual>(path(post) + '/upload', { method: 'POST', body, signal })
  },
  reviewText: (post: string, text: VisualTextOptions, signal?: AbortSignal) =>
    request<VisualTextReview>(path(post) + '/text-review', { method: 'POST', body: JSON.stringify(text), signal }),
  compose: (post: string, id: string, libraryId: string, revision: number, mode: string, logoContainsSlogan: boolean | null, signal?: AbortSignal,
    textValidation?: { text: VisualTextOptions; textFingerprint: string; textConfirmed: boolean }, layout = 'Classic') =>
    request<Visual>(path(post) + '/' + encodeURIComponent(id) + '/compose', {
      method: 'POST', body: JSON.stringify({ libraryId, revision, mode, logoContainsSlogan, layout, ...textValidation }), signal,
    }),
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
