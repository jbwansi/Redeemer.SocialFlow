let activeId: string | null = null
let controller = new AbortController()

// Cancel every read/mutation and discard late responses before mounting another workspace.
export function setActiveWorkspace(id: string | null) {
  controller.abort()
  controller = new AbortController()
  activeId = id
}
export function workspaceRequest() {
  const signal = controller.signal
  return {
    id: activeId,
    signal,
    assertCurrent: () => {
      if (signal.aborted) throw new DOMException('Workspace changed', 'AbortError')
    },
  }
}

export function expireSession() {
  setActiveWorkspace(null)
  window.dispatchEvent(new Event('session-expired'))
}

// Raw read-only probes deliberately bypass apiRequest to avoid recursive 403 handling.
export async function handleWorkspaceDenial(
  status: number,
  workspace: ReturnType<typeof workspaceRequest>,
  signal: AbortSignal,
) {
  workspace.assertCurrent()
  signal.throwIfAborted()
  if (status === 401) {
    expireSession()
    return
  }
  if (status !== 403 || !workspace.id) return
  const options: RequestInit = {
    credentials: 'same-origin', cache: 'no-store',
    signal: AbortSignal.any([signal, AbortSignal.timeout(10_000)]),
  }
  try {
    const session = await fetch('/api/auth/me', options)
    workspace.assertCurrent()
    signal.throwIfAborted()
    if (session.status === 401) { expireSession(); return }
    if (!session.ok) return // An unavailable API is not proof of revoked membership.
    const access = await fetch(`/api/workspaces/${encodeURIComponent(workspace.id)}`, options)
    workspace.assertCurrent()
    signal.throwIfAborted()
    if (access.status === 401) { expireSession(); return }
    if (access.status === 404) {
      const id = workspace.id
      setActiveWorkspace(null)
      window.dispatchEvent(new CustomEvent('workspace-access-lost', { detail: { workspaceId: id } }))
    }
  } catch {
    workspace.assertCurrent()
    signal.throwIfAborted()
    // Keep the original operation error on network failure/timeout. No retries.
  }
}
