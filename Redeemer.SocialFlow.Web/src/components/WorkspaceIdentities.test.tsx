import { useState } from 'react'
import { beforeEach, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { workspaceIdentitiesApi, type LibraryIdentity } from '../api/visualIdentities'
import * as posts from '../api/posts'
import { emptyIdentity } from '../lib/visualIdentities'
import { WorkspaceIdentityPicker } from './WorkspaceIdentityPicker'
import { VisualIdentityLibrary } from './VisualIdentityLibrary'
import { WorkspaceEditorialProfile } from './WorkspaceEditorialProfile'

const entry: LibraryIdentity = { id: 'a', identityId: 'A', name: 'Organization A', style: 'Flat', colors: ['#123456'],
  revision: 2, isActive: true, isDefault: true, logoId: null, workspaceId: 'workspace-a', approvalId: null }
let entries: LibraryIdentity[]
beforeEach(() => {
  entries = [{ ...entry }]
  vi.spyOn(workspaceIdentitiesApi, 'list').mockImplementation(async () => entries)
  vi.spyOn(workspaceIdentitiesApi, 'approve').mockImplementation(async () => { entries = [{ ...entry, approvalId: 'proof' }] })
})
function Picker() {
  const [value, onChange] = useState(emptyIdentity)
  return <WorkspaceIdentityPicker value={value} onChange={onChange} />
}
function Library({ canManage = true }: { canManage?: boolean }) {
  const [value, onChange] = useState(emptyIdentity)
  return <VisualIdentityLibrary value={value} onChange={onChange} workspace canManage={canManage} />
}
it('requires an explicit temporary choice and never offers unapproved or inactive identities', async () => {
  entries.push({ ...entry, id: 'inactive', name: 'Inactive', isActive: false, approvalId: 'old' })
  render(<Picker />)
  await waitFor(() => expect(workspaceIdentitiesApi.list).toHaveBeenCalled())
  expect(screen.queryByRole('option', { name: /Organization A/ })).not.toBeInTheDocument()
  expect(screen.queryByRole('option', { name: /Inactive/ })).not.toBeInTheDocument()
  expect(screen.queryByLabelText('Nom de l’identité')).not.toBeInTheDocument()
  await userEvent.click(screen.getByRole('checkbox'))
  expect(screen.getByLabelText('Nom de l’identité')).toHaveValue('')
  expect(screen.queryByText('Redeemer Holding')).not.toBeInTheDocument()
})
it('selects the exact approved revision without exposing mutable approved fields', async () => {
  entries[0].approvalId = 'proof'
  render(<Picker />)
  await screen.findByRole('option', { name: /Organization A/ })
  await userEvent.selectOptions(screen.getByRole('combobox'), 'a')
  expect(screen.getByText(/Approuvée · espace workspace-a · révision 2/)).toBeInTheDocument()
  expect(screen.queryByLabelText('Style visuel')).not.toBeInTheDocument()
})
it('approves only the saved revision and displays the resulting state', async () => {
  render(<Library />)
  const approve = await screen.findByRole('button', { name: 'Approuver cette révision enregistrée' })
  await userEvent.type(screen.getByLabelText('Style visuel'), ' unsaved')
  expect(approve).toBeDisabled()
  await userEvent.click(screen.getByRole('button', { name: 'Rétablir les paramètres enregistrés' }))
  await userEvent.click(approve)
  await waitFor(() => expect(workspaceIdentitiesApi.approve).toHaveBeenCalledWith('a', 2, expect.any(AbortSignal)))
  await screen.findByText(/révision 2 · Active · Approuvée/)
})
it('lets a read-only member browse identities but cannot edit or approve', async () => {
  render(<Library canManage={false} />)
  await screen.findByDisplayValue('Organization A')
  expect(screen.getByRole('combobox')).toBeEnabled()
  expect(screen.getByLabelText('Nom de l’identité')).toBeDisabled()
  expect(screen.queryByRole('button', { name: /Approuver/ })).not.toBeInTheDocument()
})
it('shows neutral fallback and saves the profile with its revision', async () => {
  const api = vi.spyOn(posts, 'apiRequest').mockResolvedValueOnce({ workspaceId: 'workspace-b', profile: null })
    .mockResolvedValueOnce({ revision: 1, organizationName: 'Organization B', tone: 'Warm' })
  render(<WorkspaceEditorialProfile canManage />)
  await screen.findByText(/workspace-b/)
  expect(screen.getByText(/Profil absent/)).toBeInTheDocument()
  await userEvent.type(screen.getByLabelText('Nom de l’organisation'), 'Organization B')
  await userEvent.type(screen.getByLabelText('Ton rédactionnel'), 'Warm')
  await userEvent.click(screen.getByRole('button', { name: 'Enregistrer le profil éditorial' }))
  await screen.findByText(/Profil configuré · révision 1/)
  expect(JSON.parse(api.mock.calls[1][1]!.body as string)).toMatchObject({ revision: 0, organizationName: 'Organization B', tone: 'Warm' })
})

it('preselects only the active approved default on its exact current revision', async () => {
  entries[0].approvalId = 'proof'
  render(<Picker />)
  await waitFor(() => expect(screen.getByRole('combobox')).toHaveValue('a'))
  expect(screen.getByText(/Approuvée · espace workspace-a · révision 2/)).toBeInTheDocument()
})
it.each([401, 403, 500])('distinguishes API %s failures from a successful empty list', async status => {
  vi.mocked(workspaceIdentitiesApi.list).mockRejectedValue(new posts.ApiError({ status, title: 'API indisponible' }))
  render(<Picker />)
  await screen.findByText('API indisponible')
  expect(screen.queryByText(/Aucune révision graphique active/)).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Actualiser les identités' })).toBeEnabled()
})
it('does not report an empty catalog while loading', () => {
  vi.mocked(workspaceIdentitiesApi.list).mockImplementation(() => new Promise(() => {}))
  render(<Picker />)
  expect(screen.getByText(/Chargement des identités graphiques approuvées/)).toBeInTheDocument()
  expect(screen.queryByText(/Aucune révision graphique active/)).not.toBeInTheDocument()
})
it('refreshes after approval and selects the newly approved default', async () => {
  render(<Picker />)
  await screen.findByText(/Aucune révision graphique active/)
  entries = [{ ...entry, approvalId: 'proof' }]
  await userEvent.click(screen.getByRole('button', { name: 'Actualiser les identités' }))
  await waitFor(() => expect(screen.getByRole('combobox')).toHaveValue('a'))
})
it('preserves explicit selection, deselection and temporary edits across refreshes', async () => {
  entries = [{ ...entry, approvalId: 'a-proof' }, { ...entry, id: 'b', name: 'B', isDefault: false, approvalId: 'b-proof' }]
  render(<Picker />)
  await waitFor(() => expect(screen.getByRole('combobox')).toHaveValue('a'))
  await userEvent.selectOptions(screen.getByRole('combobox'), 'b')
  await userEvent.click(screen.getByRole('button', { name: 'Actualiser les identités' }))
  await waitFor(() => expect(screen.getByRole('combobox')).toBeEnabled())
  expect(screen.getByRole('combobox')).toHaveValue('b')
  await userEvent.selectOptions(screen.getByRole('combobox'), '')
  await userEvent.click(screen.getByRole('button', { name: 'Actualiser les identités' }))
  await waitFor(() => expect(screen.getByRole('combobox')).toBeEnabled())
  expect(screen.getByRole('combobox')).toHaveValue('')
  await userEvent.click(screen.getByRole('checkbox'))
  await userEvent.type(screen.getByLabelText('Nom de l’identité'), 'Brouillon temporaire')
  await userEvent.click(screen.getByRole('button', { name: 'Actualiser les identités' }))
  await waitFor(() => expect(screen.getByRole('combobox')).toBeEnabled())
  expect(screen.getByLabelText('Nom de l’identité')).toHaveValue('Brouillon temporaire')
})
it('does not replace a temporary choice made before a slow initial load completes', async () => {
  let finish!: (entries: LibraryIdentity[]) => void
  vi.mocked(workspaceIdentitiesApi.list).mockImplementation(() => new Promise(resolve => { finish = resolve }))
  render(<Picker />)
  await userEvent.click(screen.getByRole('checkbox'))
  await userEvent.type(screen.getByLabelText('Nom de l’identité'), 'Saisie en cours')
  finish([{ ...entry, approvalId: 'proof' }])
  await waitFor(() => expect(screen.getByRole('combobox')).toBeEnabled())
  expect(screen.getByLabelText('Nom de l’identité')).toHaveValue('Saisie en cours')
})
it('keeps an obsolete selection as a warning rather than replacing its unsaved context', async () => {
  entries[0].approvalId = 'proof'
  render(<Picker />)
  await waitFor(() => expect(screen.getByRole('combobox')).toHaveValue('a'))
  entries = [{ ...entry, revision: 3, approvalId: null }]
  await userEvent.click(screen.getByRole('button', { name: 'Actualiser les identités' }))
  await screen.findByText(/La sélection précédente n’est plus/)
  expect(screen.queryByText(/Approuvée · espace/)).not.toBeInTheDocument()
  expect(screen.queryByRole('option', { name: /Organization A/ })).not.toBeInTheDocument()
})
it('ignores a late response from the previous workspace on remount', async () => {
  let finish!: (entries: LibraryIdentity[]) => void
  vi.mocked(workspaceIdentitiesApi.list).mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
    .mockResolvedValueOnce([{ ...entry, id: 'b', workspaceId: 'workspace-b', approvalId: 'b-proof' }])
  const view = render(<Picker key="workspace-a" />)
  view.rerender(<Picker key="workspace-b" />)
  await waitFor(() => expect(screen.getByRole('combobox')).toHaveValue('b'))
  finish([{ ...entry, approvalId: 'a-proof' }])
  await waitFor(() => expect(screen.getByRole('combobox')).toHaveValue('b'))
  expect(screen.getByText(/espace workspace-b/)).toBeInTheDocument()
})

it('offers several approved identities without choosing an arbitrary non-default', async () => {
  entries = [{ ...entry, isDefault: false, approvalId: 'a-proof' }, { ...entry, id: 'b', name: 'B', isDefault: false, approvalId: 'b-proof' }]
  render(<Picker />)
  await screen.findByRole('option', { name: /Organization A/ })
  expect(screen.getByRole('option', { name: /B · révision/ })).toBeInTheDocument()
  expect(screen.getByRole('combobox')).toHaveValue('')
})
it('allows explicitly adopting a newly approved revision without silently updating the old choice', async () => {
  entries[0].approvalId = 'proof'
  render(<Picker />)
  await waitFor(() => expect(screen.getByRole('combobox')).toHaveValue('a'))
  entries = [{ ...entry, revision: 3, approvalId: 'proof-v3' }]
  await userEvent.click(screen.getByRole('button', { name: 'Actualiser les identités' }))
  await screen.findByText(/La sélection précédente n’est plus/)
  expect(screen.getByRole('combobox')).toHaveValue('__previous')
  await userEvent.selectOptions(screen.getByRole('combobox'), 'a')
  expect(screen.getByText(/Approuvée · espace workspace-a · révision 3/)).toBeInTheDocument()
})
