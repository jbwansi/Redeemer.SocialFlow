import { useEffect, useMemo, useRef, useState } from 'react'
import { ApiError, postsApi, type Post } from '../api/posts'
import {
  linkedInApi,
  workspaceLinkedInApi,
  type PublicationOperation,
  type PublicationPreview,
  type PublicationView,
} from '../api/linkedin'
import { facebookApi, workspaceFacebookApi } from '../api/facebook'
import { ErrorNotice } from './shared'
import { socialConnectionsApi, type Connection } from '../api/socialConnections'

export function PlatformPublication({
  post,
  busy,
  setBusy,
  changed,
  workspaceId,
}: {
  post: Post
  busy: boolean
  setBusy: (value: boolean) => void
  changed: (post: Post) => void
  workspaceId?: string
}) {
  const [connectionId, setConnectionId] = useState('')
  const [connections, setConnections] = useState<Connection[]>([])
  const [blockedConnections, setBlockedConnections] = useState<Connection[]>([])
  const [connectionsError, setConnectionsError] = useState<unknown>()
  const [connectionsLoading, setConnectionsLoading] = useState(!!workspaceId)
  const api = useMemo(() => workspaceId ? post.platform === 1 ? workspaceFacebookApi(connectionId) : workspaceLinkedInApi(connectionId) : post.platform === 1 ? facebookApi : linkedInApi, [workspaceId, connectionId, post.platform])
  const platformName = post.platform === 1 ? 'Facebook' : 'LinkedIn'
  const destinationName = post.platform === 1 ? 'Page Facebook' : 'Profil LinkedIn'
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
    if (!workspaceId) return
    const controller = new AbortController()
    setConnectionsLoading(true)
    socialConnectionsApi.list(workspaceId, controller.signal).then(items => {
      if (!controller.signal.aborted) {
        const destinations = items.filter(c => c.platform === post.platform && c.accountType === (post.platform === 1 ? 'Page' : 'Profile'))
        const eligible = destinations.filter(c => c.connectionStatus === 1 && c.publishingPermissionGranted &&
          (c.publishingCapability == null || c.publishingCapability.status === 1) &&
          (c.expiresAtUtc === null ? post.platform === 1 : Date.parse(c.expiresAtUtc) > Date.now()))
        setConnections(eligible)
        setBlockedConnections(destinations.filter(c => !eligible.includes(c)))
        setConnectionsError(undefined)
      }
    }).catch(err => { if (!controller.signal.aborted) { setConnections([]); setBlockedConnections([]); setConnectionsError(err) } })
      .finally(() => { if (!controller.signal.aborted) setConnectionsLoading(false) })
    return () => controller.abort()
  }, [workspaceId, revision, post.platform])
  useEffect(() => {
    const state = view?.operation?.state
    if (
      (!workspaceId && !import.meta.env.DEV) ||
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
    if ((!workspaceId && !import.meta.env.DEV) || ![1, 2].includes(post.platform)) return
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
          if (!workspaceId && err instanceof ApiError && err.problem.status === 404) setHidden(true)
          else setError(err)
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => controller.abort()
  }, [post.id, post.status, post.platform, revision, api, workspaceId])
  // Refresh eligibility as a scheduled instant arrives and observe automatic publication.
  useEffect(() => {
    if ((!workspaceId && !import.meta.env.DEV) || ![1, 2].includes(post.platform)) return
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
  async function automatic(cancel: boolean) {
    if (sending.current || busy) return
    sending.current = true; setBusy(true); setError(undefined)
    try {
      const facebook = workspaceFacebookApi(connectionId)
      if (cancel) await facebook.cancelAutomatic(post.id)
      else await facebook.authorize(post.id)
      setPreview(undefined); setRevision(value => value + 1)
    } catch (err) { setError(err) }
    finally { sending.current = false; setBusy(false) }
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
  if ((!workspaceId && !import.meta.env.DEV) || ![1, 2].includes(post.platform) || hidden) return null
  const unavailableReason = !workspaceId ? undefined :
    loading ? 'Vérification de la publication en cours…' :
    error != null ? 'L’état de publication est indisponible. Consultez l’erreur puis actualisez.' :
    view?.operation ? 'Une opération existe déjà pour ce post. Aucun nouvel envoi autorisé.' :
    post.status !== 3 && post.status !== 4 ? 'Le post doit être approuvé avant un envoi manuel.' :
    post.status === 4 && !post.scheduledAt ? 'La date de planification est manquante.' :
    !view?.canPublish ? (view?.manualBlockReason ? undefined : post.status === 4 && post.scheduledAt
      ? `Envoi non autorisé par le serveur. Échéance prévue : ${new Date(post.scheduledAt).toLocaleString()}. Attendez cette échéance puis actualisez.`
      : 'Envoi manuel non autorisé par le serveur. Vérifiez la configuration et les autorisations de la destination.') :
    connectionsLoading ? `Chargement des destinations ${platformName}…` :
    connectionsError != null ? 'Impossible de charger les profils du workspace. Consultez l’erreur puis actualisez.' :
    connections.length === 0 ? (post.platform === 1 ? 'Aucune Page Facebook autorisée à publier. Une connexion OAuth seule ne suffit pas.' : 'Aucun profil LinkedIn connecté, non expiré et autorisé à publier dans ce workspace.') :
    !connections.some(c => c.id === connectionId) ? `Sélectionnez explicitement ${post.platform === 1 ? 'la Page Facebook' : 'le profil LinkedIn'} destinataire.` : undefined
  const blockMessages: Record<string, string> = {
    publication_disabled: 'Publication Facebook désactivée côté serveur.',
    approval_required: 'Le post doit être approuvé avant un envoi manuel.',
    schedule_missing: 'La date de planification est manquante.',
    scheduled_not_due: `Publication programmée : l’envoi manuel sera disponible à l’échéance${post.scheduledAt ? ' du ' + new Date(post.scheduledAt).toLocaleString() : ''}.`,
    operation_exists: 'Une opération existe déjà pour ce post. Aucun nouvel envoi autorisé.',
    publishing_permission_unverified: 'Publication bloquée : permissions de publication non vérifiées ou connexion expirée. Reconnectez la Page et vérifiez ses autorisations.',
    automatic_not_authorized: 'Publication planifiée non autorisée : sélectionnez une Page puis autorisez explicitement son envoi à échéance.',
    worker_disabled: 'Traitement automatique désactivé côté serveur. L’envoi manuel reste possible à échéance.',
    authorization_or_validation_failed: 'Envoi automatique bloqué avant publication : autorisation, jeton ou contenu à vérifier. Après correction, autorisez de nouveau le plan.',
    automatic_cancelled: 'Autorisation de publication automatique retirée.',
  }
  return (
    <section className="linkedin-panel" aria-label={'Publication ' + platformName}>
      <h3>Publication {platformName}</h3>
      {view?.manualBlockReason && !view.operation && <p role="status">{blockMessages[view.manualBlockReason] ?? 'Envoi manuel bloqué par le serveur.'}</p>}
      {view?.blockReason && view.blockReason !== view.manualBlockReason && <p role="status">{blockMessages[view.blockReason] ?? 'Publication bloquée : vérifiez la connexion et le plan.'}</p>}
      {unavailableReason && <p role="status">{unavailableReason}</p>}
      {connectionsError != null && <ErrorNotice error={connectionsError} />}
      {workspaceId && !view?.operation && <label>{destinationName} destinataire
        <select value={connectionId} disabled={busy || !!preview} onChange={e => { setConnectionId(e.target.value); setPreview(undefined) }}>
          <option value="">{post.platform === 1 ? 'Choisir explicitement une Page' : 'Choisir explicitement un profil'}</option>
          {connections.map(c => <option key={c.id} value={c.id}>{c.displayName} ({c.externalAccountId})</option>)}
        </select>
        {!connectionsLoading && !connectionsError && connections.length === 0 && <p>{post.platform === 1 ? 'Aucune Page Facebook autorisée à publier dans cet espace. Vérifiez les connexions sociales.' : 'Aucun profil LinkedIn autorisé et non expiré dans cet espace. Vérifiez les connexions sociales.'}</p>}
      </label>}
      {workspaceId && !view?.operation && blockedConnections.map(c => <p key={c.id} role="status">
        {c.displayName} : {c.publishingCapability?.reason === 'PageTaskEvidenceUnavailable'
          ? 'Page connectée, mais Meta ne fournit pas de preuve des tâches de publication (Unverified — PageTaskEvidenceUnavailable). Envoi bloqué.'
          : c.publishingCapability?.status === 2 ? 'Autorisation de publication refusée (Denied).'
          : c.publishingCapability?.status === 0 ? 'Capacités de publication non vérifiées (Unverified).'
          : 'Connexion non autorisée à publier, expirée ou déconnectée.'}
      </p>)}
      {workspaceId && post.platform === 1 && post.status === 4 && !view?.operation && <div>
        {view?.automaticAuthorized ? <>
          <p>Envoi automatique autorisé pour le contenu, la destination et l’échéance enregistrés.</p>
          <button className="button" disabled={busy} onClick={() => automatic(true)}>Retirer l’autorisation automatique</button>
        </> : <button className="button" disabled={busy || loading || connectionsLoading || !!connectionsError || !connections.some(c => c.id === connectionId) || view?.blockReason === 'publication_disabled'} onClick={() => automatic(false)}>
          Autoriser la publication Facebook à échéance
        </button>}
      </div>}
      {loading && <p role="status">Chargement de l’opération…</p>}
      {workspaceId && !loading && !view && error != null && (
        <p role="status">Impossible de préparer l’envoi {platformName}. Aucune confirmation disponible : vérifiez l’erreur ci-dessous, puis actualisez la publication.</p>
      )}
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
          {workspaceId
            ? `Un post ${platformName} Approved peut être envoyé immédiatement après sélection de la destination et confirmation. Un post Scheduled reste soumis à son échéance.`
            : `Seul un post ${platformName} Scheduled arrivé à échéance peut être publié.`}
        </p>
      )}
      {(workspaceId || view?.canPublish) && !preview && (
        <>
        {workspaceId && !connectionId && <p>{post.platform === 1 ? 'Choisissez une Page ci-dessus. Un envoi automatique exige une autorisation explicite.' : 'Choisissez un profil ci-dessus pour préparer l’envoi. Le statut Scheduled ne déclenche pas de publication automatique.'}</p>}
        <button className="button primary" disabled={busy || loading || (!!workspaceId && (!view?.canPublish || !!unavailableReason))} onClick={prepare}>
          Publier sur {platformName}
        </button>
        </>
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
