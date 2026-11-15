import { apiRequest, type Platform } from './posts'
export type PublishingCapability = {
  status: 0 | 1 | 2 // Unverified, Verified, Denied
  reason: string
  source: string
  checkedAtUtc: string | null
  task: string | null
}
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
  publishingCapability?: PublishingCapability | null
  revision: string
}
export type Provider = { platform: Platform; available: boolean; message: string }
export type Candidate = {
  externalAccountId: string
  displayName: string
  accountType: string
  publishingPermissionGranted: boolean
  publishingCapability?: PublishingCapability | null
}
export type DiscoveryDiagnostic = {
  outcome: string
  stage: string
  pagesReceived: number
  pagesRetained: number
  pagesWithoutPublishing: number
  requests: number
  complete: boolean
  httpStatus: number | null
  exclusions: Record<string, number>
  pages?: { externalPageId: string | null; outcome: string }[] | null
  facebook?: {
    clientId: string | null; loginConfigId: string | null
    tokenType: string | null; appIdMatches: boolean | null
    granular?: { shape: string; permissions: { permission: string; targetsStatus: string; targetIds: string[] }[] } | null
    pagesShowList: boolean | null; pagesReadEngagement: boolean | null; pagesManagePosts: boolean | null
    responses: { stage: string; httpStatus: number; dataShape: string; graphError: boolean; graphCode: number | null; graphSubcode: number | null }[] | null
  } | null
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
  diagnostics: (id: string, session: string, signal?: AbortSignal) =>
    apiRequest<DiscoveryDiagnostic | null>(base(id) + `/sessions/${encodeURIComponent(session)}/diagnostics`, { signal }),
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
