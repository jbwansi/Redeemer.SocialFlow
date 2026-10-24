import { useEffect, useRef, useState } from 'react'
import { ApiError, postsApi, type Post } from '../api/posts'
import {
  linkedInApi,
  type PublicationOperation,
  type PublicationPreview,
  type PublicationView,
} from '../api/linkedin'
import { facebookApi } from '../api/facebook'
import { ErrorNotice } from './shared'

export function PlatformPublication({
  post,
  busy,
  setBusy,
  changed,
}: {
  post: Post
  busy: boolean
  setBusy: (value: boolean) => void
  changed: (post: Post) => void
}) {
  const api = post.platform === 1 ? facebookApi : linkedInApi
  const platformName = post.platform === 1 ? 'Facebook' : 'LinkedIn'
  const [view, setView] = useState<PublicationView>()
  const [preview, setPreview] = useState<PublicationPreview>()
  const [error, setError] = useState<unknown>()
  const [hidden, setHidden] = useState(false)
  const [loading, setLoading] = useState(true)
  const [revision, setRevision] = useState(0)
  const [uncertain, setUncertain] = useState(false)
  const sending = useRef(false)
  const changedRef = useRef(changed)
  changedRef.current = changed
  useEffect(() => {
    const state = view?.operation?.state
    if (
      !import.meta.env.DEV ||
      !((state === 2 && post.status !== 5) || (state === 3 && post.status !== 7))
    )
      return
    const controller = new AbortController()
    postsApi
      .get(post.id, controller.signal)
      .then((result) => {
        if (!controller.signal.aborted) changedRef.current(result)
      })
      .catch((err) => {
        if (!controller.signal.aborted) setError(err)
      })
    return () => controller.abort()
  }, [view?.operation?.state, post.id, post.status, revision])
  useEffect(() => {
    if (!import.meta.env.DEV || ![1, 2].includes(post.platform)) return
    const controller = new AbortController()
    setLoading(true)
    api
      .publication(post.id, controller.signal)
      .then((result) => {
        if (!controller.signal.aborted) {
          setView(result)
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
  }, [post.id, post.status, post.platform, revision, api])
  // Refresh eligibility as a scheduled instant arrives and observe automatic publication.
  useEffect(() => {
    if (!import.meta.env.DEV || ![1, 2].includes(post.platform)) return
    const refresh = () => {
      if (!sending.current) setRevision((value) => value + 1)
    }
    const timer = window.setInterval(refresh, 30000)
    window.addEventListener('focus', refresh)
    return () => {
      clearInterval(timer)
      window.removeEventListener('focus', refresh)
    }
  }, [post.platform])
  async function prepare() {
    if (sending.current || busy) return
    sending.current = true
    setBusy(true)
    setError(undefined)
    try {
      setPreview(await api.preview(post.id))
    } catch (err) {
      setError(err)
    } finally {
      sending.current = false
      setBusy(false)
    }
  }
  async function publish() {
    if (sending.current || busy || !preview) return
    sending.current = true
    setBusy(true)
    setError(undefined)
    setUncertain(false)
    try {
      const operation = await api.publish(post.id, preview.confirmation)
      setView({ operation, canPublish: false })
      setPreview(undefined)
    } catch (err) {
      setError(err)
      setUncertain(true)
      setPreview(undefined)
      try {
        setView(await api.publication(post.id))
      } catch {
        /* Keep uncertainty visible; no automatic POST retry. */
      }
    } finally {
      sending.current = false
      setBusy(false)
    }
  }
  if (!import.meta.env.DEV || ![1, 2].includes(post.platform) || hidden) return null
  return (
    <section className="linkedin-panel" aria-label={'Publication ' + platformName}>
      <h3>Publication {platformName}</h3>
      {loading && <p role="status">Chargement de l’opération…</p>}
      {error != null && <ErrorNotice error={error} />}
      {uncertain && (
        <p role="alert">
          Résultat de la demande non confirmé. Consultez l’opération durable avant toute autre
          action. Aucun renvoi automatique.
        </p>
      )}
      {view?.operation && <OperationInfo operation={view.operation} platformName={platformName} />}
      {!view?.operation && (
        <p>
          Seul un post {platformName} Scheduled arrivé à échéance peut être publié. Aucune
          planification automatique d’un post Approved.
        </p>
      )}
      {view?.canPublish && !preview && (
        <button className="button primary" disabled={busy || loading} onClick={prepare}>
          Publier sur {platformName}
        </button>
      )}
      {preview && (
        <div
          className="delete-confirm"
          role="group"
          aria-label={'Confirmer la publication ' + platformName}
        >
          <strong>
            Publier ce texte sur {post.platform === 1 ? 'la Page' : 'le profil'}{' '}
            {preview.destination} ?
          </strong>
          <div className="post-content">{preview.content}</div>
          <p>
            Publication publique. Cette action envoie le texte et le visuel sélectionné, le cas
            échéant, à {platformName}.
          </p>
          <button className="button primary" disabled={busy} onClick={publish}>
            {busy ? 'Publication en cours…' : 'Confirmer la publication'}
          </button>
          <button className="button" disabled={busy} onClick={() => setPreview(undefined)}>
            Annuler
          </button>
        </div>
      )}
      <button
        className="button"
        disabled={busy || loading}
        onClick={() => setRevision((value) => value + 1)}
      >
        Actualiser la publication
      </button>
    </section>
  )
}

function OperationInfo({
  operation,
  platformName,
}: {
  operation: PublicationOperation
  platformName: string
}) {
  return (
    <div role="status">
      <strong>
        {operation.state === 2
          ? 'Publication confirmée'
          : operation.state === 3
            ? 'Échec certain — aucun nouvel essai automatique.'
            : `Publication indéterminée — vérification manuelle sur ${platformName} nécessaire, aucun renvoi.`}
      </strong>
      {!operation.completedAt && (
        <p>Opération prise en charge, en cours ou interrompue. Ne pas renvoyer.</p>
      )}
      <p>Destination : {operation.destination}</p>
      <p>Opération : {operation.operationId}</p>
      <p>Dernière mise à jour : {new Date(operation.updatedAt).toLocaleString()}</p>
      {operation.externalId && (
        <p>
          Identifiant {platformName} : {operation.externalId}
        </p>
      )}
      <details>
        <summary>Contenu enregistré pour cette opération</summary>
        <div className="post-content">{operation.content}</div>
      </details>
    </div>
  )
}
