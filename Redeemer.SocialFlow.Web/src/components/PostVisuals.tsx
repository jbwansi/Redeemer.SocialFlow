import { WorkspaceIdentityPicker } from './WorkspaceIdentityPicker'
import { VisualIdentityLibrary } from './VisualIdentityLibrary'
import { emptyIdentity } from '../lib/visualIdentities'
import { useEffect, useRef, useState } from 'react'
import { ApiError, type Post } from '../api/posts'
import { visualsApi, workspaceVisualsApi, type Visual, type VisualAvailability, type VisualConcept } from '../api/visuals'
import { ErrorNotice } from './shared'

export function VisualPreview({ postId, visual, workspace = false }: { postId: string; visual: Visual; workspace?: boolean }) {
  const api = workspace ? workspaceVisualsApi : visualsApi
  const [url, setUrl] = useState('')
  const [error, setError] = useState<unknown>()
  useEffect(() => {
    const controller = new AbortController()
    let objectUrl: string | undefined
    setUrl('')
    setError(undefined)
    void api
      .image(postId, visual.id, controller.signal)
      .then((blob) => {
        if (controller.signal.aborted) return
        objectUrl = URL.createObjectURL(blob)
        setUrl(objectUrl)
      })
      .catch((err) => {
        if (!controller.signal.aborted) setError(err)
      })
    return () => {
      controller.abort()
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [api, postId, visual.id])
  if (error) return <ErrorNotice error={error} />
  return url ? (
    <img
      className="visual-preview"
      src={url}
      alt={visual.selected ? 'Visuel sélectionné' : 'Visuel candidat'}
    />
  ) : (
    <p role="status">Chargement de l’image…</p>
  )
}

export function PostVisuals({
  post,
  brief = '',
  readOnly = false,
  disabled = false,
  onBusy,
  workspace = false,
  unsaved = false,
}: {
  post: Post
  brief?: string
  readOnly?: boolean
  disabled?: boolean
  workspace?: boolean
  unsaved?: boolean
  onBusy?: (busy: boolean) => void
}) {
  const api = workspace ? workspaceVisualsApi : visualsApi
  const [available, setAvailable] = useState<boolean>()
  const [capabilities, setCapabilities] = useState<VisualAvailability>()
  const [loaded, setLoaded] = useState(false)
  const [visuals, setVisuals] = useState<Visual[]>([])
  const [error, setError] = useState<unknown>()
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState('')
  const [notice, setNotice] = useState('')
  const [identityBusy, setIdentityBusy] = useState(false)
  const [identity, setIdentity] = useState(emptyIdentity)
  const [concept, setConcept] = useState<VisualConcept>()
  const [confirmed, setConfirmed] = useState(false)
  useEffect(() => {
    setConcept(undefined)
    setConfirmed(false)
  }, [post.id, post.title, post.content, post.updatedAt, brief, identity, unsaved])
  const active = useRef<AbortController | null>(null)
  const mounted = useRef(false)
  const busyCallback = useRef(onBusy)
  busyCallback.current = onBusy
  const editable = !readOnly && (post.status === 1 || post.status === 6)
  const locked = disabled || !!busy || loading || identityBusy || (workspace && (!loaded || available !== true))
  const canGenerate = !workspace || capabilities?.generationEnabled === true
  const canUpload = !workspace || capabilities?.uploadEnabled === true
  useEffect(() => {
    mounted.current = true
    const controller = new AbortController()
    setLoading(true)
    setError(undefined)
    setAvailable(undefined)
    setCapabilities(undefined)
    setLoaded(false)
    setVisuals([])
    void api
      .runtime(controller.signal)
      .then(async (runtime) => {
        if (controller.signal.aborted) return
        setAvailable(runtime.enabled)
        setCapabilities(runtime)
        if (runtime.enabled) {
          const result = await api.list(post.id, controller.signal)
          if (!controller.signal.aborted) { setVisuals(result); setLoaded(true) }
        }
      })
      .catch((err) => {
        if (controller.signal.aborted) return
        if (!workspace && err instanceof ApiError && err.problem.status === 404) setAvailable(false)
        else setError(err)
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => {
      mounted.current = false
      controller.abort()
      active.current?.abort()
      busyCallback.current?.(false)
    }
  }, [api, post.id])

  async function run(label: string, operation: (signal: AbortSignal) => Promise<unknown>) {
    if (active.current || disabled) return
    const controller = new AbortController()
    active.current = controller
    setBusy(label)
    busyCallback.current?.(true)
    setError(undefined)
    setNotice('')
    try {
      await operation(controller.signal)
      const result = await api.list(post.id, controller.signal)
      if (mounted.current) setVisuals(result)
    } catch (err) {
      if (mounted.current) {
        if (typeof err === 'object' && err !== null && 'name' in err && err.name === 'AbortError')
          setNotice(
            'Attente annulée. L’opération peut déjà avoir été traitée ; actualisez les visuels avant de réessayer.',
          )
        else setError(err)
      }
    } finally {
      active.current = null
      if (mounted.current) {
        setBusy('')
        busyCallback.current?.(false)
      }
    }
  }
  function prepare() {
    if (!editable || locked || unsaved || !canGenerate || identity.isActive === false || (workspace && !identity.identityMode)) return
    const colors = identity.colors
    if (
      !identity.identityId.trim() ||
      !identity.name.trim() ||
      !identity.style.trim() ||
      colors.length < 1 ||
      colors.length > 8 ||
      colors.some((c) => !/^#[0-9a-f]{6}$/i.test(c))
    ) {
      setError(
        new ApiError({
          title: 'Informations manquantes',
          detail:
            'Renseignez l’identité, son nom, son style et 1 à 8 couleurs valides au format #RRGGBB.',
        }),
      )
      return
    }
    setConcept(undefined)
    setConfirmed(false)
    void run('Préparation sans appel IA…', async (signal) => {
      const result = await api.prepare(post.id, { ...identity, colors }, brief, signal)
      if (!signal.aborted && mounted.current) setConcept(result)
    })
  }
  function generate() {
    if (!editable || locked || unsaved || !canGenerate || !concept || !confirmed) return
    const approved = concept
    setConcept(undefined)
    setConfirmed(false)
    void run('Génération en cours…', (signal) =>
      api.generate(post.id, identity, brief, signal, approved.conceptToken))
  }
  if (available === false)
    return workspace ? <p className="field-note">Les visuels sont désactivés pour cet environnement.</p> : readOnly ? null : (
      <p className="field-note">Gestion des visuels désactivée sur ce poste.</p>
    )
  if (loading) return <p role="status">Chargement des visuels…</p>
  const candidates = readOnly ? visuals.filter((v) => v.selected) : visuals
  return (
    <section className="visual-manager" aria-label="Visuels de la publication">
      <h3>{readOnly ? 'Visuel sélectionné' : 'Visuels de la publication'}</h3>
      {workspace && error != null && <p role="alert">
        {error instanceof ApiError && error.problem.status === 403
          ? 'Accès aux visuels refusé pour cette opération.'
          : error instanceof ApiError && error.problem.status === 401
            ? 'Session expirée. Reconnectez-vous pour accéder aux visuels.'
            : 'Impossible de charger ou traiter les visuels. Réessayez après actualisation.'}
      </p>}
      {workspace && capabilities?.reason === 'ImageGenerationNotConfigured' &&
        <p className="field-note">La génération d’images n’est pas configurée. L’importation reste disponible si le chargement réussit.</p>}
      {error != null && <ErrorNotice error={error} />}
      {notice && <p role="status">{notice}</p>}
      {!readOnly && !editable && (
        <p className="field-note">
          Les visuels sont modifiables uniquement en brouillon ou après rejet.
        </p>
      )}
      {editable && (
        <fieldset disabled={locked}>
          <details>
            <summary>Identité visuelle pour la génération</summary>
            {workspace ? (
              <WorkspaceIdentityPicker value={identity} onChange={setIdentity} />
            ) : (
              <VisualIdentityLibrary value={identity} onChange={setIdentity} onBusy={setIdentityBusy} />
            )}
          </details>
          {unsaved && <p role="status">Enregistrez les modifications de la publication avant de préparer un concept.</p>}
          <button type="button" className="button" disabled={unsaved || !canGenerate || identity.isActive === false || (workspace && !identity.identityMode)} onClick={prepare}>
            Préparer le concept (sans coût)
          </button>
          {concept && <section aria-label="Concept visuel">
            <h4>{concept.subject}</h4>
            {!!concept.warnings?.length && <div role="alert"><strong>Revue terminologique requise</strong>
              <ul>{concept.warnings.map((w, i) => <li key={i}>{w}</li>)}</ul>
              <p>Vérifiez les ambiguïtés et les zones conservées avant de valider ce concept.</p>
            </div>}
            {concept.identityName && <p>{concept.identityName} · {concept.identityStatus === 'Approved' ? 'Approuvée' : 'Temporaire, non approuvée'}</p>}
            <p><strong>Message du concept, issu du contenu enregistré :</strong> {concept.message}</p>
            <p><strong>Scène proposée :</strong> {concept.scene}</p>
            <p><strong>Brief complémentaire :</strong> {concept.brief}</p>
            <p><strong>Style :</strong> {concept.style}</p>
            <ul>{concept.exclusions.map(x => <li key={x}>{x}</li>)}</ul>
            <p>Le sujet enregistré reste prioritaire. Pour revoir le brief, modifiez et enregistrez la publication, puis préparez à nouveau le concept.</p>
            <label><input type="checkbox" checked={confirmed} onChange={e => setConfirmed(e.target.checked)} />
              Je valide ce concept avant la génération payante
            </label>
          </section>}
          <div className="visual-actions">
            <button
              type="button"
              className="button primary"
              disabled={identity.isActive === false || !canGenerate || unsaved || !concept || !confirmed}
              onClick={generate}
            >
              {visuals.length ? 'Régénérer' : 'Générer un visuel'}
            </button>
            <label className="button">
              Importer une image
              <input
                className="visual-file-input"
                aria-label="Importer une image"
                type="file"
                disabled={!canUpload}
                accept="image/png,image/jpeg"
                onChange={(e) => {
                  const file = e.target.files?.[0]
                  e.target.value = ''
                  if (!file || !editable || locked || !canUpload) return
                  if (
                    !['image/png', 'image/jpeg'].includes(file.type) ||
                    file.size > 20 * 1024 * 1024 ||
                    file.size === 0
                  ) {
                    setError(
                      new ApiError({
                        title: 'Image non acceptée',
                        detail: 'Choisissez un PNG ou JPEG non vide de 20 Mio maximum.',
                      }),
                    )
                    return
                  }
                  void run('Importation en cours…', (signal) =>
                    api.upload(post.id, file, signal),
                  )
                }}
              />
            </label>
          </div>
          <p className="field-note">
            La génération est payante et démarre uniquement sur clic. Chaque résultat reste un
            candidat à sélectionner.
          </p>
        </fieldset>
      )}
      {busy && (
        <div className="visual-actions">
          <span role="status">{busy}</span>
          <button type="button" className="button" onClick={() => active.current?.abort()}>
            Annuler l’attente
          </button>
        </div>
      )}
      <button
        type="button"
        className="text-button"
        disabled={locked}
        onClick={() => void run('Actualisation…', async () => {})}
      >
        Actualiser les visuels
      </button>
      {candidates.length === 0 && (
        <p className="field-note">
          {readOnly ? 'Aucun visuel sélectionné.' : 'Aucun visuel pour cette publication.'}
        </p>
      )}
      <div className="visual-grid">
        {candidates.map((visual) => (
          <article className="visual-card" key={visual.id}>
            <VisualPreview postId={post.id} visual={visual} workspace={workspace} />
            <p>
              {visual.selected ? 'Sélectionné et validé' : 'Candidat'} ·{' '}
              {visual.origin === 1 ? 'Généré' : 'Importé'}
            </p>
            <small>
              {visual.width} × {visual.height} · {Math.ceil(visual.byteLength / 1024)} Kio ·{' '}
              {new Date(visual.createdAt).toLocaleString()}
            </small>
            {!readOnly && (
              <div className="visual-actions">
                <button
                  type="button"
                  className="button"
                  disabled={!editable || locked || visual.selected}
                  onClick={() =>
                    void run('Sélection…', (signal) =>
                      api.select(post.id, visual.id, signal),
                    )
                  }
                >
                  Sélectionner ce visuel
                </button>
                <button
                  type="button"
                  className="button danger"
                  disabled={!editable || locked || visual.selected}
                  onClick={() =>
                    void run('Suppression…', (signal) =>
                      api.remove(post.id, visual.id, signal),
                    )
                  }
                >
                  Supprimer
                </button>
              </div>
            )}
          </article>
        ))}
      </div>
    </section>
  )
}
