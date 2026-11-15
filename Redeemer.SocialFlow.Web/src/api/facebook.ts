import { visualRequest } from './visuals'
import { apiRequest } from './posts'
import type { PublicationOperation, PublicationPreview, PublicationView } from './linkedin'
export interface FacebookRuntime {
  connected: boolean
  configured: boolean
  state:
    'disconnected' | 'expired' | 'select-page' | 'connected' | 'no-pages' | 'verification-required'
  connectionId: string | null
  expiresAt: string | null
  destination: string | null
  publicationEnabled: boolean
  workerEnabled: boolean
  pages: { id: string; name: string }[] | null
}
const root = '/dev/facebook'
export const workspaceFacebookApi = (connectionId: string) => ({
  publication: (id: string, signal?: AbortSignal) => apiRequest<PublicationView>(`/facebook/posts/${encodeURIComponent(id)}/publication`, { signal }),
  preview: (id: string) => apiRequest<PublicationPreview>(`/facebook/posts/${encodeURIComponent(id)}/preview`, { method: 'POST', body: JSON.stringify({ connectionId }) }),
  publish: (id: string, confirmation: string) => apiRequest<PublicationOperation>(`/facebook/posts/${encodeURIComponent(id)}/publish`, { method: 'POST', body: JSON.stringify({ confirmation }) }),
  authorize: (id: string) => apiRequest<void>(`/facebook/posts/${encodeURIComponent(id)}/automatic`, { method: 'POST', body: JSON.stringify({ connectionId }) }),
  cancelAutomatic: (id: string) => apiRequest<void>(`/facebook/posts/${encodeURIComponent(id)}/automatic`, { method: 'DELETE' }),
})
const path = (id: string) => root + '/posts/' + encodeURIComponent(id)
const post = <T>(url: string, body: unknown) =>
  visualRequest<T>(url, { method: 'POST', body: JSON.stringify(body) })
export const facebookApi = {
  runtime: (signal?: AbortSignal) => visualRequest<FacebookRuntime>(root + '/runtime', { signal }),
  connect: () => post<{ authorizeUrl: string }>(root + '/connect', {}),
  select: (connectionId: string, pageId: string) => post(root + '/page', { connectionId, pageId }),
  disconnect: (connectionId: string) => post(root + '/disconnect', { connectionId }),
  publication: (id: string, signal?: AbortSignal) =>
    visualRequest<PublicationView>(path(id) + '/publication', { signal }),
  preview: (id: string) => visualRequest<PublicationPreview>(path(id) + '/preview'),
  publish: (id: string, confirmation: string) =>
    post<PublicationOperation>(path(id) + '/publish', { confirmation }),
}
