import { useEffect, useState } from 'react'
import { ApiError } from '../api/posts'
import { linkedInApi, type LinkedInRuntime } from '../api/linkedin'
import { ErrorNotice } from './shared'

export function LinkedInConnection() {
  const [runtime, setRuntime] = useState<LinkedInRuntime>()
  const [error, setError] = useState<unknown>()
  const [hidden, setHidden] = useState(false)
  const [loading, setLoading] = useState(true)
  const [revision, setRevision] = useState(0)
  useEffect(() => {
    if (!import.meta.env.DEV) return
    const controller = new AbortController()
    setLoading(true)
    linkedInApi
      .runtime(controller.signal)
      .then((result) => {
        if (!controller.signal.aborted) {
          setRuntime(result)
          setError(undefined)
        }
      })
      .catch((err) => {
        if (!controller.signal.aborted) {
          if (err instanceof ApiError && err.problem.status === 404) setHidden(true)
          else setError(err)
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => controller.abort()
  }, [revision])
  useEffect(() => {
    if (!import.meta.env.DEV) return
    const refresh = () => setRevision((value) => value + 1)
    const visible = () => {
      if (document.visibilityState === 'visible') refresh()
    }
    window.addEventListener('focus', refresh)
    document.addEventListener('visibilitychange', visible)
    return () => {
      window.removeEventListener('focus', refresh)
      document.removeEventListener('visibilitychange', visible)
    }
  }, [])
  if (!import.meta.env.DEV || hidden) return null
  return (
    <section className="linkedin-panel" aria-label="LinkedIn Development">
      <h3>LinkedIn · Development local</h3>
      {loading && <p role="status">Chargement de la connexion LinkedIn…</p>}
      {error != null && <ErrorNotice error={error} />}
      {runtime && (
        <>
          <p>
            {runtime.connected
              ? `Connecté au profil ${runtime.destination}`
              : runtime.expiresAt
                ? 'Connexion LinkedIn expirée.'
                : 'LinkedIn non connecté.'}
          </p>
          {runtime.expiresAt && <p>Expiration : {new Date(runtime.expiresAt).toLocaleString()}</p>}
          <p>
            {runtime.workerEnabled
              ? 'Publication automatique activée (contrôle toutes les 30 secondes).'
              : 'Publication automatique désactivée.'}{' '}
            L’API doit rester en fonctionnement pour traiter les échéances.
          </p>
          {!runtime.publicationEnabled && <p>Publication LinkedIn désactivée côté serveur.</p>}
          <a className="button" href={runtime.connectUrl} target="_blank" rel="noopener noreferrer">
            Connecter LinkedIn
          </a>
          <small>
            Le parcours s’ouvre dans un nouvel onglet. Après connexion, revenez ici pour actualiser
            le statut.
          </small>
        </>
      )}
      <button
        className="button"
        disabled={loading}
        onClick={() => setRevision((value) => value + 1)}
      >
        Actualiser LinkedIn
      </button>
    </section>
  )
}
