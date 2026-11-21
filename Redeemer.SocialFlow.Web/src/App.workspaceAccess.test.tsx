import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import App from './App'
import { setLocalAdministration } from './api/posts'
import { setActiveWorkspace, workspaceRequest } from './api/workspaceTransport'

afterEach(() => setActiveWorkspace(null))

it('keeps the dashboard and valid workspace when optional publication settings are forbidden', async () => {
  location.hash = 'dashboard'
  setLocalAdministration(false)
  setActiveWorkspace('holding')
  const fetcher = vi.fn(async (url: string) => {
    if (url === '/api/workspace-publication-settings') return new Response('{}', { status: 403 })
    if (url.startsWith('/api/posts')) return new Response('[]')
    if (url === '/api/auth/me') return new Response('{"id":"owner"}')
    if (url === '/api/workspaces/holding') return new Response('{"id":"holding","role":1}')
    throw new Error('Unexpected request')
  })
  vi.stubGlobal('fetch', fetcher)
  const events = vi.spyOn(window, 'dispatchEvent')
  render(<App workspaceId="holding" workspaceName="Holding" localAdministration={false} canManageSocial />)
  await waitFor(() => expect(fetcher).toHaveBeenCalledWith('/api/workspaces/holding', expect.any(Object)))
  expect(await screen.findByRole('region', { name: 'Post overview' })).toBeInTheDocument()
  expect(workspaceRequest().id).toBe('holding')
  expect(events).not.toHaveBeenCalledWith(expect.objectContaining({ type: 'workspace-access-lost' }))
})
