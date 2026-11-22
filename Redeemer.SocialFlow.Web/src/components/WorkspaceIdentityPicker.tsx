import { useEffect, useRef, useState } from 'react'
import { effectiveIdentity, workspaceIdentitiesApi, type LibraryIdentity } from '../api/visualIdentities'
import type { VisualIdentity } from '../api/visuals'
import { emptyIdentity } from '../lib/visualIdentities'
import { VisualIdentityEditor } from './VisualIdentityEditor'
import { ApiError } from '../api/posts'
import { ErrorNotice } from './shared'

export function WorkspaceIdentityPicker({ value, onChange, onBusy }: { value: VisualIdentity; onChange: (value: VisualIdentity) => void; onBusy?: (busy: boolean) => void }) {
  const [entries, setEntries] = useState<LibraryIdentity[]>([])
  const [error, setError] = useState<unknown>()
  const [loading, setLoading] = useState(true)
  const [reload, setReload] = useState(0)
  const explicit = useRef(false)
  const latest = useRef({ value, onChange, onBusy })
  latest.current = { value, onChange, onBusy }
  useEffect(() => {
    const controller = new AbortController()
    setLoading(true); setError(undefined); latest.current.onBusy?.(true)
    void workspaceIdentitiesApi.list(controller.signal).then(list => {
      if (controller.signal.aborted) return
      setEntries(list)
      const defaults = list.filter(x => x.isDefault && x.isActive && x.approvalId)
      if (!explicit.current && JSON.stringify(latest.current.value) === JSON.stringify(emptyIdentity()) && defaults.length === 1)
        latest.current.onChange({ ...effectiveIdentity(defaults[0]), identityMode: 'Approved' })
    }).catch(e => { if (!controller.signal.aborted) setError(e) })
      .finally(() => { if (!controller.signal.aborted) { setLoading(false); latest.current.onBusy?.(false) } })
    return () => { controller.abort(); latest.current.onBusy?.(false) }
  }, [reload])
  const eligible = entries.filter(x => x.isActive && x.approvalId)
  const selected = eligible.find(x => x.id === value.libraryId && x.revision === value.revision)
  const errorMessage = error instanceof ApiError && error.problem.status === 401 ? 'Session expirée. Reconnectez-vous pour charger les identités.'
    : error instanceof ApiError && error.problem.status === 403 ? 'Accès aux identités refusé pour cette opération.'
      : 'Impossible de charger les identités graphiques. Actualisez la liste pour réessayer.'
  return <div>
    {loading && <p role="status">Chargement des identités graphiques approuvées…</p>}
    {error != null && <><p role="alert">{errorMessage}</p><ErrorNotice error={error} /></>}
    <button type="button" className="button" disabled={loading} onClick={() => setReload(x => x + 1)}>Actualiser les identités</button>
    <label>Identité graphique approuvée de cet espace
      <select disabled={loading} value={value.libraryId ? (selected ? value.libraryId : '__previous') : ''} onChange={e => {
        explicit.current = true
        const item = eligible.find(x => x.id === e.target.value)
        onChange(item ? { ...effectiveIdentity(item), identityMode: 'Approved' } : emptyIdentity())
      }}>
        <option value="">Aucune identité approuvée sélectionnée</option>
        {value.libraryId && !selected && <option value="__previous" disabled>Sélection précédente · révision {value.revision} à vérifier</option>}
        {eligible.map(x => <option key={x.id} value={x.id}>{x.name} · révision {x.revision}</option>)}
      </select>
    </label>
    {!loading && error == null && selected && <p>{selected.name} · Approuvée · espace {selected.workspaceId} · révision {selected.revision}</p>}
    {!loading && error == null && value.libraryId && !selected && <p role="alert">La sélection précédente n’est plus une révision active et approuvée. Choisissez à nouveau une identité ; vos paramètres ont été conservés.</p>}
    {!loading && error == null && !eligible.length && <p>Aucune révision graphique active et approuvée. Un Owner/Admin peut en approuver une dans Identités.</p>}
    <label><input type="checkbox" checked={value.identityMode === 'Temporary'} onChange={e => { explicit.current = true; onChange({ ...emptyIdentity(), identityMode: e.target.checked ? 'Temporary' : undefined }) }} />
      Utiliser explicitement une identité temporaire, non approuvée
    </label>
    {value.identityMode === 'Temporary' && <>
      <p>Paramètres temporaires pour cette génération, sans logo officiel ni approbation de bibliothèque.</p>
      <VisualIdentityEditor value={value} onChange={v => onChange({ ...v, identityMode: 'Temporary', libraryId: undefined, revision: undefined })} localLibrary={false} />
    </>}
  </div>
}
