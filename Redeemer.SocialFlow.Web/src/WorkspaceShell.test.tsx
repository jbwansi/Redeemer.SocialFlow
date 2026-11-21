import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, expect, it, vi } from 'vitest'
import { WorkspaceShell } from './WorkspaceShell'
import { workspacesApi, type Workspace } from './api/workspaces'
import { workspaceRequest, setActiveWorkspace } from './api/workspaceTransport'

vi.mock('./api/workspaces', () => ({
  workspacesApi: { personal: vi.fn(), list: vi.fn(), select: vi.fn(), create: vi.fn() },
}))
vi.mock('./App', () => ({
  default: ({
    workspaceName,
    workspaceHeader,
  }: {
    workspaceName: string
    workspaceHeader: import('react').ReactNode
  }) => (
    <div>
      {workspaceHeader}Contenu : {workspaceName}
    </div>
  ),
}))
const first: Workspace = { id: 'one', name: 'Personnel Alice', type: 1, status: 1, role: 1 }
const second: Workspace = { ...first, id: 'two', name: 'Studio Alice', type: 2 }
beforeEach(() => {
  location.hash = 'workspaces'
  vi.clearAllMocks()
  setActiveWorkspace(null)
  vi.mocked(workspacesApi.personal).mockResolvedValue(first)
  vi.mocked(workspacesApi.list).mockResolvedValue([first, second])
  vi.mocked(workspacesApi.select).mockImplementation(async (id) =>
    id === first.id ? first : second,
  )
})
it('revalidates the original workspace from an OAuth return before displaying its selection', async () => {
  location.hash = 'social-connections?session=discovery&workspace=two'
  render(<WorkspaceShell email="alice@example.test" logout={vi.fn()} busy={false} />)
  expect(await screen.findByText('Contenu : Studio Alice')).toBeInTheDocument()
  expect(workspacesApi.select).toHaveBeenCalledWith('two', expect.any(AbortSignal))
  expect(location.hash).toContain('session=discovery')
  expect(workspaceRequest().id).toBe('two')
})
it('lists accessible spaces and clears all previous content and requests before selecting another', async () => {
  render(<WorkspaceShell email="alice@example.test" logout={vi.fn()} busy={false} />)
  await userEvent.click(await screen.findByRole('button', { name: 'Ouvrir Personnel Alice' }))
  expect(await screen.findByText('Contenu : Personnel Alice')).toBeInTheDocument()
  const previous = workspaceRequest()
  let finish!: (value: Workspace) => void
  vi.mocked(workspacesApi.select).mockReturnValueOnce(
    new Promise((resolve) => {
      finish = resolve
    }),
  )
  await userEvent.selectOptions(screen.getByLabelText('Espace actif'), 'two')
  expect(previous.signal.aborted).toBe(true)
  expect(screen.queryByText('Contenu : Personnel Alice')).not.toBeInTheDocument()
  expect(screen.getByRole('status')).toHaveTextContent('Chargement')
  finish(second)
  expect(await screen.findByText('Contenu : Studio Alice')).toBeInTheDocument()
  expect(workspaceRequest().id).toBe('two')
})
it('never restores old workspace content when a response arrives after leaving', async () => {
  render(<WorkspaceShell email="alice@example.test" logout={vi.fn()} busy={false} />)
  await screen.findByRole('button', { name: 'Ouvrir Personnel Alice' })
  let finish!: (value: Workspace) => void
  vi.mocked(workspacesApi.select).mockReturnValueOnce(
    new Promise((resolve) => {
      finish = resolve
    }),
  )
  await userEvent.click(screen.getByRole('button', { name: 'Ouvrir Personnel Alice' }))
  await userEvent.click(screen.getByRole('button', { name: 'Mes espaces' }))
  finish(first)
  await waitFor(() =>
    expect(screen.queryByText('Contenu : Personnel Alice')).not.toBeInTheDocument(),
  )
  expect(workspaceRequest().id).toBeNull()
})
it('creates an organization and selects the server-authorized result', async () => {
  vi.mocked(workspacesApi.create).mockResolvedValue(second)
  render(<WorkspaceShell email="alice@example.test" logout={vi.fn()} busy={false} />)
  await screen.findByRole('button', { name: 'Créer l’espace' })
  await userEvent.type(screen.getByLabelText('Nom de l’espace'), 'Studio Alice')
  await userEvent.click(screen.getByRole('button', { name: 'Créer l’espace' }))
  expect(workspacesApi.create).toHaveBeenCalledWith('Studio Alice', expect.any(AbortSignal))
  expect(await screen.findByText('Contenu : Studio Alice')).toBeInTheDocument()
})
it('clears the current workspace immediately when access is revoked', async () => {
  render(<WorkspaceShell email="alice@example.test" logout={vi.fn()} busy={false} />)
  await userEvent.click(await screen.findByRole('button', { name: 'Ouvrir Personnel Alice' }))
  await screen.findByText('Contenu : Personnel Alice')
  fireEvent(window, new CustomEvent('workspace-access-lost', { detail: { workspaceId: first.id } }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Accès à cet espace refusé')
  expect(screen.queryByText('Contenu : Personnel Alice')).not.toBeInTheDocument()
  expect(workspaceRequest().id).toBeNull()
  expect(screen.queryByRole('button', { name: 'Ouvrir Personnel Alice' })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Ouvrir Studio Alice' })).toBeInTheDocument()
})

it('starts without an active workspace on reload and does not trust the URL as a selection', async () => {
  location.hash = 'posts'
  setActiveWorkspace('previous')
  render(<WorkspaceShell email="alice@example.test" logout={vi.fn()} busy={false} />)
  await screen.findByRole('button', { name: 'Ouvrir Personnel Alice' })
  expect(workspaceRequest().id).toBeNull()
  expect(screen.getByLabelText('Espace actif')).toHaveValue('')
  expect(workspacesApi.select).not.toHaveBeenCalled()
})
