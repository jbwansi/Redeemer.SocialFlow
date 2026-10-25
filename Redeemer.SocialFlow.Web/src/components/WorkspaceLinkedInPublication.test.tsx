import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, expect, it, vi } from 'vitest'
import { PostDetails } from './PostDetails'
import { post } from '../test/fixtures'
import { socialConnectionsApi } from '../api/socialConnections'
import { workspaceLinkedInApi } from '../api/linkedin'
import { ApiError, postsApi } from '../api/posts'

const api = vi.hoisted(() => ({ publication: vi.fn(), preview: vi.fn(), publish: vi.fn() }))
vi.mock('../api/linkedin', async () => ({ ...await vi.importActual('../api/linkedin'), workspaceLinkedInApi: vi.fn(() => api) }))
vi.mock('../api/socialConnections', () => ({ socialConnectionsApi: { list: vi.fn() } }))
beforeEach(() => {
  vi.clearAllMocks()
  api.publication.mockResolvedValue({ operation: null, canPublish: true })
  api.preview.mockResolvedValue({ content: 'Confirmed text', destination: 'urn:li:person:member', confirmation: 'proof' })
  api.publish.mockResolvedValue({ operationId: 'op', state: 2, destination: 'urn:li:person:member', content: 'Confirmed text', completedAt: '2026-01-01', updatedAt: '2026-01-01' })
  vi.spyOn(postsApi, 'get').mockResolvedValue(post({ platform: 2, status: 5 }))
  const c = { id: 'profile', platform: 2 as const, externalAccountId: 'member', displayName: 'Allowed profile', accountType: 'Profile', connectionStatus: 1 as const, connectedAtUtc: '2026-01-01', expiresAtUtc: '2099-01-01', lastValidatedAtUtc: null, publishingPermissionGranted: true, revision: 'r' }
  vi.mocked(socialConnectionsApi.list).mockResolvedValue([c, { ...c, id: 'no-grant', displayName: 'Forbidden profile', publishingPermissionGranted: false }, { ...c, id: 'expired', displayName: 'Expired profile', expiresAtUtc: '2020-01-01' }])
})
function show(canEdit = true, status: 3 | 4 = 4) {
  return render(<PostDetails workspaceId="workspace" localAdministration={false} canEdit={canEdit} post={post({ platform: 2, status, scheduledAt: status === 4 ? '2020-01-01T00:00:00Z' : null })} close={vi.fn()} edit={vi.fn()} changed={vi.fn()} removed={vi.fn()} />)
}
it('requires explicit eligible profile selection and confirmation before sending', async () => {
  show()
  const select = await screen.findByLabelText(/Profil LinkedIn destinataire/)
  await screen.findByRole('option', { name: /Allowed profile/ })
  expect(screen.queryByRole('option', { name: /Forbidden|Expired/ })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Publier sur LinkedIn' })).toBeDisabled()
  await userEvent.selectOptions(select, 'profile')
  expect(workspaceLinkedInApi).toHaveBeenLastCalledWith('profile')
  await userEvent.click(screen.getByRole('button', { name: 'Publier sur LinkedIn' }))
  expect(await screen.findByText('Confirmed text')).toBeInTheDocument()
  expect(api.publish).not.toHaveBeenCalled()
  await userEvent.click(screen.getByRole('button', { name: 'Confirmer la publication' }))
  await waitFor(() => expect(api.publish).toHaveBeenCalledTimes(1))
  expect(await screen.findByText('Publication confirmée')).toBeInTheDocument()
})
it('offers profile selection on an approved post but preserves scheduling requirements', async () => {
  api.publication.mockResolvedValue({ operation: null, canPublish: false })
  show(true, 3)
  expect(await screen.findByLabelText(/Profil LinkedIn destinataire/)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Publier sur LinkedIn' })).toBeDisabled()
  expect(api.publish).not.toHaveBeenCalled()
})
it('does not expose manual publishing to a Viewer', () => {
  show(false)
  expect(screen.getByRole('button', { name: 'Publier sur LinkedIn' })).toBeDisabled()
  expect(screen.getByText(/Votre accès est en lecture seule/)).toBeInTheDocument()
  expect(screen.queryByLabelText(/Profil LinkedIn destinataire/)).not.toBeInTheDocument()
  expect(api.publication).not.toHaveBeenCalled()
})
it('keeps the action visible with an explanation before the server permits publication', async () => {
  api.publication.mockResolvedValue({ operation: null, canPublish: false })
  show()
  expect(await screen.findByText(/Envoi non autorisé par le serveur. Échéance prévue/)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Publier sur LinkedIn' })).toBeDisabled()
})
it('preserves a connection loading error even when the publication request succeeds', async () => {
  vi.mocked(socialConnectionsApi.list).mockRejectedValue(new ApiError({ status: 503, title: 'Profiles unavailable' }))
  show()
  expect(await screen.findByText('Profiles unavailable')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Publier sur LinkedIn' })).toBeDisabled()
  expect(api.publish).not.toHaveBeenCalled()
})
it('shows unavailable workspace publication endpoints instead of silently hiding the panel', async () => {
  api.publication.mockRejectedValue(new ApiError({ status: 404, title: 'Endpoint unavailable' }))
  show()
  expect(await screen.findByText(/Impossible de préparer l’envoi LinkedIn/)).toBeInTheDocument()
  expect(screen.getByText('Endpoint unavailable')).toBeInTheDocument()
  expect(api.publish).not.toHaveBeenCalled()
})
it('keeps an explicit confirmation visible across focus refreshes', async () => {
  show()
  await screen.findByRole('option', { name: /Allowed profile/ })
  await userEvent.selectOptions(screen.getByLabelText(/Profil LinkedIn destinataire/), 'profile')
  await userEvent.click(screen.getByRole('button', { name: 'Publier sur LinkedIn' }))
  await screen.findByRole('button', { name: 'Confirmer la publication' })
  const calls = api.publication.mock.calls.length
  fireEvent.focus(window)
  await waitFor(() => expect(api.publication.mock.calls.length).toBeGreaterThan(calls))
  expect(screen.getByRole('button', { name: 'Confirmer la publication' })).toBeInTheDocument()
  expect(api.publish).not.toHaveBeenCalled()
})
it('keeps uncertain results visible and never retries a send automatically', async () => {
  api.publish.mockRejectedValue(new Error('Network unavailable'))
  show()
  await screen.findByRole('option', { name: /Allowed profile/ })
  await userEvent.selectOptions(screen.getByLabelText(/Profil LinkedIn destinataire/), 'profile')
  await userEvent.click(screen.getByRole('button', { name: 'Publier sur LinkedIn' }))
  await userEvent.click(await screen.findByRole('button', { name: 'Confirmer la publication' }))
  expect(await screen.findByText(/Résultat de la demande non confirmé/)).toBeInTheDocument()
  expect(api.publish).toHaveBeenCalledTimes(1)
})


