import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, expect, it, vi } from 'vitest'
import { SocialConnections } from './SocialConnections'
import { socialConnectionsApi as api, type Connection } from '../api/socialConnections'
vi.mock('../api/socialConnections', () => ({
  socialConnectionsApi: {
    list: vi.fn(),
    providers: vi.fn(),
    candidates: vi.fn(),
    diagnostics: vi.fn(),
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
it.each([
  [0, 'PageTaskEvidenceUnavailable', 'Capacité non vérifiée (Unverified)'],
  [1, 'PageCreateContentTaskVerified', 'Capacité vérifiée (Verified)'],
  [2, 'PageCreateContentTaskMissing', 'Publication refusée (Denied)'],
] as const)('shows Facebook capability state %i with safe evidence', async (status, reason, label) => {
  vi.mocked(api.list).mockResolvedValue([{ ...connection, publishingPermissionGranted: status === 1,
    publishingCapability: { status, reason, source: 'MetaUserAccounts', checkedAtUtc: '2026-11-08T10:00:00Z', task: status === 1 ? 'PROFILE_PLUS_CREATE_CONTENT' : null } }])
  render(<SocialConnections workspaceId="one" canManage />)
  expect(await screen.findByText(label)).toBeVisible()
  expect(screen.getByText(reason)).toBeVisible()
  expect(screen.getByText(/Source :/)).toHaveTextContent('MetaUserAccounts')
  if (status === 0) expect(screen.getByText(/Meta ne fournit pas de preuve/)).toBeVisible()
  if (status === 1) expect(screen.getByText(/Tâche confirmée/)).toHaveTextContent('PROFILE_PLUS_CREATE_CONTENT')
})
it('shows the missing task diagnostic on a selectable Facebook candidate', async () => {
  location.hash = 'social-connections?session=discovery&platform=1'
  vi.mocked(api.candidates).mockResolvedValue([{ externalAccountId: '123', displayName: 'Fallback Page', accountType: 'Page', publishingPermissionGranted: false,
    publishingCapability: { status: 0, reason: 'PageTaskEvidenceUnavailable', source: 'MetaGranularDiscovery', checkedAtUtc: '2026-11-08T10:00:00Z', task: null } }])
  render(<SocialConnections workspaceId="one" canManage />)
  expect(await screen.findByText('PageTaskEvidenceUnavailable')).toBeVisible()
  expect(screen.getByRole('checkbox', { name: /Fallback Page/ })).toBeEnabled()
})
it('refreshes persisted Unverified evidence after a failed Meta validation while keeping the error', async () => {
  vi.mocked(api.list).mockResolvedValueOnce([connection]).mockResolvedValue([{ ...connection, publishingPermissionGranted: false,
    publishingCapability: { status: 0, reason: 'MetaVerificationUnavailable', source: 'MetaValidation', checkedAtUtc: '2026-11-08T10:00:00Z', task: null } }])
  vi.mocked(api.validate).mockRejectedValueOnce(new Error('Meta indisponible'))
  render(<SocialConnections workspaceId="one" canManage />)
  await userEvent.click(await screen.findByRole('button', { name: 'Vérifier / renouveler' }))
  expect(await screen.findByText('MetaVerificationUnavailable')).toBeVisible()
  expect(await screen.findByRole('alert')).toHaveTextContent('Meta indisponible')
})
it('keeps distinct provider identities with the same display name visible', async () => {
  vi.mocked(api.list).mockResolvedValue([connection, { ...connection, id: 'other', externalAccountId: 'page-2' }])
  render(<SocialConnections workspaceId="one" canManage />)
  expect(await screen.findAllByRole('heading', { name: 'My page' })).toHaveLength(2)
  expect(screen.getByText('Identifiant du compte : page-1')).toBeInTheDocument()
  expect(screen.getByText('Identifiant du compte : page-2')).toBeInTheDocument()
})
it('refreshes a reconnected identity as one card with its current state', async () => {
  location.hash = 'social-connections?session=reconnect'
  vi.mocked(api.list).mockResolvedValue([{ ...connection, connectionStatus: 3, publishingPermissionGranted: false }])
  vi.mocked(api.candidates).mockResolvedValue([{ externalAccountId: 'page-1', displayName: 'My page', accountType: 'Page', publishingPermissionGranted: true }])
  vi.mocked(api.select).mockImplementation(async () => { vi.mocked(api.list).mockResolvedValue([connection]) })
  render(<SocialConnections workspaceId="one" canManage />)
  await screen.findByText('Déconnecté')
  await userEvent.click(screen.getByRole('checkbox'))
  await userEvent.click(screen.getByRole('button', { name: 'Ajouter les comptes sélectionnés' }))
  await screen.findByText('Connecté')
  expect(screen.queryByText('Déconnecté')).not.toBeInTheDocument()
  expect(screen.getAllByRole('heading', { name: 'My page' })).toHaveLength(1)
})
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
  vi.mocked(api.diagnostics).mockResolvedValue(null)
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
it.each([0, 1, 3])('shows a visible diagnostic heading and API counts for %i Pages', async (count) => {
  location.hash = 'social-connections?session=discovery&platform=1'
  vi.mocked(api.diagnostics).mockResolvedValue({ outcome: count ? 'pages_without_publishing' : 'no_pages', stage: 'complete', pagesReceived: count, pagesRetained: count, pagesWithoutPublishing: count, requests: 1, complete: true, httpStatus: null, exclusions: {} })
  vi.mocked(api.candidates).mockResolvedValue(Array.from({ length: count }, (_, i) => ({ externalAccountId: `page-${i}`, displayName: `Page ${i}`, accountType: 'Page', publishingPermissionGranted: false })))
  render(<SocialConnections workspaceId="one" canManage />)
  expect(await screen.findByRole('heading', { name: 'Diagnostic de découverte Facebook' })).toBeVisible()
  expect(await screen.findByText(`Pages reçues : ${count} · Pages retenues : ${count} · Sans droit de publication : ${count}`)).toBeVisible()
  expect(screen.queryAllByRole('checkbox')).toHaveLength(count)
  expect(api.diagnostics).toHaveBeenCalledWith('one', 'discovery', expect.any(AbortSignal))
})
it('keeps a received diagnostic visible even when loading the connections fails', async () => {
  location.hash = 'social-connections?session=discovery&platform=1'
  vi.mocked(api.list).mockRejectedValue(new Error('Liste indisponible'))
  vi.mocked(api.diagnostics).mockResolvedValue({ outcome: 'no_pages', stage: 'complete', pagesReceived: 0, pagesRetained: 0, pagesWithoutPublishing: 0, requests: 1, complete: true, httpStatus: null, exclusions: {} })
  render(<SocialConnections workspaceId="one" canManage />)
  expect(await screen.findByText('Liste indisponible')).toBeVisible()
  expect(screen.getByText('Meta n’a retourné aucune Page pour cette autorisation.')).toBeVisible()
})
it('identifies a received but excluded Page separately from a retained Page', async () => {
  location.hash = 'social-connections?session=discovery&platform=1'
  vi.mocked(api.diagnostics).mockResolvedValue({ outcome: 'pages_available', stage: 'complete', pagesReceived: 2, pagesRetained: 1, pagesWithoutPublishing: 0, requests: 1, complete: true, httpStatus: null, exclusions: { missing_page_token: 1 }, pages: [{ externalPageId: '100', outcome: 'retained' }, { externalPageId: '200', outcome: 'missing_page_token' }] })
  render(<SocialConnections workspaceId="one" canManage />)
  expect(await screen.findByText('Page 100 : Retenue')).toBeVisible()
  expect(screen.getByText('Page 200 : Jeton de Page absent')).toBeVisible()
  expect(screen.getByText(/une Page absente de cette liste n’a pas été retournée par Meta/)).toBeVisible()
})
it('reloads diagnostics when a callback session changes without remounting', async () => {
  render(<SocialConnections workspaceId="one" canManage />)
  await screen.findByText('My page')
  expect(api.diagnostics).not.toHaveBeenCalled()
  vi.mocked(api.diagnostics).mockResolvedValue({ outcome: 'no_pages', stage: 'complete', pagesReceived: 0, pagesRetained: 0, pagesWithoutPublishing: 0, requests: 1, complete: true, httpStatus: null, exclusions: {} })
  act(() => {
    location.hash = 'social-connections?session=new-callback&platform=1'
    window.dispatchEvent(new HashChangeEvent('hashchange'))
  })
  expect(await screen.findByText('Meta n’a retourné aucune Page pour cette autorisation.')).toBeVisible()
  expect(api.diagnostics).toHaveBeenCalledWith('one', 'new-callback', expect.any(AbortSignal))
})
it('explains a missing diagnostic response rather than hiding the panel', async () => {
  location.hash = 'social-connections?session=legacy-session&platform=1'
  render(<SocialConnections workspaceId="one" canManage />)
  expect(await screen.findByText(/Aucun diagnostic Facebook reçu/)).toBeVisible()
  expect(screen.getByRole('heading', { name: 'Diagnostic de découverte Facebook' })).toBeVisible()
})
it('does not label a LinkedIn callback as a Facebook diagnostic', async () => {
  location.hash = 'social-connections?session=linkedin-session&platform=2'
  render(<SocialConnections workspaceId="one" canManage />)
  await screen.findByText('My page')
  expect(screen.queryByRole('heading', { name: 'Diagnostic de découverte Facebook' })).not.toBeInTheDocument()
})
it('distinguishes filtered Pages from an empty provider response and hides diagnostics from readers', async () => {
  location.hash = 'social-connections?session=discovery'
  vi.mocked(api.diagnostics).mockResolvedValue({ outcome: 'pages_filtered', stage: 'complete', pagesReceived: 2, pagesRetained: 0, pagesWithoutPublishing: 0, requests: 1, complete: true, httpStatus: null, exclusions: { missing_page_token: 2 } })
  const view = render(<SocialConnections workspaceId="one" canManage />)
  expect(await screen.findByText(/Pages reçues : 2/)).toBeInTheDocument()
  expect(screen.getByText('Jeton de Page absent : 2')).toBeInTheDocument()
  expect(screen.queryByText(/Aucune destination compatible/)).not.toBeInTheDocument()
  view.rerender(<SocialConnections workspaceId="one" canManage={false} />)
  await screen.findByText('My page')
  expect(screen.queryByRole('region', { name: 'Diagnostic de découverte Facebook' })).not.toBeInTheDocument()
})
it('keeps a Page without publishing permission selectable with a precise diagnostic', async () => {
  location.hash = 'social-connections?session=discovery'
  vi.mocked(api.diagnostics).mockResolvedValue({ outcome: 'pages_without_publishing', stage: 'complete', pagesReceived: 1, pagesRetained: 1, pagesWithoutPublishing: 1, requests: 1, complete: true, httpStatus: null, exclusions: {} })
  vi.mocked(api.candidates).mockResolvedValue([{ externalAccountId: 'page-1', displayName: 'Page sans publication', accountType: 'Page', publishingPermissionGranted: false }])
  render(<SocialConnections workspaceId="one" canManage />)
  expect(await screen.findByText(/Pages disponibles pour connexion, sans droit/)).toBeInTheDocument()
  expect(screen.queryByText(/Aucune destination compatible/)).not.toBeInTheDocument()
  await userEvent.click(screen.getByRole('checkbox'))
  expect(screen.getByRole('button', { name: 'Ajouter les comptes sélectionnés' })).toBeEnabled()
})
it('shows safe HTTP failure details without suggesting a successful connection', async () => {
  location.hash = 'social-connections?session=failed-discovery'
  vi.mocked(api.diagnostics).mockResolvedValue({ outcome: 'http_error', stage: 'pages', pagesReceived: 0, pagesRetained: 0, pagesWithoutPublishing: 0, requests: 1, complete: false, httpStatus: 403, exclusions: {} })
  render(<SocialConnections workspaceId="one" canManage />)
  expect(await screen.findByText('Statut HTTP : 403')).toBeInTheDocument()
  expect(screen.getByText(/aucune sélection possible/)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Ajouter les comptes sélectionnés' })).toBeDisabled()
})
it('explains that a complementary Page has unverified publishing capabilities', async () => {
  location.hash = 'social-connections?session=discovery&platform=1'
  vi.mocked(api.diagnostics).mockResolvedValue({ outcome: 'pages_without_publishing', stage: 'complete', pagesReceived: 1, pagesRetained: 1, pagesWithoutPublishing: 1, requests: 2, complete: true, httpStatus: null, exclusions: {}, pages: [{ externalPageId: '810001', outcome: 'retained_publishing_unverified' }] })
  vi.mocked(api.candidates).mockResolvedValue([{ externalAccountId: '810001', displayName: 'Page autorisée', accountType: 'Page', publishingPermissionGranted: false }])
  render(<SocialConnections workspaceId="one" canManage />)
  expect(await screen.findByText(/capacités de publication non vérifiées, publication bloquée/)).toBeVisible()
  await userEvent.click(screen.getByRole('checkbox'))
  expect(screen.getByRole('button', { name: 'Ajouter les comptes sélectionnés' })).toBeEnabled()
})
it('shows Graph errors in HTTP 200 with effective configuration and permission evidence', async () => {
  location.hash = 'social-connections?session=failed-discovery'
  vi.mocked(api.diagnostics).mockResolvedValue({ outcome: 'graph_error', stage: 'pages', pagesReceived: 0, pagesRetained: 0, pagesWithoutPublishing: 0, requests: 1, complete: false, httpStatus: 200, exclusions: {}, facebook: {
    clientId: '1145889561364859', loginConfigId: '2140120956587865', tokenType: 'USER', appIdMatches: true,
    pagesShowList: true, pagesReadEngagement: false, pagesManagePosts: null,
    granular: { shape: 'array', permissions: [
      { permission: 'pages_show_list', targetsStatus: 'array', targetIds: ['810001'] },
      { permission: 'pages_read_engagement', targetsStatus: 'empty_array', targetIds: [] },
      { permission: 'pages_manage_posts', targetsStatus: 'missing', targetIds: [] },
    ] },
    responses: [{ stage: 'pages', httpStatus: 200, dataShape: 'empty_array', graphError: true, graphCode: 190, graphSubcode: 463 }],
  } })
  render(<SocialConnections workspaceId="one" canManage />)
  expect(await screen.findByText(/Meta a renvoyé une erreur Graph/)).toBeInTheDocument()
  expect(screen.getByText(/App ID effectif : 1145889561364859/)).toBeInTheDocument()
  expect(screen.getByText(/LoginConfigId effectif : 2140120956587865/)).toBeInTheDocument()
  expect(screen.getByText('pages_read_engagement : non accordée')).toBeInTheDocument()
  expect(screen.getByText('pages_manage_posts : non vérifiée')).toBeInTheDocument()
  expect(screen.getByText(/code 190/)).toHaveTextContent('empty_array')
  expect(screen.getByText(/pages_show_list · ressources ciblées/)).toHaveTextContent('IDs : 810001')
  expect(screen.getByText(/pages_read_engagement · ressources ciblées/)).toHaveTextContent('empty_array')
  expect(screen.getByText(/pages_manage_posts · ressources ciblées/)).toHaveTextContent('missing')
  expect(screen.getByRole('button', { name: 'Ajouter les comptes sélectionnés' })).toBeDisabled()
})
it('explains disabled Facebook configuration to managers and hides details from readers', async () => {
  const message = 'Identifiants Facebook SaaS manquants : UseLegacyClientCredentials.'
  vi.mocked(api.providers).mockResolvedValue([{ platform: 1, available: false, message }])
  const view = render(<SocialConnections workspaceId="one" canManage />)
  const button = await screen.findByRole('button', { name: 'Connecter Facebook' })
  expect(button).toBeDisabled()
  expect(button).toHaveAccessibleDescription(message)
  await userEvent.click(button)
  expect(api.start).not.toHaveBeenCalled()
  view.rerender(<SocialConnections workspaceId="one" canManage={false} />)
  await screen.findByText('My page')
  expect(screen.queryByText(message)).not.toBeInTheDocument()
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
