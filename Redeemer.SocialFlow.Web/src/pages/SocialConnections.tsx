import { useEffect, useState } from 'react'
import {
  socialConnectionsApi as api,
  type Connection,
  type Candidate,
  type Provider,
  type DiscoveryDiagnostic,
  type PublishingCapability,
} from '../api/socialConnections'
import { platforms, type Platform } from '../api/posts'
const statuses = { 1: 'Connecté', 2: 'Expiré', 3: 'Déconnecté', 4: 'Autorisation à renouveler' }
const capabilityReasons: Record<string, string> = {
  PageTaskEvidenceUnavailable: 'Meta ne fournit pas de preuve exploitable du droit de créer du contenu sur cette Page. La publication reste bloquée. Reconnectez la Page si le contexte de vérification est ancien.',
  PublishingScopeMissing: 'Une permission de publication requise n’est pas accordée.',
  PageCreateContentTaskMissing: 'Les tâches retournées par Meta ne comprennent pas la création de contenu.',
  PageCreateContentTaskVerified: 'Meta a confirmé une tâche de création de contenu pour cette Page.',
  AuthorizationInvalid: 'L’autorisation est invalide ou expirée. Reconnectez ce compte.',
  ConnectionRevoked: 'La connexion a été révoquée dans cet espace.',
  MetaVerificationUnavailable: 'La vérification Meta a échoué. La publication reste bloquée ; réessayez la validation.',
  LegacyGrant: 'État hérité de la connexion existante ; aucune nouvelle vérification Meta n’est attestée.',
}
function CapabilityDiagnostic({ evidence }: { evidence: PublishingCapability }) {
  return <span>
    <strong>{evidence.status === 1 ? 'Capacité vérifiée (Verified)' : evidence.status === 2 ? 'Publication refusée (Denied)' : 'Capacité non vérifiée (Unverified)'}</strong>
    {' · '}{capabilityReasons[evidence.reason] ?? 'Consultez les autorisations de cette connexion.'}
    {' '}<code>{evidence.reason}</code>
    {' · Source : '}{evidence.source}
    {evidence.checkedAtUtc && <> · Vérification : {new Date(evidence.checkedAtUtc).toLocaleString()}</>}
    {evidence.task && <> · Tâche confirmée : {evidence.task}</>}
  </span>
}
const discoveryMessages: Record<string, string> = {
  graph_error: 'Meta a renvoyé une erreur Graph malgré un statut HTTP de succès. Consultez les codes du diagnostic.',
  no_pages: 'Meta n’a retourné aucune Page pour cette autorisation.',
  missing_discovery_permission: 'Permission de découverte pages_show_list non accordée. La liste des Pages n’a pas été demandée.',
  pages_filtered: 'Des Pages ont été reçues mais aucune n’a été retenue. Consultez les exclusions ci-dessous.',
  pages_without_publishing: 'Pages disponibles pour connexion, sans droit de publication confirmé. Vous pouvez les sélectionner.',
  pages_available: 'Pages disponibles pour sélection. Les droits de publication sont indiqués pour chaque Page.',
  http_error: 'Meta a retourné une erreur HTTP. Aucun compte n’a été enregistré par ce parcours.',
  network_error: 'La communication avec Meta a échoué. Recommencez la connexion.',
  invalid_response: 'La réponse de Meta est invalide ou incomplète. Recommencez la connexion.',
  authorization_invalid: 'L’autorisation ou le jeton ne passe pas les contrôles de validité. Reconnectez le compte.',
  pagination_limit: 'La limite de pagination a été atteinte. Aucun résultat partiel ne peut être sélectionné.',
}
const exclusionMessages: Record<string, string> = {
  missing_page_token: 'Jeton de Page absent',
  missing_page_id: 'Identifiant de Page absent',
  duplicate_page_id: 'Identifiant de Page déjà reçu',
}
const pageOutcomes: Record<string, string> = {
  ...exclusionMessages,
  retained: 'Retenue',
  retained_publishing_unverified: 'Retenue — capacités de publication non vérifiées, publication bloquée',
  retained_without_publishing: 'Retenue sans droit de publication',
  validation_not_completed: 'Reçue, validation non terminée',
}
const discoveryStages: Record<string, string> = {
  authorized_pages_validation: 'vérification des autorisations granulaires',
  authorized_page: 'découverte complémentaire', authorized_page_validation: 'validation de la Page autorisée',
  token_exchange: 'échange du code', user_validation: 'validation du jeton utilisateur',
  permissions: 'vérification des permissions', pages: 'liste des Pages',
  page_validation: 'validation d’une Page', pagination: 'pagination', complete: 'terminé',
}
export function SocialConnections({
  workspaceId,
  canManage,
}: {
  workspaceId: string
  canManage: boolean
}) {
  const [connections, setConnections] = useState<Connection[]>([])
  const [providers, setProviders] = useState<Provider[]>([])
  const [candidates, setCandidates] = useState<Candidate[]>([])
  const [diagnostic, setDiagnostic] = useState<DiscoveryDiagnostic | null>(null)
  const [diagnosticError, setDiagnosticError] = useState('')
  const [selected, setSelected] = useState<string[]>([])
  const [session, setSession] = useState(() =>
    new URLSearchParams(location.hash.split('?')[1]).get('session'),
  )
  const [sessionPlatform, setSessionPlatform] = useState(() => new URLSearchParams(location.hash.split('?')[1]).get('platform'))
  const [filter, setFilter] = useState('')
  const [revision, refresh] = useState(0)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [disconnect, setDisconnect] = useState<Connection>()
  useEffect(() => {
    const syncSession = () => {
      const query = new URLSearchParams(location.hash.split('?')[1])
      setSession(query.get('session'))
      setSessionPlatform(query.get('platform'))
    }
    window.addEventListener('hashchange', syncSession)
    return () => window.removeEventListener('hashchange', syncSession)
  }, [])
  useEffect(() => {
    const controller = new AbortController()
    setLoading(true)
    setDiagnostic(null)
    setDiagnosticError('')
    setError('')
    setCandidates([])
    setSelected([])
    Promise.allSettled([
      api.list(workspaceId, controller.signal),
      api.providers(workspaceId, controller.signal),
      session && canManage
        ? api.candidates(workspaceId, session, controller.signal)
        : Promise.resolve([]),
      session && canManage ? api.diagnostics(workspaceId, session, controller.signal) : Promise.resolve(null),
    ])
      .then(([items, available, choices, report]) => {
        if (!controller.signal.aborted) {
          setConnections(items.status === 'fulfilled' ? items.value : [])
          setProviders(available.status === 'fulfilled' ? available.value : [])
          setCandidates(choices.status === 'fulfilled' ? choices.value : [])
          setDiagnostic(report.status === 'fulfilled' ? report.value ?? null : null)
          if (report.status === 'rejected') setDiagnosticError('Le diagnostic n’a pas pu être chargé. Rechargez la page ou recommencez la connexion.')
          const failure = [items, available, choices].find((result) => result.status === 'rejected')
          if (failure?.status === 'rejected') setError(failure.reason instanceof Error ? failure.reason.message : 'Connexions indisponibles.')
        }
      })
      .catch((e) => {
        if (!controller.signal.aborted)
          setError(e instanceof Error ? e.message : 'Connexions indisponibles.')
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => controller.abort()
  }, [workspaceId, revision, session, canManage])
  async function run(action: () => Promise<void>) {
    setBusy(true)
    setError('')
    setMessage('')
    try {
      await action()
      refresh((x) => x + 1)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Opération impossible.')
    } finally {
      setBusy(false)
    }
  }
  async function connect(platform: Platform) {
    await run(async () => {
      const { authorizationUrl } = await api.start(workspaceId, platform)
      const url = new URL(authorizationUrl)
      if (
        url.protocol !== 'https:' ||
        !['www.linkedin.com', 'www.facebook.com'].includes(url.hostname)
      )
        throw new Error('Adresse du fournisseur invalide.')
      location.assign(url.href)
    })
  }
  return (
    <section aria-label="Connexions sociales">
      <p>Connectez les comptes de cet espace. La publication automatique reste désactivée.</p>
      {!canManage && (
        <p>Consultation uniquement : Owner ou Admin requis pour gérer les connexions.</p>
      )}
      {error && <p role="alert">{error}</p>}
      {message && <p role="status">{message}</p>}
      {session && canManage && (sessionPlatform === null || sessionPlatform === '1') && (
        <section aria-labelledby="facebook-discovery-title" className="workspace-create">
          <h2 id="facebook-discovery-title">Diagnostic de découverte Facebook</h2>
          {loading ? <p role="status">Chargement du diagnostic…</p> : diagnostic ? (
            <>
              <p>{discoveryMessages[diagnostic.outcome] ?? 'Diagnostic de découverte indisponible.'}</p>
              <p>Pages reçues : {diagnostic.pagesReceived} · Pages retenues : {diagnostic.pagesRetained} · Sans droit de publication : {diagnostic.pagesWithoutPublishing}</p>
              <p>Requêtes de liste : {diagnostic.requests} · Étape : {discoveryStages[diagnostic.stage] ?? 'inconnue'}</p>
              {!diagnostic.complete && <p>Découverte incomplète : compteurs partiels, aucune sélection possible.</p>}
              {diagnostic.httpStatus && <p>Statut HTTP : {diagnostic.httpStatus}</p>}
              {diagnostic.facebook && <details>
                <summary>Diagnostic Facebook détaillé</summary>
                <p>App ID effectif : {diagnostic.facebook.clientId ?? 'non disponible'} · LoginConfigId effectif : {diagnostic.facebook.loginConfigId ?? 'absent ou invalide'}</p>
                <p>Type de jeton : {diagnostic.facebook.tokenType ?? 'non vérifié'} · App ID correspondant : {diagnostic.facebook.appIdMatches == null ? 'non vérifié' : diagnostic.facebook.appIdMatches ? 'oui' : 'non'}</p>
                <ul>{([
                  ['pages_show_list', diagnostic.facebook.pagesShowList],
                  ['pages_read_engagement', diagnostic.facebook.pagesReadEngagement],
                  ['pages_manage_posts', diagnostic.facebook.pagesManagePosts],
                ] as const).map(([permission, granted]) => <li key={permission}>{permission} : {granted == null ? 'non vérifiée' : granted ? 'accordée' : 'non accordée'}</li>)}</ul>
                <p>Autorisations granulaires du jeton utilisateur : {diagnostic.facebook.granular?.shape ?? 'non vérifiées'}</p>
                <ul>{diagnostic.facebook.granular?.permissions.map(permission => <li key={permission.permission}>
                  {permission.permission} · ressources ciblées : {permission.targetsStatus}
                  {permission.targetIds.length > 0 && ` · IDs : ${permission.targetIds.join(', ')}`}
                </li>)}</ul>
                <p>Comparez ces IDs à l’identifiant numérique de la Page, pas à son nom ni à l’App ID. Des données absentes ou invalides ne prouvent pas un refus d’accès. Ces observations ne donnent pas de droit de publication.</p>
                <ul>{diagnostic.facebook.responses?.map((response, index) => <li key={index}>
                  {discoveryStages[response.stage] ?? response.stage} · HTTP {response.httpStatus} · data : {response.dataShape} · erreur Graph : {response.graphError ? 'oui' : 'non'}
                  {response.graphCode != null && ` · code ${response.graphCode}`}{response.graphSubcode != null && ` · sous-code ${response.graphSubcode}`}
                </li>)}</ul>
              </details>}
              {Object.entries(diagnostic.exclusions).map(([reason, count]) => (
                <p key={reason}>{exclusionMessages[reason] ?? 'Autre exclusion'} : {count}</p>
              ))}
              {!!diagnostic.pages?.length && (
                <>
                  <h3>Pages retournées par Meta</h3>
                  <ul>{diagnostic.pages.map((page, index) => (
                    <li key={index}>Page {page.externalPageId ?? '(identifiant indisponible)'} : {pageOutcomes[page.outcome] ?? 'Traitement non déterminé'}</li>
                  ))}</ul>
                  <p>Si la découverte est complète et tous les identifiants disponibles, une Page absente de cette liste n’a pas été retournée par Meta. Les exclusions SocialFlow sont indiquées pour les Pages reçues.</p>
                </>
              )}
            </>
          ) : (
            <p role="status">{diagnosticError || 'Aucun diagnostic Facebook reçu pour cette session. S’il s’agit d’une connexion Facebook, recommencez le parcours avec la version actuelle de l’API.'}</p>
          )}
        </section>
      )}
      {loading ? (
        <p role="status">Chargement des connexions…</p>
      ) : (
        <>
          {canManage && (
            <div className="workspace-cards">
              {providers.map((provider) => (
                <article key={provider.platform}>
                  <h2>{platforms[provider.platform]}</h2>
                  <p id={`provider-reason-${provider.platform}`}>{provider.message}</p>
                  <button
                    className="button"
                    disabled={busy || !provider.available}
                    aria-describedby={`provider-reason-${provider.platform}`}
                    onClick={() => void connect(provider.platform)}
                  >
                    Connecter {platforms[provider.platform]}
                  </button>
                </article>
              ))}
            </div>
          )}
          {session && canManage && (
            <section className="workspace-create">
              <h2>Choisir les comptes autorisés</h2>
              {candidates.length === 0 && !diagnostic && (
                <p>
                  Aucune destination compatible. Vérifiez les comptes et permissions accordés au
                  fournisseur.
                </p>
              )}
              {candidates.map((candidate) => (
                <label key={candidate.externalAccountId}>
                  <input
                    type="checkbox"
                    checked={selected.includes(candidate.externalAccountId)}
                    onChange={(e) =>
                      setSelected((ids) =>
                        e.target.checked
                          ? [...ids, candidate.externalAccountId]
                          : ids.filter((x) => x !== candidate.externalAccountId),
                      )
                    }
                  />
                  {candidate.displayName} · {candidate.accountType}
                  {candidate.publishingCapability ? <CapabilityDiagnostic evidence={candidate.publishingCapability} /> : !candidate.publishingPermissionGranted &&
                    ' · Permission de publication non accordée'}
                </label>
              ))}
              <button
                className="button primary"
                disabled={busy || selected.length === 0}
                onClick={() =>
                  void run(async () => {
                    await api.select(workspaceId, session, selected)
                    setSession(null)
                    setSelected([])
                    history.replaceState(null, '', '#social-connections')
                    setMessage('Comptes enregistrés dans cet espace.')
                  })
                }
              >
                Ajouter les comptes sélectionnés
              </button>
              <button
                className="button"
                disabled={busy}
                onClick={() => {
                  setSession(null)
                  history.replaceState(null, '', '#social-connections')
                }}
              >
                Annuler la sélection
              </button>
            </section>
          )}
          <label>
            Filtrer par plateforme
            <select value={filter} onChange={(e) => setFilter(e.target.value)}>
              <option value="">Toutes</option>
              {Object.entries(platforms).map(([id, name]) => (
                <option key={id} value={id}>
                  {name}
                </option>
              ))}
            </select>
          </label>
          {connections.length === 0 && <p>Aucun compte connecté dans cet espace.</p>}
          <div className="workspace-cards">
            {connections
              .filter((c) => !filter || String(c.platform) === filter)
              .map((c) => (
                <article key={c.id}>
                  <h2>{c.displayName}</h2>
                  <p>
                    {platforms[c.platform]} · {c.accountType}
                  </p>
                  <p>Identifiant du compte : {c.externalAccountId}</p>
                  <p>{statuses[c.connectionStatus]}</p>
                  {c.expiresAtUtc && (
                    <p>Expiration : {new Date(c.expiresAtUtc).toLocaleString()}</p>
                  )}
                  <p>
                    {c.platform === 1 && c.publishingCapability ? <CapabilityDiagnostic evidence={c.publishingCapability} /> : c.publishingPermissionGranted
                      ? 'Permission de publication accordée ; envoi automatique désactivé.'
                      : 'Permission de publication non accordée.'}
                  </p>
                  {c.platform === 2 && c.accountType === 'Profile' && !c.publishingPermissionGranted && canManage && (
                    <p>Reconnectez ce profil pour demander la permission de publication LinkedIn, puis sélectionnez-le au retour pour enregistrer les droits accordés.</p>
                  )}
                  {canManage && (
                    <>
                      <button
                        className="button"
                        disabled={
                          busy || !providers.find((p) => p.platform === c.platform)?.available
                        }
                        onClick={() => void connect(c.platform)}
                      >
                        Reconnecter {c.displayName}
                      </button>
                      {c.connectionStatus === 1 && (
                        <button
                          className="button"
                          disabled={busy}
                          onClick={() =>
                            void run(async () => {
                              try { await api.validate(workspaceId, c) }
                              finally {
                                // A failed Meta check may have persisted Unverified. Show it while retaining the error.
                                setConnections(await api.list(workspaceId))
                              }
                              setMessage('État de connexion actualisé.')
                            })
                          }
                        >
                          Vérifier / renouveler
                        </button>
                      )}
                      {c.connectionStatus !== 3 && (
                        <button
                          className="button danger"
                          disabled={busy}
                          onClick={() => setDisconnect(c)}
                        >
                          Déconnecter {c.displayName}
                        </button>
                      )}
                    </>
                  )}
                </article>
              ))}
          </div>
          {disconnect && (
            <div role="alert">
              <p>
                Déconnecter {disconnect.displayName} de cet espace ? Les autres espaces restent
                inchangés.
              </p>
              <button
                className="button danger"
                disabled={busy}
                onClick={() =>
                  void run(async () => {
                    await api.disconnect(workspaceId, disconnect)
                    setDisconnect(undefined)
                    setMessage('Compte déconnecté de cet espace.')
                  })
                }
              >
                Confirmer la déconnexion
              </button>
              <button className="button" disabled={busy} onClick={() => setDisconnect(undefined)}>
                Annuler
              </button>
            </div>
          )}
        </>
      )}
    </section>
  )
}
