export type Account = { id: string; email: string }

export class AccountError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message)
  }
}

export async function accountRequest<T>(path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {}
  if (body !== undefined) {
    const csrf = await accountRequest<{ token: string }>('csrf')
    headers['X-CSRF-TOKEN'] = csrf.token
    headers['Content-Type'] = 'application/json'
  }
  const response = await fetch(`/api/auth/${path}`, {
    method: body === undefined ? 'GET' : 'POST',
    credentials: 'same-origin',
    cache: 'no-store',
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) {
    const problem = (await response.json().catch(() => ({}))) as { title?: string }
    throw new AccountError(response.status, problem.title ?? 'La demande a échoué. Réessayez.')
  }
  return (response.status === 204 ? undefined : await response.json()) as T
}
