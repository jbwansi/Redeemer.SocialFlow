import { VisualLightbox } from './VisualLightbox'
import { WorkspaceIdentityPicker } from './WorkspaceIdentityPicker'
import { VisualIdentityLibrary } from './VisualIdentityLibrary'
import { emptyIdentity } from '../lib/visualIdentities'
import { useEffect, useRef, useState } from 'react'
import { ApiError, type Post } from '../api/posts'
import { visualsApi, workspaceVisualsApi, type Visual, type VisualAvailability, type VisualConcept, type VisualTextOptions, type VisualTextReview } from '../api/visuals'
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
  const [expanded, setExpanded] = useState<string>()
  const [brandMode, setBrandMode] = useState('None')
  const [brandLayout, setBrandLayout] = useState('Compact')
  const [logoSlogan, setLogoSlogan] = useState('')
  const [textDisplay, setTextDisplay] = useState<VisualTextOptions['display']>('Illustration')
  const [textTitle, setTextTitle] = useState(post.title)
  const [textSubtitle, setTextSubtitle] = useState('')
  const [textMessages, setTextMessages] = useState('')
  const [textLayout, setTextLayout] = useState<VisualTextOptions['layout']>('Landscape')
  const [textReview, setTextReview] = useState<VisualTextReview>()
  const [textConfirmed, setTextConfirmed] = useState(false)
  const hasText = textDisplay !== 'Illustration'
  const textOptions: VisualTextOptions = { display: textDisplay, layout: textLayout,
    ...(hasText ? { title: textTitle, subtitle: textSubtitle, messages: textDisplay === 'Messages' ? textMessages.split('\n') : [] } : {}) }
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
  useEffect(() => { setLogoSlogan('') }, [identity.libraryId, identity.revision])
  useEffect(() => { setTextReview(undefined); setTextConfirmed(false) },
    [textDisplay, textTitle, textSubtitle, textMessages, textLayout, post.id, post.updatedAt, post.title, post.content, unsaved, identity])
  useEffect(() => {
    setTextDisplay('Illustration'); setTextTitle(post.title); setTextSubtitle(''); setTextMessages('')
    // Reset only when navigating to another post, never overwrite a draft during a save/refetch.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [post.id])
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
    setExpanded(undefined)
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
              <WorkspaceIdentityPicker value={identity} onChange={setIdentity} onBusy={setIdentityBusy} />
            ) : (
              <VisualIdentityLibrary value={identity} onChange={setIdentity} onBusy={setIdentityBusy} />
            )}
          </details>
          {workspace && <section aria-label="Présentation de marque">
            <label>Disposition de marque<select value={brandLayout} onChange={e => setBrandLayout(e.target.value)}>
              <option value="Compact">Compacte — bandeau réduit</option><option value="Classic">Classique — logo à gauche et slogan à droite</option>
              <option value="Overlay" disabled>Sans bandeau — zone sûre non garantie</option>
            </select></label>
            <div className={`brand-layout-preview ${brandLayout === 'Compact' ? 'compact' : 'classic'}`} role="img" aria-label={`Schéma de la disposition ${brandLayout === 'Compact' ? 'compacte' : 'classique'}`}>
              <div>Illustration entière conservée</div><footer>{brandMode === 'None' ? 'Aucune marque ajoutée' : brandMode === 'Slogan' ? 'Slogan enregistré seul' : `Logo officiel${brandMode === 'LogoAndSlogan' && logoSlogan === 'no' ? ' + slogan séparé' : ' · sans slogan ajouté'}`}</footer>
            </div>
            <p>Sans bandeau indisponible : aucune zone libre ne peut être garantie sans masquer le contenu. Cet aperçu schématique ne déclenche aucun appel IA.</p>
            <label>Présentation de marque<select value={brandMode} onChange={e => setBrandMode(e.target.value)}>
              <option value="None">Aucune composition</option><option value="Logo">Logo officiel seul</option>
              <option value="Slogan">Slogan enregistré seul</option><option value="LogoAndSlogan">Logo et slogan</option>
            </select></label>
            {brandMode === 'LogoAndSlogan' && <label>Le slogan figure-t-il déjà dans le logo officiel ?
              <select value={logoSlogan} onChange={e => setLogoSlogan(e.target.value)}>
                <option value="">À vérifier sur le fichier officiel</option><option value="yes">Oui — ne pas le répéter</option><option value="no">Non — ajouter le slogan enregistré</option>
              </select></label>}
            <p>Composition sans appel IA, sur un nouveau candidat. Le bandeau est ajouté sous l’image sans masquer son contenu. Seuls les éléments de l’identité approuvée sont utilisés. Vérifiez le logo avant de déclarer la présence du slogan.</p>
          </section>}
          {workspace && <section className="visual-text-panel" aria-label="Texte du visuel">
            <h4>Texte du visuel</h4>
            <label>Affichage du texte<select value={textDisplay} onChange={e => setTextDisplay(e.target.value as VisualTextOptions['display'])}>
              <option value="Illustration">Illustration seule</option>
              <option value="Title">Illustration avec titre</option>
              <option value="Messages">Illustration avec titre et messages courts</option>
            </select></label>
            {hasText && <>
              <label>Titre du visuel<textarea maxLength={240} value={textTitle} onChange={e => setTextTitle(e.target.value)} /></label>
              <label>Sous-titre facultatif<textarea maxLength={400} value={textSubtitle} onChange={e => setTextSubtitle(e.target.value)} /></label>
              {textDisplay === 'Messages' && <label>Messages courts (3 à 5, un par ligne)<textarea value={textMessages} onChange={e => setTextMessages(e.target.value)} maxLength={1004} /></label>}
              <label>Disposition<select value={textLayout} onChange={e => setTextLayout(e.target.value as VisualTextOptions['layout'])}>
                <option value="Landscape">Paysage — 1200 × 900</option><option value="Square">Carré — 1200 × 1200</option>
              </select></label>
              <p>Le titre est repris de la publication. Corrigez les textes, puis validez leur graphie et leur exactitude. Aucun fait, label ou certification n’est ajouté automatiquement.</p>
              <button type="button" className="button" disabled={unsaved} onClick={() => {
                setTextReview(undefined); setTextConfirmed(false)
                void run('Vérification du texte sans appel IA…', async signal => {
                  const result = await api.reviewText(post.id, textOptions, signal)
                  if (!signal.aborted) setTextReview(result)
                })
              }}>Vérifier le texte (sans coût)</button>
              {textReview && <div>
                {!!textReview.terminology?.warnings.length && <div role="alert"><strong>Revue terminologique requise</strong>
                  <ul>{textReview.terminology.warnings.map((warning, i) => <li key={i}>{warning}</li>)}</ul>
                  <p>Corrigez les graphies recommandées. Vérifiez les ambiguïtés dans le contexte de votre publication.</p>
                </div>}
                <label><input type="checkbox" disabled={textReview.canonicalCorrectionsRequired} checked={textConfirmed} onChange={e => setTextConfirmed(e.target.checked)} />Je valide ces textes exacts et leur usage après revue du référentiel</label>
              </div>}
            </>}
            <p>La composition est gratuite et crée un candidat à valider. L’illustration source reste intacte. La consigne « sans texte » donnée à l’IA ne garantit pas l’absence d’inscriptions : inspectez toujours l’image source.</p>
          </section>}
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
      {expanded && candidates.some(v => v.id === expanded) && <VisualLightbox key={post.id} postId={post.id} visuals={candidates} initialId={expanded} workspace={workspace} close={() => setExpanded(undefined)} />}
      <div className="visual-grid">
        {candidates.map((visual) => (
          <article className="visual-card" key={visual.id}>
            <button type="button" className="visual-enlarge" aria-label="Agrandir le visuel" onClick={() => setExpanded(visual.id)}>
              <VisualPreview postId={post.id} visual={visual} workspace={workspace} />
              <span>Agrandir</span>
            </button>
            <p>
              {visual.selected ? 'Sélectionné et validé' : 'Candidat'} ·{' '}
              {visual.generation?.composition ? 'Visuel composé' : visual.origin === 1 ? 'Illustration source générée' : 'Illustration source importée'}
            </p>
            <small>
              {visual.width} × {visual.height} · {Math.ceil(visual.byteLength / 1024)} Kio ·{' '}
              {new Date(visual.createdAt).toLocaleString()}
            </small>
            {!readOnly && (
              <div className="visual-actions">
                {workspace && <button type="button" className="button" disabled={!editable || locked || unsaved || (brandMode === 'None' && !hasText) || (hasText && (!textReview || !textConfirmed)) || identity.identityMode !== 'Approved' || !identity.libraryId || !!visual.generation?.composition || (brandMode === 'LogoAndSlogan' && !logoSlogan)}
                  onClick={() => void run('Composition sans appel IA…', async signal => {
                    const args = [post.id, visual.id, identity.libraryId!, identity.revision!, brandMode, logoSlogan === '' ? null : logoSlogan === 'yes', signal] as const
                    const result = hasText
                      ? await api.compose(...args, { text: textOptions, textFingerprint: textReview!.fingerprint, textConfirmed }, brandLayout)
                      : await api.compose(...args, undefined, brandLayout)
                    if (!signal.aborted) setExpanded(result.id)
                  })}>{hasText ? 'Composer un aperçu avec texte et marque' : 'Composer un aperçu de marque'}</button>}
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
