import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, expect, it, vi } from 'vitest'
import { PostDetails } from './PostDetails'
import { post } from '../test/fixtures'
import { socialConnectionsApi } from '../api/socialConnections'
import { workspaceFacebookApi } from '../api/facebook'
import { postsApi } from '../api/posts'

const api = vi.hoisted(() => ({ publication: vi.fn(), preview: vi.fn(), publish: vi.fn(), authorize: vi.fn(), cancelAutomatic: vi.fn() }))
vi.mock('../api/facebook', async () => ({ ...await vi.importActual('../api/facebook'), workspaceFacebookApi: vi.fn(() => api) }))
vi.mock('../api/socialConnections', () => ({ socialConnectionsApi: { list: vi.fn() } }))
beforeEach(() => {
  vi.clearAllMocks()
  api.publication.mockResolvedValue({ operation: null, canPublish: true, blockReason: 'automatic_not_authorized' })
  api.preview.mockResolvedValue({ content: 'Facebook text', destination: '123', confirmation: 'proof' })
  api.publish.mockResolvedValue({ operationId: 'op', state: 2, destination: '123', content: 'Facebook text', completedAt: '2026-01-01', updatedAt: '2026-01-01' })
  api.authorize.mockResolvedValue(undefined)
  api.cancelAutomatic.mockResolvedValue(undefined)
  vi.spyOn(postsApi, 'get').mockResolvedValue(post({ platform: 1, status: 5 }))
  const c = { id: 'page', platform: 1 as const, externalAccountId: '123', displayName: 'Allowed Page', accountType: 'Page', connectionStatus: 1 as const, connectedAtUtc: '2026-01-01', expiresAtUtc: null, lastValidatedAtUtc: null, publishingPermissionGranted: true, revision: 'r' }
  vi.mocked(socialConnectionsApi.list).mockResolvedValue([c, { ...c, id: 'unverified', displayName: 'Unverified Page', publishingPermissionGranted: false }, { ...c, id: 'expired', displayName: 'Expired Page', expiresAtUtc: '2020-01-01' }])
})
function show(status: 3 | 4 = 4) {
  return render(<PostDetails workspaceId="workspace" localAdministration={false} canEdit post={post({ platform: 1, status, scheduledAt: status === 4 ? '2020-01-01T00:00:00Z' : null })} close={vi.fn()} edit={vi.fn()} changed={vi.fn()} removed={vi.fn()} />)
}
it('prepares an Approved post immediately with explicit selection and no scheduling', async () => {
  api.publication.mockResolvedValue({ operation: null, canPublish: true, manualBlockReason: null, workerEnabled: false })
  show(3)
  await screen.findByRole('option', { name: /Allowed Page/ })
  expect(screen.getByRole('button', { name: 'Publier sur Facebook' })).toBeDisabled()
  await userEvent.selectOptions(screen.getByLabelText(/Page Facebook destinataire/), 'page')
  await userEvent.click(screen.getByRole('button', { name: 'Publier sur Facebook' }))
  expect(await screen.findByRole('button', { name: 'Confirmer la publication' })).toBeEnabled()
  expect(api.authorize).not.toHaveBeenCalled()
  expect(api.publish).not.toHaveBeenCalled()
  expect(screen.queryByText(/approuvé puis planifié/)).not.toBeInTheDocument()
})
it.each(['publication_disabled', 'scheduled_not_due', 'approval_required'])('keeps the manual server blocker %s independent of destination selection', async reason => {
  api.publication.mockResolvedValue({ operation: null, canPublish: false, manualBlockReason: reason })
  show(reason === 'scheduled_not_due' ? 4 : 3)
  await screen.findByRole('option', { name: /Allowed Page/ })
  await userEvent.selectOptions(screen.getByLabelText(/Page Facebook destinataire/), 'page')
  expect(screen.getByRole('button', { name: 'Publier sur Facebook' })).toBeDisabled()
  expect(api.preview).not.toHaveBeenCalled()
})
it('explains missing Meta task evidence and excludes explicit Unverified even with a legacy grant', async () => {
  const items = await socialConnectionsApi.list('workspace')
  vi.mocked(socialConnectionsApi.list).mockResolvedValue([{ ...items[0], publishingCapability: {
    status: 0, reason: 'PageTaskEvidenceUnavailable', source: 'Meta', checkedAtUtc: null, task: null,
  } }])
  api.publication.mockResolvedValue({ operation: null, canPublish: false, manualBlockReason: 'publishing_permission_unverified' })
  show(3)
  expect(await screen.findByText(/Unverified — PageTaskEvidenceUnavailable/)).toBeVisible()
  expect(screen.queryByRole('option', { name: /Allowed Page/ })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Publier sur Facebook' })).toBeDisabled()
  expect(screen.queryByText(/Échéance prévue/)).not.toBeInTheDocument()
})
it('uses the Facebook workspace API with explicit Page selection and confirmation', async () => {
  show()
  await screen.findByRole('option', { name: /Allowed Page/ })
  expect(screen.queryByRole('option', { name: /Unverified|Expired/ })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Publier sur Facebook' })).toBeDisabled()
  await userEvent.selectOptions(screen.getByLabelText(/Page Facebook destinataire/), 'page')
  expect(workspaceFacebookApi).toHaveBeenLastCalledWith('page')
  await userEvent.click(screen.getByRole('button', { name: 'Publier sur Facebook' }))
  await screen.findByText('Facebook text')
  expect(api.publish).not.toHaveBeenCalled()
  await userEvent.click(screen.getByRole('button', { name: 'Confirmer la publication' }))
  await waitFor(() => expect(api.publish).toHaveBeenCalledTimes(1))
  expect(await screen.findByText('Publication confirmée')).toBeVisible()
})
it('authorizes scheduling separately without publishing immediately', async () => {
  api.publication.mockResolvedValue({ operation: null, canPublish: false, blockReason: 'automatic_not_authorized' })
  show()
  await screen.findByRole('option', { name: /Allowed Page/ })
  await userEvent.selectOptions(screen.getByLabelText(/Page Facebook destinataire/), 'page')
  await userEvent.click(screen.getByRole('button', { name: 'Autoriser la publication Facebook à échéance' }))
  expect(api.authorize).toHaveBeenCalledTimes(1)
  expect(api.publish).not.toHaveBeenCalled()
})
it('shows an explicit blocked diagnostic for unverified capabilities', async () => {
  vi.mocked(socialConnectionsApi.list).mockResolvedValue([])
  api.publication.mockResolvedValue({ operation: null, canPublish: false, blockReason: 'publishing_permission_unverified' })
  show()
  expect(await screen.findByText(/Publication bloquée : permissions de publication non vérifiées/)).toBeVisible()
  expect(screen.getByRole('button', { name: 'Publier sur Facebook' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Autoriser la publication Facebook à échéance' })).toBeDisabled()
})
it('allows withdrawing automatic consent and exposes a blocked preflight', async () => {
  api.publication.mockResolvedValue({ operation: null, canPublish: true, automaticAuthorized: true, workerEnabled: false, blockReason: 'worker_disabled' })
  show()
  expect(await screen.findByText(/Traitement automatique désactivé/)).toBeVisible()
  await userEvent.click(screen.getByRole('button', { name: 'Retirer l’autorisation automatique' }))
  expect(api.cancelAutomatic).toHaveBeenCalledTimes(1)
  expect(api.publish).not.toHaveBeenCalled()
})
