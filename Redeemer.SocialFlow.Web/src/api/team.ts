import { apiRequest } from './posts'
import { accountRequest } from './accounts'
export type Role = 1 | 2 | 3 | 4
export const roleNames: Record<Role, string> = { 1: 'Owner', 2: 'Editor', 3: 'Admin', 4: 'Viewer' }
export type Member = { userId: string; email: string; role: Role; revision: string }
export type Invitation = {
  id: string
  email: string
  role: Role
  revision: string
  expiresAtUtc: string
}
export type Team = { members: Member[]; invitations: Invitation[]; myRole: Role }
const base = (id: string) => `/workspaces/${encodeURIComponent(id)}/team`
const body = (value: unknown, method = 'POST') => ({ method, body: JSON.stringify(value) })
export const teamApi = {
  get: (id: string, signal?: AbortSignal) => apiRequest<Team>(base(id), { signal }),
  invite: (id: string, email: string, role: Role) =>
    apiRequest<void>(base(id) + '/invitations', body({ email, role })),
  change: (id: string, member: Member, role: Role) =>
    apiRequest<void>(
      base(id) + `/members/${encodeURIComponent(member.userId)}`,
      body({ role, revision: member.revision }, 'PUT'),
    ),
  remove: (id: string, member: Member) =>
    apiRequest<void>(
      base(id) + `/members/${encodeURIComponent(member.userId)}/remove`,
      body({ revision: member.revision }),
    ),
  revoke: (id: string, invitation: Invitation) =>
    apiRequest<void>(
      base(id) + `/invitations/${invitation.id}/revoke`,
      body({ revision: invitation.revision }),
    ),
  leave: (id: string, revision: string) =>
    apiRequest<void>(base(id) + '/leave', body({ revision })),
  accept: async (code: string) => {
    const csrf = await accountRequest<{ token: string }>('csrf')
    return apiRequest<{ workspaceId: string }>('/invitations/accept', {
      ...body({ code }),
      headers: { 'X-CSRF-TOKEN': csrf.token },
    })
  },
}
