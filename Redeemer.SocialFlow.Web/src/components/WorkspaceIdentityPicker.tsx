import { useEffect, useState } from 'react'
import { effectiveIdentity, workspaceIdentitiesApi, type LibraryIdentity } from '../api/visualIdentities'
import type { VisualIdentity } from '../api/visuals'
import { emptyIdentity } from '../lib/visualIdentities'
import { VisualIdentityEditor } from './VisualIdentityEditor'
import { ErrorNotice } from './shared'

export function WorkspaceIdentityPicker({ value, onChange }: { value: VisualIdentity; onChange: (value: VisualIdentity) => void }) {
  const [entries, setEntries] = useState<LibraryIdentity[]>([])
  const [error, setError] = useState<unknown>()
  useEffect(() => {
    const controller = new AbortController()
    void workspaceIdentitiesApi.list(controller.signal).then(list => {
      if (!controller.signal.aborted) setEntries(list)
    }).catch(e => { if (!controller.signal.aborted) setError(e) })
    return () => controller.abort()
  }, [])
  const selected = entries.find(x => x.id === value.libraryId)
  return <div>
    {error != null && <ErrorNotice error={error} />}
    <label>Identité graphique approuvée de cet espace
      <select value={value.libraryId || ''} onChange={e => {
        const item = entries.find(x => x.id === e.target.value)
        onChange(item ? { ...effectiveIdentity(item), identityMode: 'Approved' } : emptyIdentity())
      }}>
        <option value="">Aucune identité approuvée sélectionnée</option>
        {entries.filter(x => x.isActive && x.approvalId).map(x => <option key={x.id} value={x.id}>{x.name} · révision {x.revision}</option>)}
      </select>
    </label>
    {selected && <p>{selected.name} · Approuvée · espace {selected.workspaceId} · révision {selected.revision}</p>}
    {!entries.some(x => x.isActive && x.approvalId) && <p>Aucune révision graphique active et approuvée. Un Owner/Admin peut en approuver une dans Identités.</p>}
    <label><input type="checkbox" checked={value.identityMode === 'Temporary'} onChange={e => onChange({ ...emptyIdentity(), identityMode: e.target.checked ? 'Temporary' : undefined })} />
      Utiliser explicitement une identité temporaire, non approuvée
    </label>
    {value.identityMode === 'Temporary' && <>
      <p>Paramètres temporaires pour cette génération, sans logo officiel ni approbation de bibliothèque.</p>
      <VisualIdentityEditor value={value} onChange={v => onChange({ ...v, identityMode: 'Temporary', libraryId: undefined, revision: undefined })} localLibrary={false} />
    </>}
  </div>
}
