import { useRef, useState, type FormEvent } from 'react'
import {
  postsApi,
  platforms,
  type Platform,
  type GenerateSocialPostDraftResult,
} from '../api/posts'
import { ErrorNotice, Modal } from './shared'

export function GenerateDraft({
  close,
  saved,
}: {
  close: () => void
  saved: (result: GenerateSocialPostDraftResult) => void
}) {
  const [subject, setSubject] = useState('')
  const [objective, setObjective] = useState('')
  const [audience, setAudience] = useState('')
  const [platform, setPlatform] = useState<Platform>(2)
  const [busy, setBusy] = useState(false)
  const pending = useRef(false)
  const [error, setError] = useState<unknown>()
  async function submit(event: FormEvent) {
    event.preventDefault()
    if (pending.current || !subject.trim() || !objective.trim() || !audience.trim()) return
    pending.current = true
    setBusy(true)
    setError(undefined)
    try {
      saved(await postsApi.generateDraft({ subject, objective, audience, platform }))
    } catch (err) {
      setError(err)
    } finally {
      pending.current = false
      setBusy(false)
    }
  }
  return (
    <Modal title="Générer avec l’IA" close={close} busy={busy}>
      <form className="editor-form" onSubmit={submit} aria-busy={busy}>
        <p className="field-note">
          Le contenu sera enregistré comme brouillon (Draft). Une relecture humaine est nécessaire
          avant toute soumission pour validation.
        </p>
        {error != null && <ErrorNotice error={error} />}
        <fieldset disabled={busy}>
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
            disabled={busy || !subject.trim() || !objective.trim() || !audience.trim()}
          >
            {busy ? 'Génération…' : 'Générer le brouillon'}
          </button>
        </footer>
      </form>
    </Modal>
  )
}

export function AiReviewNotice({ warnings }: { warnings: string[] }) {
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
    </div>
  )
}
