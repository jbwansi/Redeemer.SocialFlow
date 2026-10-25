import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import { InvitationAcceptance } from './InvitationAcceptance'
import { teamApi } from './api/team'
vi.mock('./api/team', () => ({ teamApi: { accept: vi.fn() } }))
it('accepts only on explicit action and keeps invalid invitations on screen', async () => {
  const done = vi.fn()
  vi.mocked(teamApi.accept)
    .mockRejectedValueOnce(new Error('Invitation expirée.'))
    .mockResolvedValueOnce({ workspaceId: 'org' })
  render(
    <InvitationAcceptance code="secret" email="bob@example.test" done={done} logout={vi.fn()} />,
  )
  expect(teamApi.accept).not.toHaveBeenCalled()
  await userEvent.click(screen.getByRole('button', { name: 'Accepter l’invitation' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Invitation expirée.')
  expect(done).not.toHaveBeenCalled()
  await userEvent.click(screen.getByRole('button', { name: 'Accepter l’invitation' }))
  expect(done).toHaveBeenCalled()
})
