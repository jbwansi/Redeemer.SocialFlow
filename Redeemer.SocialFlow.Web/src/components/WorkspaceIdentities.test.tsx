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
