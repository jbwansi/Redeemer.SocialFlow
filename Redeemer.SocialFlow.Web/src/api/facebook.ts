import { visualRequest } from './visuals'
import type { PublicationOperation, PublicationPreview, PublicationView } from './linkedin'
export interface FacebookRuntime {
  connected: boolean
  configured: boolean
  state: 'disconnected' | 'expired' | 'select-page' | 'connected'
  connectionId: string | null
  expiresAt: string | null
  destination: string | null
  publicationEnabled: boolean
  workerEnabled: boolean
  pages: { id: string; name: string }[] | null
}
const root = '/dev/facebook'
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
