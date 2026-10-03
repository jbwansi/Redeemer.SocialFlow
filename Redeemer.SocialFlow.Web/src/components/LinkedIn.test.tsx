import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { linkedInApi, type LinkedInRuntime, type PublicationOperation } from '../api/linkedin'
import { ApiError, postsApi } from '../api/posts'
import { post } from '../test/fixtures'
import { LinkedInConnection } from './LinkedInConnection'
import { PostDetails } from './PostDetails'

const runtime: LinkedInRuntime = {
  connected: true,
  destination: 'urn:li:person:member',
  expiresAt: '2030-01-01T00:00:00Z',
  publicationEnabled: true,
  workerEnabled: false,
  workerIntervalSeconds: 30,
  connectUrl: 'https://localhost:65474/api/dev/linkedin/connect',
}
const operation: PublicationOperation = {
  operationId: 'op-1',
  socialPostId: 'post-1',
  provider: 'LinkedIn',
  destination: 'urn:li:person:member',
  content: 'Contenu confirmé',
  state: 2,
  claimedAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  completedAt: '2026-01-01T00:00:00Z',
  externalId: 'urn:li:share:123',
}
function details(changed = vi.fn()) {
  return render(
    <PostDetails
      post={post({ status: 4, scheduledAt: '2020-01-01T00:00:00Z' })}
      close={vi.fn()}
      edit={vi.fn()}
      changed={changed}
      removed={vi.fn()}
    />,
  )
}
beforeEach(() => {
  vi.mocked(linkedInApi.runtime).mockResolvedValue(runtime)
  vi.mocked(linkedInApi.publication).mockResolvedValue({ operation: null, canPublish: true })
  vi.mocked(linkedInApi.preview).mockResolvedValue({
    content: operation.content,
    destination: operation.destination,
    confirmation: 'server-proof',
  })
  vi.mocked(linkedInApi.publish).mockResolvedValue(operation)
  vi.mocked(linkedInApi.publish).mockClear()
  vi.spyOn(postsApi, 'get').mockResolvedValue(post({ status: 5 }))
})

describe('LinkedIn connection', () => {
  it('uses a browser navigation link and refreshes after returning; shows expiration and worker state', async () => {
    render(<LinkedInConnection />)
    const link = await screen.findByRole('link', { name: 'Connecter LinkedIn' })
    expect(link).toHaveAttribute('href', runtime.connectUrl)
    expect(link).toHaveAttribute('target', '_blank')
    expect(screen.getByText(/Expiration/)).toBeInTheDocument()
    expect(screen.getByText(/Publication automatique désactivée/)).toHaveTextContent(
      'L’API doit rester en fonctionnement',
    )
    const before = vi.mocked(linkedInApi.runtime).mock.calls.length
    fireEvent.focus(window)
    await waitFor(() => expect(linkedInApi.runtime).toHaveBeenCalledTimes(before + 1))
  })
  it('shows loading, expired connection and disabled publishing', async () => {
    vi.mocked(linkedInApi.runtime).mockResolvedValue({
      ...runtime,
      connected: false,
      publicationEnabled: false,
    })
    render(<LinkedInConnection />)
    expect(screen.getByRole('status')).toHaveTextContent('Chargement')
    expect(await screen.findByText('Connexion LinkedIn expirée.')).toBeInTheDocument()
    expect(screen.getByText('Publication LinkedIn désactivée côté serveur.')).toBeInTheDocument()
  })
  it('shows ProblemDetails errors', async () => {
    vi.mocked(linkedInApi.runtime).mockRejectedValue(new ApiError({ title: 'Statut indisponible' }))
    render(<LinkedInConnection />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Statut indisponible')
  })
  it('hides on server 404 and outside frontend Development', async () => {
    vi.mocked(linkedInApi.runtime).mockRejectedValue(new ApiError({ status: 404 }))
    const { unmount } = render(<LinkedInConnection />)
    await waitFor(() =>
      expect(screen.queryByLabelText('LinkedIn Development')).not.toBeInTheDocument(),
    )
    unmount()
    vi.stubEnv('DEV', false)
    vi.mocked(linkedInApi.runtime).mockClear()
    render(<LinkedInConnection />)
    expect(linkedInApi.runtime).not.toHaveBeenCalled()
    expect(screen.queryByLabelText('LinkedIn Development')).not.toBeInTheDocument()
  })
})

describe('LinkedIn publication', () => {
  it('requires confirmation of exact server text and destination; cancel sends nothing', async () => {
    details()
    await userEvent.click(await screen.findByRole('button', { name: 'Publier sur LinkedIn' }))
    expect(screen.getByText('Contenu confirmé')).toBeInTheDocument()
    expect(
      screen.getByText(/Publier ce texte sur le profil urn:li:person:member/),
    ).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Annuler' }))
    expect(linkedInApi.publish).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Cancel schedule' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'OK' })).toBeInTheDocument()
  })
  it('prevents double clicks, sends only the confirmation and refreshes a confirmed post', async () => {
    let complete!: (value: PublicationOperation) => void
    vi.mocked(linkedInApi.publish).mockImplementation(
      () =>
        new Promise((resolve) => {
          complete = resolve
        }),
    )
    const changed = vi.fn()
    details(changed)
    await userEvent.click(await screen.findByRole('button', { name: 'Publier sur LinkedIn' }))
    const confirm = screen.getByRole('button', { name: 'Confirmer la publication' })
    fireEvent.click(confirm)
    fireEvent.click(confirm)
    expect(linkedInApi.publish).toHaveBeenCalledTimes(1)
    expect(linkedInApi.publish).toHaveBeenCalledWith('post-1', 'server-proof')
    expect(screen.getByRole('button', { name: 'Publication en cours…' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'OK' })).toBeDisabled()
    complete(operation)
    expect(await screen.findByText('Publication confirmée')).toBeInTheDocument()
    await waitFor(() => expect(changed).toHaveBeenCalledWith(post({ status: 5 })))
  })
  it.each([1, 3] as const)(
    'shows durable outcome %s after reload without offering retry',
    async (state) => {
      vi.mocked(linkedInApi.publication).mockResolvedValue({
        operation: { ...operation, state, externalId: null },
        canPublish: false,
      })
      details()
      expect(
        await screen.findByText(state === 1 ? /Publication indéterminée/ : /Échec certain/),
      ).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Publier sur LinkedIn' })).not.toBeInTheDocument()
      expect(screen.getByText('Opération : op-1')).toBeInTheDocument()
      expect(linkedInApi.publish).not.toHaveBeenCalled()
    },
  )
  it('handles transport uncertainty without retry and reloads the durable result', async () => {
    details()
    await userEvent.click(await screen.findByRole('button', { name: 'Publier sur LinkedIn' }))
    vi.mocked(linkedInApi.publish).mockRejectedValue(new ApiError({ title: 'Connexion perdue' }))
    vi.mocked(linkedInApi.publication).mockResolvedValue({
      operation: { ...operation, state: 1 },
      canPublish: false,
    })
    await userEvent.click(screen.getByRole('button', { name: 'Confirmer la publication' }))
    expect(await screen.findByText(/Résultat de la demande non confirmé/)).toBeInTheDocument()
    expect(screen.getByText('Connexion perdue')).toBeInTheDocument()
    expect(await screen.findByText(/Publication indéterminée/)).toBeInTheDocument()
    expect(linkedInApi.publish).toHaveBeenCalledTimes(1)
  })
  it('does not offer publishing when the server marks the post ineligible', async () => {
    vi.mocked(linkedInApi.publication).mockResolvedValue({ operation: null, canPublish: false })
    details()
    await waitFor(() =>
      expect(screen.queryByText('Chargement de l’opération…')).not.toBeInTheDocument(),
    )
    expect(screen.queryByRole('button', { name: 'Publier sur LinkedIn' })).not.toBeInTheDocument()
  })
})
