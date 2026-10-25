import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, expect, it, vi } from 'vitest'
import { facebookApi, type FacebookRuntime } from '../api/facebook'
import { linkedInApi, type PublicationOperation } from '../api/linkedin'
import { postsApi, ApiError } from '../api/posts'
import { post } from '../test/fixtures'
import { FacebookConnection } from './FacebookConnection'
import { PostDetails } from './PostDetails'
const runtime: FacebookRuntime = {
  connected: true,
  configured: true,
  connectionId: 'connection',
  state: 'connected',
  destination: '123',
  expiresAt: '2030-01-01T00:00:00Z',
  publicationEnabled: true,
  workerEnabled: true,
  pages: [
    { id: '123', name: 'Page One' },
    { id: '456', name: 'Page Two' },
  ],
}
const operation: PublicationOperation = {
  operationId: 'op',
  socialPostId: 'post-1',
  provider: 'Facebook',
  destination: '123',
  content: 'Confirmed text',
  state: 2,
  claimedAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  completedAt: '2026-01-01T00:00:00Z',
  externalId: '123_789',
}
beforeEach(() => {
  vi.mocked(facebookApi.runtime).mockResolvedValue(runtime)
  vi.mocked(facebookApi.publication).mockResolvedValue({ operation: null, canPublish: true })
  vi.spyOn(facebookApi, 'preview').mockResolvedValue({
    content: operation.content,
    destination: '123',
    confirmation: 'proof',
  })
  vi.spyOn(facebookApi, 'publish').mockResolvedValue(operation)
  vi.spyOn(facebookApi, 'connect').mockResolvedValue({
    authorizeUrl: 'https://localhost:65474/api/dev/facebook/authorize?ticket=one-use',
  })
  vi.spyOn(facebookApi, 'select').mockResolvedValue({})
  vi.spyOn(facebookApi, 'disconnect').mockResolvedValue({})
  vi.spyOn(postsApi, 'get').mockResolvedValue(post({ platform: 1, status: 5 }))
  vi.mocked(linkedInApi.publish).mockClear()
})
function details() {
  return render(
    <PostDetails
      post={post({ platform: 1, status: 4, scheduledAt: '2020-01-01T00:00:00Z' })}
      close={vi.fn()}
      edit={vi.fn()}
      changed={vi.fn()}
      removed={vi.fn()}
    />,
  )
}
it('distinguishes a connected account with no eligible Pages', async () => {
  vi.mocked(facebookApi.runtime).mockResolvedValue({
    ...runtime,
    connected: false,
    destination: null,
    state: 'no-pages',
    pages: [],
  })
  const log = vi.spyOn(console, 'info').mockImplementation(() => {})
  render(<FacebookConnection />)
  await screen.findByText('Compte Facebook connecté. Aucune Page accessible pour la publication.')
  expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Utiliser cette Page' })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Reconnecter Facebook' })).toBeEnabled()
  expect(log).toHaveBeenCalledWith('[Facebook] FrontendPagesCount=%d', 0)
})
it('distinguishes available Pages awaiting selection', async () => {
  vi.mocked(facebookApi.runtime).mockResolvedValue({
    ...runtime,
    connected: false,
    destination: null,
    state: 'select-page',
  })
  render(<FacebookConnection />)
  await screen.findByText('Compte Facebook connecté. Sélectionnez une Page de destination.')
  expect(screen.getByRole('option', { name: 'Page One' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Utiliser cette Page' })).toBeDisabled()
})
it('connects explicitly, offers Pages and disconnects without browser token storage', async () => {
  const storage = vi.spyOn(Storage.prototype, 'setItem')
  render(<FacebookConnection />)
  await screen.findByText('Connecté à la Page 123')
  expect(facebookApi.connect).not.toHaveBeenCalled()
  await userEvent.click(
    screen.getByRole('button', { name: /Connecter Facebook|Reconnecter Facebook/ }),
  )
  expect(
    await screen.findByRole('link', { name: 'Ouvrir l’autorisation Facebook' }),
  ).toHaveAttribute('href', 'https://localhost:65474/api/dev/facebook/authorize?ticket=one-use')
  await userEvent.selectOptions(screen.getByLabelText('Page Facebook de destination'), '456')
  await userEvent.click(screen.getByRole('button', { name: 'Utiliser cette Page' }))
  expect(facebookApi.select).toHaveBeenCalledWith('connection', '456')
  await userEvent.click(screen.getByRole('button', { name: 'Déconnecter Facebook' }))
  expect(facebookApi.disconnect).toHaveBeenCalledWith('connection')
  expect(storage).not.toHaveBeenCalled()
})
it('shows expiration and API errors', async () => {
  vi.mocked(facebookApi.runtime).mockResolvedValue({
    ...runtime,
    connected: false,
    state: 'expired',
  })
  render(<FacebookConnection />)
  await screen.findByText('Connexion Facebook expirée. Reconnectez-vous.')
  expect(screen.getByRole('button', { name: 'Utiliser cette Page' })).toBeDisabled()
  vi.mocked(facebookApi.runtime).mockRejectedValue(new ApiError({ title: 'Facebook indisponible' }))
  fireEvent.focus(window)
  await screen.findByText('Facebook indisponible')
})
it('keeps the panel visible after a 404 and recovers on refresh', async () => {
  vi.mocked(facebookApi.runtime).mockRejectedValueOnce(new ApiError({ status: 404 }))
  render(<FacebookConnection />)
  await screen.findByText(/Connexion Facebook indisponible/)
  expect(screen.getByLabelText('Facebook Development')).toBeInTheDocument()
  expect(
    screen.getByRole('button', { name: /Connecter Facebook|Reconnecter Facebook/ }),
  ).toBeDisabled()
  await userEvent.click(screen.getByRole('button', { name: 'Actualiser Facebook' }))
  await screen.findByText('Connecté à la Page 123')
  expect(
    screen.getByRole('button', { name: /Connecter Facebook|Reconnecter Facebook/ }),
  ).toBeEnabled()
  expect(screen.queryByText(/Connexion Facebook indisponible/)).not.toBeInTheDocument()
  expect(facebookApi.connect).not.toHaveBeenCalled()
})
it('allows connection when publication is disabled', async () => {
  vi.mocked(facebookApi.runtime).mockResolvedValue({
    ...runtime,
    publicationEnabled: false,
    workerEnabled: false,
  })
  render(<FacebookConnection />)
  await screen.findByText('Publication Facebook désactivée côté serveur.')
  expect(
    screen.getByRole('button', { name: /Connecter Facebook|Reconnecter Facebook/ }),
  ).toBeEnabled()
})
it('requires preview and explicit confirmation and displays Facebook result', async () => {
  details()
  await screen.findByRole('button', { name: 'Publier sur Facebook' })
  expect(facebookApi.publish).not.toHaveBeenCalled()
  await userEvent.click(screen.getByRole('button', { name: 'Publier sur Facebook' }))
  await screen.findByLabelText('Confirmer la publication Facebook')
  expect(facebookApi.publish).not.toHaveBeenCalled()
  await userEvent.click(screen.getByRole('button', { name: 'Confirmer la publication' }))
  await screen.findByText('Publication confirmée')
  await screen.findByText('Identifiant Facebook : 123_789')
  expect(facebookApi.publish).toHaveBeenCalledTimes(1)
  expect(linkedInApi.publish).not.toHaveBeenCalled()
})
it('shows an uncertain result without automatically resubmitting', async () => {
  vi.mocked(facebookApi.publish).mockRejectedValue(new ApiError({ title: 'Connexion interrompue' }))
  details()
  await userEvent.click(await screen.findByRole('button', { name: 'Publier sur Facebook' }))
  await userEvent.click(await screen.findByRole('button', { name: 'Confirmer la publication' }))
  await screen.findByText(/Résultat de la demande non confirmé/)
  await userEvent.click(screen.getByRole('button', { name: 'Actualiser la publication' }))
  expect(facebookApi.publish).toHaveBeenCalledTimes(1)
})
it('disables publication when the workflow is not eligible', async () => {
  vi.mocked(facebookApi.publication).mockResolvedValue({ operation: null, canPublish: false })
  details()
  await screen.findByLabelText('Publication Facebook')
  await waitFor(() =>
    expect(screen.queryByText('Chargement de l’opération…')).not.toBeInTheDocument(),
  )
  expect(screen.queryByRole('button', { name: 'Publier sur Facebook' })).not.toBeInTheDocument()
})
