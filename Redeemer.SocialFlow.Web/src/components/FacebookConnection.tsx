import { useEffect, useState } from 'react'
import { facebookApi, type FacebookRuntime } from '../api/facebook'
import { ApiError } from '../api/posts'
import { ErrorNotice } from './shared'
export function FacebookConnection() {
  const [runtime, setRuntime] = useState<FacebookRuntime>()
  const [error, setError] = useState<unknown>()
  const [unavailable, setUnavailable] = useState(false)
  const [busy, setBusy] = useState(false)
  const [revision, setRevision] = useState(0)
  const [page, setPage] = useState('')
  const [authorizeUrl, setAuthorizeUrl] = useState('')
  useEffect(() => {
    if (!import.meta.env.DEV) return
    const controller = new AbortController()
    void facebookApi
      .runtime(controller.signal)
      .then((value) => {
        if (controller.signal.aborted) return
        setUnavailable(false)
        setRuntime(value)
        setPage(value.destination || '')
        setError(undefined)
      })
      .catch((err) => {
        if (controller.signal.aborted) return
        setRuntime(undefined)
        setAuthorizeUrl('')
        setUnavailable(err instanceof ApiError && err.problem.status === 404)
        setError(err instanceof ApiError && err.problem.status === 404 ? undefined : err)
      })
    return () => controller.abort()
  }, [revision])
  useEffect(() => {
    const refresh = () => setRevision((v) => v + 1)
    window.addEventListener('focus', refresh)
    return () => window.removeEventListener('focus', refresh)
  }, [])
  async function run(action: () => Promise<unknown>) {
    if (busy) return
    setBusy(true)
    setError(undefined)
    try {
      await action()
      setRevision((v) => v + 1)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }
  if (!import.meta.env.DEV) return null
  return (
    <section className="linkedin-panel" aria-label="Facebook Development">
      <h3>Facebook — Développement local</h3>
      {error != null && <ErrorNotice error={error} />}
      {unavailable && (
        <p role="status">
          Connexion Facebook indisponible. Vérifiez que la passerelle locale est activée
          (SOCIALFLOW_VISUAL_GATEWAY_ENABLED=true) et que l’API utilise Facebook:Enabled=true en
          Development, puis redémarrez les services et cliquez sur Actualiser Facebook.
        </p>
      )}
      {!runtime && (
        <button type="button" className="button" disabled>
          Connecter Facebook
        </button>
      )}
      {!runtime && !error && !unavailable && (
        <p role="status">Chargement de la connexion Facebook…</p>
      )}
      {runtime && (
        <>
          <p role="status">
            {runtime.connected
              ? `Connecté à la Page ${runtime.destination}`
              : runtime.state === 'expired'
                ? 'Connexion Facebook expirée. Reconnectez-vous.'
                : 'Sélectionnez une Page après connexion à Facebook.'}
          </p>
          {!runtime.configured && <p>Configuration Meta manquante côté serveur.</p>}
          {runtime.expiresAt && <p>Expiration : {new Date(runtime.expiresAt).toLocaleString()}</p>}
          <button
            type="button"
            className="button"
            disabled={busy || !runtime.configured}
            onClick={() =>
              void run(async () => {
                setAuthorizeUrl((await facebookApi.connect()).authorizeUrl)
              })
            }
          >
            Connecter Facebook
          </button>
          {authorizeUrl && (
            <a
              className="button primary"
              href={authorizeUrl}
              target="_blank"
              rel="noopener noreferrer"
            >
              Ouvrir l’autorisation Facebook
            </a>
          )}
          {runtime.connectionId && (
            <>
              <label>
                Page Facebook de destination
                <select value={page} disabled={busy} onChange={(e) => setPage(e.target.value)}>
                  <option value="">Choisir une Page…</option>
                  {runtime.pages?.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name}
                    </option>
                  ))}
                </select>
              </label>
              <button
                type="button"
                className="button"
                disabled={busy || !page || runtime.state === 'expired'}
                onClick={() => void run(() => facebookApi.select(runtime.connectionId!, page))}
              >
                Utiliser cette Page
              </button>
              <button
                type="button"
                className="button"
                disabled={busy}
                onClick={() =>
                  void run(async () => {
                    await facebookApi.disconnect(runtime.connectionId!)
                    setAuthorizeUrl('')
                  })
                }
              >
                Déconnecter Facebook
              </button>
            </>
          )}
          <p>
            {runtime.workerEnabled
              ? 'Publication automatique activée, contrôle toutes les 30 secondes.'
              : 'Publication automatique désactivée.'}{' '}
            L’API doit rester en fonctionnement.
          </p>
          {!runtime.publicationEnabled && <p>Publication Facebook désactivée côté serveur.</p>}
          <p className="field-note">
            La Page sélectionnée sera utilisée pour les publications Facebook à traiter. La
            destination est figée dès la prise en charge durable. Les profils personnels ne sont pas
            pris en charge.
          </p>
        </>
      )}
      <button
        type="button"
        className="button"
        disabled={busy}
        onClick={() => setRevision((v) => v + 1)}
      >
        Actualiser Facebook
      </button>
    </section>
  )
}
