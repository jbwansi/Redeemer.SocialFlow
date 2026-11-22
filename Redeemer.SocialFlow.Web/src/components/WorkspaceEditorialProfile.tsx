import { useEffect, useState } from 'react'
import { apiRequest } from '../api/posts'
import { ErrorNotice } from './shared'

const fields = { organizationName: 'Nom de l’organisation', mission: 'Mission', values: 'Valeurs', positioning: 'Positionnement',
  audiences: 'Publics cibles', tone: 'Ton rédactionnel', themes: 'Thèmes privilégiés', rules: 'Règles éditoriales', avoid: 'Formulations à éviter' }
type Profile = { revision: number } & Record<keyof typeof fields, string | null>
const blank: Profile = { revision: 0, organizationName: '', mission: '', values: '', positioning: '', audiences: '', tone: '', themes: '', rules: '', avoid: '' }
export function WorkspaceEditorialProfile({ canManage }: { canManage: boolean }) {
  const [value, setValue] = useState<Profile>(blank)
  const [workspace, setWorkspace] = useState('')
  const [loaded, setLoaded] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()
  useEffect(() => {
    const controller = new AbortController()
    void apiRequest<{ workspaceId: string; profile: Profile | null }>('/workspace-identities/editorial', { signal: controller.signal }).then(result => {
      if (!controller.signal.aborted) { setValue(result.profile || blank); setWorkspace(result.workspaceId); setLoaded(true) }
    }).catch(e => { if (!controller.signal.aborted) setError(e) })
    return () => controller.abort()
  }, [])
  return <section aria-label="Identité éditoriale">
    <h3>Identité éditoriale</h3>
    <p>Espace : {workspace || 'chargement…'} · {!loaded ? 'Chargement du profil…' : value.revision ? `Profil configuré · révision ${value.revision}` : 'Profil absent : politique neutre, sans personnalisation'}</p>
    {error != null && <ErrorNotice error={error} />}
    <form onSubmit={async e => {
      e.preventDefault(); if (!loaded || !canManage || busy) return
      setBusy(true); setError(undefined)
      try { setValue(await apiRequest<Profile>('/workspace-identities/editorial', { method: 'PUT', body: JSON.stringify(value) })) }
      catch (err) { setError(err) } finally { setBusy(false) }
    }}>
      <fieldset disabled={!loaded || !canManage || busy}>
        {(Object.entries(fields) as [keyof typeof fields, string][]).map(([key, label]) => <label key={key}>{label}
          <textarea required={key === 'organizationName'} maxLength={key === 'organizationName' ? 200 : 4000} value={value[key] || ''}
            onChange={e => setValue({ ...value, [key]: e.target.value })} />
        </label>)}
        {canManage && <button type="submit" className="button primary">Enregistrer le profil éditorial</button>}
      </fieldset>
    </form>
    <p>Les champs facultatifs vides ne seront pas inventés. Ce profil ne constitue pas une preuve de certification ou d’affiliation.</p>
  </section>
}
