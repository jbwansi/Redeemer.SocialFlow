import { useEffect, useRef, useState } from 'react'
import { apiRequest } from '../api/posts'
import { ErrorNotice } from './shared'

type Term = { id: string; workspaceId: string; revision: number; canonical: string; aliases: string[]; category: number;
  language: string; usageContext: string; note: string | null; source: string | null; status: number; verifiedAt: string | null }
const blank = { canonical: '', aliases: '', category: 1, language: '', usageContext: '', note: '', source: '' }
const categories = ['Marque', 'Méthode', 'Certification', 'Organisation', 'Technologie', 'Acronyme', 'Concept']
const statuses = ['Proposé', 'Validé dans cet espace', 'Retiré']
export function WorkspaceTerminology({ canManage }: { canManage: boolean }) {
  const [terms, setTerms] = useState<Term[]>([])
  const [selected, setSelected] = useState<Term>()
  const [draft, setDraft] = useState(blank)
  const [busy, setBusy] = useState(true)
  const [error, setError] = useState<unknown>()
  const active = useRef<AbortController | null>(null)
  useEffect(() => {
    const controller = new AbortController(); active.current = controller
    void apiRequest<Term[]>('/workspace-terminology', { signal: controller.signal }).then(x => {
      if (!controller.signal.aborted) setTerms(x)
    }).catch(e => { if (!controller.signal.aborted) setError(e) })
      .finally(() => { if (!controller.signal.aborted) { active.current = null; setBusy(false) } })
    return () => { controller.abort(); active.current?.abort() }
  }, [])
  function choose(term?: Term) {
    setSelected(term)
    setDraft(term ? { canonical: term.canonical, aliases: term.aliases.join('\n'), category: term.category,
      language: term.language, usageContext: term.usageContext, note: term.note || '', source: term.source || '' } : blank)
  }
  async function write(action?: 'validate' | 'retire') {
    if (busy || !canManage || active.current) return
    const controller = new AbortController(); active.current = controller; setBusy(true); setError(undefined)
    try {
      const path = '/workspace-terminology' + (selected ? '/' + selected.id : '') + (action ? '/' + action : '')
      const term = await apiRequest<Term>(path, { method: action || !selected ? 'POST' : 'PUT', signal: controller.signal,
        body: JSON.stringify(action ? { revision: selected!.revision } : { ...draft, revision: selected?.revision || 0,
          aliases: draft.aliases.split('\n').map(x => x.trim()).filter(Boolean) }) })
      const list = await apiRequest<Term[]>('/workspace-terminology', { signal: controller.signal })
      if (!controller.signal.aborted) { setTerms(list); choose(term) }
    } catch (e) { if (!controller.signal.aborted) setError(e) }
    finally { if (!controller.signal.aborted) { active.current = null; setBusy(false) } }
  }
  const changed = selected && JSON.stringify(draft) !== JSON.stringify({ canonical: selected.canonical, aliases: selected.aliases.join('\n'),
    category: selected.category, language: selected.language, usageContext: selected.usageContext, note: selected.note || '', source: selected.source || '' })
  return <section aria-label="Référentiel terminologique">
    <h3>Référentiel terminologique de cet espace</h3>
    <p>La validation confirme une graphie et sa source dans cet espace, sans établir d’affiliation ni de certification. Les termes absents restent non vérifiés.</p>
    {error != null && <ErrorNotice error={error} />}
    <label>Terme enregistré<select disabled={busy} value={selected?.id || ''} onChange={e => choose(terms.find(x => x.id === e.target.value))}>
      <option value="">Nouveau terme</option>
      {terms.map(t => <option key={t.id} value={t.id}>{t.canonical} · {statuses[t.status - 1]} · v{t.revision}</option>)}
    </select></label>
    {selected && <p role="status">{statuses[selected.status - 1]} · version {selected.revision} · espace {selected.workspaceId}
      {selected.verifiedAt && ` · vérifié le ${new Date(selected.verifiedAt).toLocaleDateString()}`}</p>}
    <form onSubmit={e => { e.preventDefault(); void write() }}>
      <fieldset disabled={busy || !canManage}>
        <label>Appellation canonique exacte<input required maxLength={200} value={draft.canonical} onChange={e => setDraft({ ...draft, canonical: e.target.value })} /></label>
        <label>Variantes reconnues (une par ligne, maximum 10)<textarea value={draft.aliases} onChange={e => setDraft({ ...draft, aliases: e.target.value })} /></label>
        <label>Catégorie<select value={draft.category} onChange={e => setDraft({ ...draft, category: Number(e.target.value) })}>
          {categories.map((c, i) => <option key={c} value={i + 1}>{c}</option>)}
        </select></label>
        <label>Langue<input maxLength={30} value={draft.language} onChange={e => setDraft({ ...draft, language: e.target.value })} /></label>
        <label>Contexte d’utilisation<input maxLength={300} value={draft.usageContext} onChange={e => setDraft({ ...draft, usageContext: e.target.value })} /></label>
        <label>Définition ou note d’usage<textarea maxLength={1000} value={draft.note} onChange={e => setDraft({ ...draft, note: e.target.value })} /></label>
        <label>Source de référence<input maxLength={1000} value={draft.source} onChange={e => setDraft({ ...draft, source: e.target.value })} /></label>
        {canManage && <button type="submit">Enregistrer comme proposé</button>}
        {canManage && selected && <>
          <button type="button" disabled={!!changed || selected.status !== 1 || !selected.source?.trim()} onClick={() => void write('validate')}>Valider la graphie et la source enregistrées</button>
          <button type="button" disabled={!!changed || selected.status === 3} onClick={() => void write('retire')}>Retirer ce terme</button>
        </>}
      </fieldset>
    </form>
    <p>Modifier un terme le remet au statut proposé. Un contexte spécifique impose une revue humaine ; il n’est pas déduit automatiquement. Maximum 100 termes validés.</p>
  </section>
}
