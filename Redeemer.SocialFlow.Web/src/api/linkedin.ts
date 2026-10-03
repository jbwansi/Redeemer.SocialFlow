import { apiRequest } from './posts'

export interface LinkedInRuntime {
  connected: boolean
  expiresAt: string | null
  destination: string | null
  publicationEnabled: boolean
  workerEnabled: boolean
  workerIntervalSeconds: number
  connectUrl: string
}
export interface PublicationOperation {
  operationId: string
  socialPostId: string
  provider: string
  destination: string
  content: string
  state: 1 | 2 | 3 // durable states: Indeterminate, Confirmed, Failed
  claimedAt: string
  updatedAt: string
  completedAt: string | null
  externalId: string | null
}
export interface PublicationView {
  operation: PublicationOperation | null
  canPublish: boolean
}
export interface PublicationPreview {
  content: string
  destination: string
  confirmation: string
}
const base = '/dev/linkedin'
export const linkedInApi = {
  runtime: (signal?: AbortSignal) => apiRequest<LinkedInRuntime>(`${base}/runtime`, { signal }),
  publication: (id: string, signal?: AbortSignal) =>
    apiRequest<PublicationView>(`${base}/posts/${encodeURIComponent(id)}/publication`, { signal }),
  preview: (id: string) =>
    apiRequest<PublicationPreview>(`${base}/posts/${encodeURIComponent(id)}/preview`),
  publish: (id: string, confirmation: string) =>
    apiRequest<PublicationOperation>(`${base}/posts/${encodeURIComponent(id)}/publish`, {
      method: 'POST',
      body: JSON.stringify({ confirmation }),
    }),
}
