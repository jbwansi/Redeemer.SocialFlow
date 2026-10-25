import { accountRequest } from './accounts'
import { ApiError } from './posts'

export type Workspace = {
  id: string
  name: string
  type: 1 | 2
  status: 1 | 2
  role: 1 | 2 | 3 | 4
}
async function request<T>(path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
  const headers: Record<string, string> = {}
  if (body !== undefined) {
    const csrf = await accountRequest<{ token: string }>('csrf', undefined, signal)
    headers['X-CSRF-TOKEN'] = csrf.token
    headers['Content-Type'] = 'application/json'
  }
  signal?.throwIfAborted()
  const response = await fetch(`/api/workspaces${path}`, {
    method: body === undefined ? 'GET' : 'POST',
    signal,
    credentials: 'same-origin',
    cache: 'no-store',
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok)
    throw new ApiError({ ...(await response.json().catch(() => ({}))), status: response.status })
  const value = (await response.json()) as T
  signal?.throwIfAborted()
  return value
}
export const workspacesApi = {
  personal: (signal?: AbortSignal) => request<Workspace>('/personal', {}, signal),
  list: (signal?: AbortSignal) => request<Workspace[]>('', undefined, signal),
  create: (name: string, signal?: AbortSignal) => request<Workspace>('', { name }, signal),
  select: (id: string, signal?: AbortSignal) =>
    request<Workspace>(`/${encodeURIComponent(id)}/select`, {}, signal),
}
