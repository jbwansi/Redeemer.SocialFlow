import { apiRequest, type Platform } from './posts'
export type Connection = {
  id: string
  platform: Platform
  externalAccountId: string
  displayName: string
  accountType: string
  connectionStatus: 1 | 2 | 3 | 4
  connectedAtUtc: string
  expiresAtUtc: string | null
  lastValidatedAtUtc: string | null
  publishingPermissionGranted: boolean
  revision: string
}
export type Provider = { platform: Platform; available: boolean; message: string }
export type Candidate = {
  externalAccountId: string
  displayName: string
  accountType: string
  publishingPermissionGranted: boolean
}
const base = (id: string) => `/workspaces/${encodeURIComponent(id)}/social-connections`
const post = (data: unknown) => ({ method: 'POST', body: JSON.stringify(data) })
export const socialConnectionsApi = {
  list: (id: string, signal?: AbortSignal) => apiRequest<Connection[]>(base(id), { signal }),
  providers: (id: string, signal?: AbortSignal) =>
    apiRequest<Provider[]>(base(id) + '/providers', { signal }),
  start: (id: string, platform: Platform) =>
    apiRequest<{ authorizationUrl: string }>(base(id) + '/start', post({ platform })),
  candidates: (id: string, session: string, signal?: AbortSignal) =>
    apiRequest<Candidate[]>(base(id) + `/sessions/${encodeURIComponent(session)}`, { signal }),
  select: (id: string, session: string, externalAccountIds: string[]) =>
    apiRequest<void>(
      base(id) + `/sessions/${encodeURIComponent(session)}/select`,
      post({ externalAccountIds }),
    ),
  disconnect: (id: string, c: Connection) =>
    apiRequest<void>(base(id) + `/${c.id}/disconnect`, post({ revision: c.revision })),
  validate: (id: string, c: Connection) =>
    apiRequest<void>(base(id) + `/${c.id}/validate`, post({ revision: c.revision })),
}
