import { useEffect, useState } from 'react'
import {
  socialConnectionsApi as api,
  type Connection,
  type Candidate,
  type Provider,
} from '../api/socialConnections'
import { platforms, type Platform } from '../api/posts'
const statuses = { 1: 'Connecté', 2: 'Expiré', 3: 'Déconnecté', 4: 'Autorisation à renouveler' }
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
  const [selected, setSelected] = useState<string[]>([])
  const [session, setSession] = useState(() =>
    new URLSearchParams(location.hash.split('?')[1]).get('session'),
  )
  const [filter, setFilter] = useState('')
  const [revision, refresh] = useState(0)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [disconnect, setDisconnect] = useState<Connection>()
  useEffect(() => {
    const controller = new AbortController()
    setLoading(true)
    Promise.all([
      api.list(workspaceId, controller.signal),
      api.providers(workspaceId, controller.signal),
      session && canManage
        ? api.candidates(workspaceId, session, controller.signal)
        : Promise.resolve([]),
    ])
      .then(([items, available, choices]) => {
        if (!controller.signal.aborted) {
          setConnections(items)
          setProviders(available)
          setCandidates(choices)
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
      {loading ? (
        <p role="status">Chargement des connexions…</p>
      ) : (
        <>
          {canManage && (
            <div className="workspace-cards">
              {providers.map((provider) => (
                <article key={provider.platform}>
                  <h2>{platforms[provider.platform]}</h2>
                  <p>{provider.message}</p>
                  <button
                    className="button"
                    disabled={busy || !provider.available}
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
              {candidates.length === 0 && (
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
                  {!candidate.publishingPermissionGranted &&
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
                  <p>{statuses[c.connectionStatus]}</p>
                  {c.expiresAtUtc && (
                    <p>Expiration : {new Date(c.expiresAtUtc).toLocaleString()}</p>
                  )}
                  <p>
                    {c.publishingPermissionGranted
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
                              await api.validate(workspaceId, c)
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
