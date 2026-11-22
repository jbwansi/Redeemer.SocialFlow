import { useEffect, useRef, useState, type FormEvent } from 'react'
import { apiRequest } from '../api/posts'
import {
  postsApi,
  platforms,
  type Platform,
  type GeneratedDraftSuccess,
  type KnowledgePassage,
} from '../api/posts'
import type { EditorialAngle } from './WorkspaceEditorialAngles'
import { ErrorNotice, Modal } from './shared'

export function GenerateDraft({
  close,
  saved,
  workspace = false,
}: {
  close: () => void
  saved: (result: GeneratedDraftSuccess) => void
  workspace?: boolean
}) {
  const [angles, setAngles] = useState<EditorialAngle[]>([])
  const [angleId, setAngleId] = useState('')
  const [angleError, setAngleError] = useState(false)
  useEffect(() => {
    if (!workspace) return
    const controller = new AbortController()
    setAngles([]); setAngleId(''); setAngleError(false)
    void apiRequest<EditorialAngle[]>('/workspace-editorial-angles?activeOnly=true', { signal: controller.signal }).then(x => {
      if (!controller.signal.aborted) setAngles(x)
    }).catch(() => { if (!controller.signal.aborted) setAngleError(true) })
    return () => controller.abort()
  }, [workspace])
  const [subject, setSubject] = useState('')
  const [objective, setObjective] = useState('')
  const [audience, setAudience] = useState('')
  const [context, setContext] = useState('')
  const [platform, setPlatform] = useState<Platform>(2)
  const [mode, setMode] = useState<'' | 1 | 2>('')
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState(false)
  const pending = useRef(false)
  const [error, setError] = useState<unknown>()
  const [identityNotice, setIdentityNotice] = useState('Chargement de l’identité éditoriale…')
  useEffect(() => {
    if (!workspace) return
    const controller = new AbortController()
    void apiRequest<{ workspaceId: string; profile: { organizationName: string; revision: number } | null }>('/workspace-identities/editorial', { signal: controller.signal }).then(x => {
      if (!controller.signal.aborted) setIdentityNotice(x.profile
        ? `${x.profile.organizationName} · profil éditorial révision ${x.profile.revision} · espace ${x.workspaceId}`
        : 'Profil éditorial absent : génération neutre, sans personnalisation du workspace.')
    }).catch(() => { if (!controller.signal.aborted) setIdentityNotice('Impossible de charger l’aperçu du profil. Le serveur vérifiera le profil de cet espace lors de la génération.') })
    return () => controller.abort()
  }, [workspace])
  async function submit(event: FormEvent) {
    event.preventDefault()
    if (pending.current || !mode || !subject.trim() || !objective.trim() || !audience.trim()) return
    pending.current = true
    setBusy(true)
    setError(undefined)
    setMessage('')
    try {
      const result = await postsApi.generateDraft({ subject, objective, audience, platform, mode, ...(workspace && angleId ? { editorialAngleId: angleId } : {}), ...(context.trim() ? { context: context.trim() } : {}) })
      if (result.outcome === 1) saved(result)
      else
        setMessage(
          result.outcome === 2
            ? 'Aucun passage pertinent trouvé dans vos sources. Aucun brouillon créé. Précisez le sujet ou vérifiez vos documents.'
            : 'Les passages trouvés dépassent la limite de contexte. Aucun brouillon créé. Précisez le sujet ou réduisez la taille des passages dans vos documents.',
        )
    } catch (err) {
      setError(err)
    } finally {
      pending.current = false
      setBusy(false)
    }
  }
  return (
    <Modal title="Générer un brouillon" close={close} busy={busy}>
      <form className="editor-form" onSubmit={submit} aria-busy={busy}>
        {workspace && <p role="status">{identityNotice}</p>}
        <p className="field-note">
          Le contenu sera enregistré comme brouillon (Draft). Une relecture humaine est nécessaire
          avant toute soumission pour validation.
        </p>
        {error != null && <ErrorNotice error={error} />}
        {message && (
          <p className="ai-review-notice" role="status">
            {message}
          </p>
        )}
        <fieldset disabled={busy}>
          {workspace && <>
            <label>Angle éditorial (facultatif)<select value={angleId} onChange={e => setAngleId(e.target.value)}>
              <option value="">Sans angle</option>
              {angles.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}
            </select></label>
            {angleId && <p>{angles.find(x => x.id === angleId)?.description}</p>}
            {angleError && <p role="status">Angles indisponibles. Vous pouvez continuer sans angle.</p>}
          </>}
          <label>
            Mode de génération
            <select
              required
              value={mode}
              onChange={(e) => {
                setMode(e.target.value ? (Number(e.target.value) as 1 | 2) : '')
                setMessage('')
              }}
            >
              <option value="">Choisir un mode</option>
              <option value="1">Éditorial</option>
              <option value="2">Avec mes sources</option>
            </select>
          </label>
          <label>
            Sujet
            <input
              autoFocus
              required
              value={subject}
              onChange={(e) => setSubject(e.target.value)}
            />
          </label>
          <label>
            Objectif
            <textarea
              required
              rows={3}
              value={objective}
              onChange={(e) => setObjective(e.target.value)}
            />
          </label>
          <label>
            Public cible
            <input required value={audience} onChange={(e) => setAudience(e.target.value)} />
          </label>
          <label>
            Contexte complémentaire (facultatif)
            <textarea rows={4} maxLength={4000} value={context} onChange={(e) => setContext(e.target.value)} />
          </label>
          <label>
            Plateforme
            <select
              value={platform}
              onChange={(e) => setPlatform(Number(e.target.value) as Platform)}
            >
              {Object.entries(platforms).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>
        </fieldset>
        {busy && (
          <p className="field-note" role="status">
            Génération du brouillon en cours…
          </p>
        )}
        <footer className="modal-footer">
          <button type="button" className="button" onClick={close} disabled={busy}>
            Annuler
          </button>
          <button
            className="button primary"
            disabled={busy || !mode || !subject.trim() || !objective.trim() || !audience.trim()}
          >
            {busy ? 'Génération…' : 'Générer le brouillon'}
          </button>
        </footer>
      </form>
    </Modal>
  )
}

export function AiReviewNotice({
  warnings,
  references = [],
}: {
  warnings: string[]
  references?: KnowledgePassage[]
}) {
  return (
    <div className="ai-review-notice" role="note">
      <strong>Brouillon généré par l’IA — relecture humaine nécessaire</strong>
      <p>Vérifiez le contenu et les faits avant de le soumettre pour validation.</p>
      {warnings.length > 0 && (
        <>
          <strong>Avertissements de l’IA</strong>
          <ul>
            {warnings.map((warning, index) => (
              <li key={index}>{warning}</li>
            ))}
          </ul>
        </>
      )}
      {references.length > 0 && (
        <>
          <strong>Références de cette génération</strong>
          <p>
            Ces références ont été transmises pour cette génération. Elles ne sont pas enregistrées
            avec le post et disparaissent au rechargement de la page. Elles ne garantissent pas que
            toutes les affirmations sont vérifiées.
          </p>
          <ul>
            {references.map((reference, index) => (
              <li key={`${reference.knowledgeChunkId}-${index}`}>
                <details>
                  <summary>
                    {reference.documentTitle} · version {reference.documentVersion}
                    {reference.pageNumber != null && ` · page ${reference.pageNumber}`}
                    {reference.section && ` · ${reference.section}`}
                  </summary>
                  <p>{reference.content}</p>
                </details>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  )
}
