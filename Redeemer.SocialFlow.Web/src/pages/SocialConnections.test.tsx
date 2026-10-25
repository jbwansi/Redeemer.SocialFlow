import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, expect, it, vi } from 'vitest'
import { SocialConnections } from './SocialConnections'
import { socialConnectionsApi as api, type Connection } from '../api/socialConnections'
vi.mock('../api/socialConnections', () => ({
  socialConnectionsApi: {
    list: vi.fn(),
    providers: vi.fn(),
    candidates: vi.fn(),
    start: vi.fn(),
    select: vi.fn(),
    disconnect: vi.fn(),
    validate: vi.fn(),
  },
}))
const connection: Connection = {
  id: 'connection',
  platform: 1,
  externalAccountId: 'page-1',
  displayName: 'My page',
  accountType: 'Page',
  connectionStatus: 1,
  connectedAtUtc: '2026-01-01',
  expiresAtUtc: null,
  lastValidatedAtUtc: null,
  publishingPermissionGranted: true,
  revision: 'version-one',
}
it('refreshes confirmed LinkedIn permissions only after selecting the returned profile', async () => {
  location.hash = 'social-connections?session=oauth-session'
  const profile: Connection = { ...connection, platform: 2, accountType: 'Profile', publishingPermissionGranted: false }
  vi.mocked(api.list).mockResolvedValue([profile])
  vi.mocked(api.candidates).mockResolvedValue([{ externalAccountId: profile.externalAccountId, displayName: profile.displayName, accountType: 'Profile', publishingPermissionGranted: true }])
  vi.mocked(api.select).mockImplementation(async () => {
    vi.mocked(api.list).mockResolvedValue([{ ...profile, publishingPermissionGranted: true }])
  })
  render(<SocialConnections workspaceId="one" canManage />)
  expect(await screen.findByText(/Reconnectez ce profil/)).toBeInTheDocument()
  expect(screen.queryByText('Permission de publication accordée ; envoi automatique désactivé.')).not.toBeInTheDocument()
  await userEvent.click(screen.getByRole('checkbox'))
  await userEvent.click(screen.getByRole('button', { name: 'Ajouter les comptes sélectionnés' }))
  expect(api.select).toHaveBeenCalledWith('one', 'oauth-session', [profile.externalAccountId])
  expect(await screen.findByText('Permission de publication accordée ; envoi automatique désactivé.')).toBeInTheDocument()
})
beforeEach(() => {
  vi.resetAllMocks()
  location.hash = 'social-connections'
  vi.mocked(api.list).mockResolvedValue([
    connection,
    {
      ...connection,
      id: 'expired',
      platform: 2,
      displayName: 'LinkedIn account',
      connectionStatus: 2,
    },
  ])
  vi.mocked(api.providers).mockResolvedValue([
    { platform: 1, available: true, message: 'Available' },
    { platform: 2, available: false, message: 'Autorisations requises' },
    { platform: 3, available: false, message: 'Autorisations requises' },
  ])
  vi.mocked(api.candidates).mockResolvedValue([])
})
it('shows provider availability, expiration and platform filters', async () => {
  render(<SocialConnections workspaceId="one" canManage />)
  expect(await screen.findByText('Expiré')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Connecter Instagram' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Reconnecter LinkedIn account' })).toBeDisabled()
  await userEvent.selectOptions(screen.getByLabelText('Filtrer par plateforme'), '1')
  expect(screen.queryByText('LinkedIn account')).not.toBeInTheDocument()
  expect(screen.getByText('My page')).toBeInTheDocument()
})
it('keeps Editor and Viewer read-only', async () => {
  render(<SocialConnections workspaceId="one" canManage={false} />)
  await screen.findByText('My page')
  expect(screen.getByText(/Owner ou Admin requis/)).toBeInTheDocument()
  expect(
    screen.queryByRole('button', { name: /Connecter|Reconnecter|Déconnecter/ }),
  ).not.toBeInTheDocument()
})
it('selects several discovered accounts in one workspace', async () => {
  location.hash = 'social-connections?session=discovery'
  vi.mocked(api.candidates).mockResolvedValue([
    {
      externalAccountId: 'p1',
      displayName: 'First page',
      accountType: 'Page',
      publishingPermissionGranted: true,
    },
    {
      externalAccountId: 'p2',
      displayName: 'Second page',
      accountType: 'Page',
      publishingPermissionGranted: false,
    },
  ])
  render(<SocialConnections workspaceId="one" canManage />)
  await userEvent.click(await screen.findByRole('checkbox', { name: /First page/ }))
  await userEvent.click(screen.getByRole('checkbox', { name: /Second page/ }))
  await userEvent.click(screen.getByRole('button', { name: 'Ajouter les comptes sélectionnés' }))
  expect(api.select).toHaveBeenCalledWith('one', 'discovery', ['p1', 'p2'])
  expect(await screen.findByRole('status')).toHaveTextContent('Comptes enregistrés')
  expect(location.hash).toBe('#social-connections')
})
it('requires disconnect confirmation and includes the current revision', async () => {
  render(<SocialConnections workspaceId="one" canManage />)
  await userEvent.click(await screen.findByRole('button', { name: 'Déconnecter My page' }))
  expect(api.disconnect).not.toHaveBeenCalled()
  await userEvent.click(screen.getByRole('button', { name: 'Confirmer la déconnexion' }))
  expect(api.disconnect).toHaveBeenCalledWith('one', connection)
  await waitFor(() =>
    expect(screen.queryByText(/Déconnecter My page de cet espace/)).not.toBeInTheDocument(),
  )
})
it('shows safe provider errors and aborts old workspace requests', async () => {
  vi.mocked(api.start).mockRejectedValue(new Error('Intégration indisponible.'))
  const view = render(<SocialConnections workspaceId="one" canManage />)
  await userEvent.click(await screen.findByRole('button', { name: 'Connecter Facebook' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Intégration indisponible.')
  const signal = vi.mocked(api.list).mock.calls[0][1]!
  view.unmount()
  expect(signal.aborted).toBe(true)
})
