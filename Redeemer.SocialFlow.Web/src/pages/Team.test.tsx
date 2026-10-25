import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, expect, it, vi } from 'vitest'
import { Team } from './Team'
import { teamApi, type Team as TeamData, type Role } from '../api/team'
vi.mock('../api/team', async () => ({
  ...(await vi.importActual('../api/team')),
  teamApi: {
    get: vi.fn(),
    invite: vi.fn(),
    change: vi.fn(),
    remove: vi.fn(),
    revoke: vi.fn(),
    leave: vi.fn(),
  },
}))
const owner = {
  userId: 'a',
  email: 'alice@example.test',
  role: 1 as Role,
  revision: 'owner-version',
}
const editor = {
  userId: 'b',
  email: 'bob@example.test',
  role: 2 as Role,
  revision: 'editor-version',
}
const data: TeamData = {
  myRole: 1,
  members: [owner, editor],
  invitations: [
    {
      id: 'invite',
      email: 'new@example.test',
      role: 4,
      revision: 'invite-version',
      expiresAtUtc: '2026-10-25T16:00:00Z',
    },
  ],
}
beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(teamApi.get).mockResolvedValue(data)
})
it('invites with a selected role and confirms role changes using the current revision', async () => {
  render(<Team workspaceId="org" email={owner.email} left={vi.fn()} />)
  await screen.findByText('Inviter un collaborateur')
  await userEvent.type(screen.getByLabelText('Email'), 'new@example.test')
  await userEvent.selectOptions(screen.getByLabelText('Rôle'), '4')
  await userEvent.click(screen.getByRole('button', { name: 'Envoyer l’invitation' }))
  expect(teamApi.invite).toHaveBeenCalledWith('org', 'new@example.test', 4)
  await userEvent.selectOptions(screen.getByLabelText('Rôle de bob@example.test'), '3')
  expect(teamApi.change).not.toHaveBeenCalled()
  await userEvent.click(screen.getByRole('button', { name: 'Confirmer' }))
  expect(teamApi.change).toHaveBeenCalledWith('org', editor, 3)
})
it.each([2, 4] as Role[])('keeps role %s read-only for team administration', async (role) => {
  vi.mocked(teamApi.get).mockResolvedValue({ ...data, myRole: role, invitations: [] })
  render(<Team workspaceId="org" email={editor.email} left={vi.fn()} />)
  await screen.findByText(editor.email)
  expect(screen.queryByText('Inviter un collaborateur')).not.toBeInTheDocument()
  expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Quitter l’espace' })).toBeInTheDocument()
})
it('hides Owner controls from Admin and shows stale version failures', async () => {
  vi.mocked(teamApi.get).mockResolvedValue({ ...data, myRole: 3 })
  vi.mocked(teamApi.change).mockRejectedValue(new Error('Les données ont changé.'))
  render(<Team workspaceId="org" email={editor.email} left={vi.fn()} />)
  await screen.findByText(owner.email)
  expect(screen.queryByLabelText('Rôle de alice@example.test')).not.toBeInTheDocument()
  expect(screen.queryByRole('option', { name: 'Owner' })).not.toBeInTheDocument()
  await userEvent.selectOptions(screen.getByLabelText('Rôle de bob@example.test'), '4')
  await userEvent.click(screen.getByRole('button', { name: 'Confirmer' }))
  expect(await screen.findByText('Les données ont changé.')).toBeInTheDocument()
})
it('confirms revocation and leaving, and cancels reads on unmount', async () => {
  const left = vi.fn()
  const view = render(<Team workspaceId="org" email={editor.email} left={left} />)
  await userEvent.click(await screen.findByRole('button', { name: 'Annuler l’invitation' }))
  await userEvent.click(screen.getByRole('button', { name: 'Confirmer' }))
  expect(teamApi.revoke).toHaveBeenCalledWith('org', data.invitations[0])
  await userEvent.click(screen.getByRole('button', { name: 'Quitter l’espace' }))
  await userEvent.click(screen.getByRole('button', { name: 'Confirmer' }))
  await waitFor(() => expect(left).toHaveBeenCalled())
  const signal = vi.mocked(teamApi.get).mock.calls.at(-1)![1]!
  view.unmount()
  expect(signal.aborted).toBe(true)
})
