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
