import { useEffect, useRef, useState } from 'react'
import { apiRequest } from '../api/posts'
import { ErrorNotice } from './shared'
export type EditorialAngle = { id: string; workspaceId: string; revision: number; name: string; description: string;
  objective: string; instructions: string; callToAction: string; active: boolean; displayOrder: number }
const blank = { name: '', description: '', objective: '', instructions: '', callToAction: '', active: true, displayOrder: 0 }
export function WorkspaceEditorialAngles({ canManage }: { canManage: boolean }) {
  const [angles, setAngles] = useState<EditorialAngle[]>([])
  const [selected, setSelected] = useState<EditorialAngle>()
  const [draft, setDraft] = useState(blank)
  const [busy, setBusy] = useState(true)
  const [error, setError] = useState<unknown>()
  const [loaded, setLoaded] = useState(false)
  const active = useRef<AbortController | null>(null)
  useEffect(() => {
    const controller = new AbortController(); active.current = controller
    void apiRequest<EditorialAngle[]>('/workspace-editorial-angles', { signal: controller.signal }).then(x => {
      if (!controller.signal.aborted) { setAngles(x); setLoaded(true) }
    }).catch(e => { if (!controller.signal.aborted) setError(e) })
      .finally(() => { if (!controller.signal.aborted) { active.current = null; setBusy(false) } })
    return () => { controller.abort(); active.current?.abort() }
  }, [])
  function choose(angle?: EditorialAngle) {
    setSelected(angle)
    setDraft(angle ? { name: angle.name, description: angle.description, objective: angle.objective, instructions: angle.instructions,
      callToAction: angle.callToAction, active: angle.active, displayOrder: angle.displayOrder } : blank)
  }
  async function save(initialize = false) {
    if (!canManage || busy || active.current) return
    const controller = new AbortController(); active.current = controller; setBusy(true); setError(undefined)
    try {
      await apiRequest('/workspace-editorial-angles' + (initialize ? '/initialize' : selected ? '/' + selected.id : ''), {
        method: initialize || !selected ? 'POST' : 'PUT', signal: controller.signal,
        ...(!initialize && { body: JSON.stringify({ ...draft, revision: selected?.revision || 0 }) }) })
      const list = await apiRequest<EditorialAngle[]>('/workspace-editorial-angles', { signal: controller.signal })
      if (!controller.signal.aborted) { setAngles(list); setLoaded(true); choose(selected ? list.find(x => x.id === selected.id) : undefined) }
    } catch (e) { if (!controller.signal.aborted) setError(e) }
    finally { if (!controller.signal.aborted) { active.current = null; setBusy(false) } }
  }
  return <section aria-label="Angles éditoriaux">
    <h3>Angles éditoriaux de cet espace</h3>
    <p>Une intention pour une publication ; le profil permanent reste inchangé. Aucune sélection par défaut.</p>
    {error != null && <ErrorNotice error={error} />}
    {canManage && loaded && !angles.length && <button type="button" disabled={busy} onClick={() => void save(true)}>Importer le catalogue initial configuré</button>}
    <label>Angle enregistré<select disabled={busy} value={selected?.id || ''} onChange={e => choose(angles.find(x => x.id === e.target.value))}>
      <option value="">Nouvel angle</option>
      {angles.map(x => <option key={x.id} value={x.id}>{x.name} · {x.active ? 'Actif' : 'Inactif'} · v{x.revision}</option>)}
    </select></label>
    <form onSubmit={e => { e.preventDefault(); void save() }}><fieldset disabled={busy || !canManage}>
      <label>Nom de l’angle<input required maxLength={200} value={draft.name} onChange={e => setDraft({ ...draft, name: e.target.value })} /></label>
      <label>Description de l’angle<textarea maxLength={4000} value={draft.description} onChange={e => setDraft({ ...draft, description: e.target.value })} /></label>
      <label>Objectif de communication<textarea maxLength={4000} value={draft.objective} onChange={e => setDraft({ ...draft, objective: e.target.value })} /></label>
      <label>Consignes de rédaction<textarea maxLength={4000} value={draft.instructions} onChange={e => setDraft({ ...draft, instructions: e.target.value })} /></label>
      <label>Appel à l’action recommandé<textarea maxLength={4000} value={draft.callToAction} onChange={e => setDraft({ ...draft, callToAction: e.target.value })} /></label>
      <label>Angle actif<input type="checkbox" checked={draft.active} onChange={e => setDraft({ ...draft, active: e.target.checked })} /></label>
      <label>Ordre d’affichage<input type="number" min={0} max={10000} required value={draft.displayOrder} onChange={e => setDraft({ ...draft, displayOrder: Number(e.target.value) })} /></label>
      {canManage && <button type="submit">Enregistrer l’angle</button>}
    </fieldset></form>
    <p>Les modifications et désactivations concernent les prochaines générations. Les brouillons conservent les consignes utilisées dans leur audit.</p>
  </section>
}
