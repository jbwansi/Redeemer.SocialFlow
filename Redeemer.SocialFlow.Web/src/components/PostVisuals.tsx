import { VisualIdentityLibrary } from './VisualIdentityLibrary'
import { emptyIdentity } from '../lib/visualIdentities'
import { useEffect, useRef, useState } from 'react'
import { ApiError, type Post } from '../api/posts'
import { visualsApi, type Visual } from '../api/visuals'
import { ErrorNotice } from './shared'

export function VisualPreview({ postId, visual }: { postId: string; visual: Visual }) {
  const [url, setUrl] = useState('')
  const [error, setError] = useState<unknown>()
  useEffect(() => {
    const controller = new AbortController()
    let objectUrl: string | undefined
    setUrl('')
    setError(undefined)
    void visualsApi
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
  }, [postId, visual.id])
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
}: {
  post: Post
  brief?: string
  readOnly?: boolean
  disabled?: boolean
  onBusy?: (busy: boolean) => void
}) {
  const [available, setAvailable] = useState<boolean>()
  const [visuals, setVisuals] = useState<Visual[]>([])
  const [error, setError] = useState<unknown>()
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState('')
  const [notice, setNotice] = useState('')
  const [identityBusy, setIdentityBusy] = useState(false)
  const [identity, setIdentity] = useState(emptyIdentity)
  const active = useRef<AbortController | null>(null)
  const mounted = useRef(false)
  const busyCallback = useRef(onBusy)
  busyCallback.current = onBusy
  const editable = !readOnly && (post.status === 1 || post.status === 6)
  const locked = disabled || !!busy || loading || identityBusy
  useEffect(() => {
    mounted.current = true
    const controller = new AbortController()
    setLoading(true)
    setError(undefined)
    setAvailable(undefined)
    setVisuals([])
    void visualsApi
      .runtime(controller.signal)
      .then(async (runtime) => {
        if (controller.signal.aborted) return
        setAvailable(runtime.enabled)
        if (runtime.enabled) {
          const result = await visualsApi.list(post.id, controller.signal)
          if (!controller.signal.aborted) setVisuals(result)
        }
      })
      .catch((err) => {
        if (controller.signal.aborted) return
        if (err instanceof ApiError && err.problem.status === 404) setAvailable(false)
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
  }, [post.id])

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
      const result = await visualsApi.list(post.id, controller.signal)
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
  function generate() {
    if (!editable || locked || identity.isActive === false) return
    const colors = identity.colors
    if (
      !brief.trim() ||
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
            'Renseignez le brief, l’identité, son nom, son style et 1 à 8 couleurs valides au format #RRGGBB.',
        }),
      )
      return
    }
    void run('Génération en cours…', (signal) =>
      visualsApi.generate(post.id, { ...identity, colors }, brief, signal),
    )
  }
  if (available === false)
    return readOnly ? null : (
      <p className="field-note">Gestion des visuels désactivée sur ce poste.</p>
    )
  if (loading) return <p role="status">Chargement des visuels…</p>
  const candidates = readOnly ? visuals.filter((v) => v.selected) : visuals
  return (
    <section className="visual-manager" aria-label="Visuels de la publication">
      <h3>{readOnly ? 'Visuel sélectionné' : 'Visuels de la publication'}</h3>
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
            <VisualIdentityLibrary
              value={identity}
              onChange={setIdentity}
              onBusy={setIdentityBusy}
            />
          </details>
          <div className="visual-actions">
            <button
              type="button"
              className="button primary"
              disabled={identity.isActive === false}
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
                accept="image/png,image/jpeg"
                onChange={(e) => {
                  const file = e.target.files?.[0]
                  e.target.value = ''
                  if (!file || !editable || locked) return
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
                    visualsApi.upload(post.id, file, signal),
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
            <VisualPreview postId={post.id} visual={visual} />
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
                      visualsApi.select(post.id, visual.id, signal),
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
                      visualsApi.remove(post.id, visual.id, signal),
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
