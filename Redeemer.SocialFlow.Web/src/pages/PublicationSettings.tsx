import { useEffect, useState, type FormEvent } from 'react'
import { publicationSettingsApi, type PublicationSettings as Settings } from '../api/publicationSettings'
import { socialConnectionsApi, type Connection } from '../api/socialConnections'
import { ErrorNotice } from '../components/shared'

export function PublicationSettings({ workspaceId, settings, changed }: {
  workspaceId: string
  settings?: Settings
  changed: (settings: Settings) => void
}) {
  const [connections, setConnections] = useState<Connection[]>()
  const [value, setValue] = useState(settings)
  const [times, setTimes] = useState(settings?.suggestedTimes.join(', ') ?? '')
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    Promise.all([publicationSettingsApi.get(controller.signal), socialConnectionsApi.list(workspaceId, controller.signal)])
      .then(([current, accounts]) => {
        if (controller.signal.aborted) return
        setConnections(accounts)
        setValue(current)
        setTimes(current.suggestedTimes.join(', '))
      })
      .catch(err => { if (!controller.signal.aborted) setError(err) })
    return () => controller.abort()
  }, [workspaceId])

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!value || !connections || busy) return
    setBusy(true)
    setError(undefined)
    setSaved(false)
    try {
      const result = await publicationSettingsApi.save({
        ...value, timeZone: value.timeZone.trim(), suggestedTimes: times.split(',').map(x => x.trim()).filter(Boolean),
      })
      changed(result)
      setValue(result)
      setTimes(result.suggestedTimes.join(', '))
      setSaved(true)
    } catch (err) { setError(err) }
    finally { setBusy(false) }
  }

  return (
    <section aria-label="Paramètres de publication">
      {error != null && <ErrorNotice error={error} />}
      {saved && <p role="status">Paramètres enregistrés.</p>}
      {value && connections && (
        <form className="editor-form" onSubmit={save}>
          <fieldset disabled={busy}>
            {([['facebookConnectionId', 'Facebook', 1, 'Page'], ['linkedInConnectionId', 'LinkedIn', 2, 'Profile']] as const).map(([field, name, platform, type]) => {
              const choices = connections.filter(x => x.platform === platform && x.accountType === type)
              return (
                <label key={field}>
                  Destination préférée {name}
                  <select value={value[field] ?? ''} onChange={e => setValue({ ...value, [field]: e.target.value || null })}>
                    <option value="">Aucune</option>
                    {value[field] && !choices.some(x => x.id === value[field]) && <option value={value[field]!}>Ancienne destination indisponible</option>}
                    {choices.map(x => <option key={x.id} value={x.id}>{x.displayName || x.externalAccountId}</option>)}
                  </select>
                </label>
              )
            })}
            <label>
              Fuseau horaire (IANA)
              <input required maxLength={100} placeholder="Europe/Zurich" value={value.timeZone}
                onChange={e => setValue({ ...value, timeZone: e.target.value })} />
            </label>
            <label>
              Créneaux proposés (HH:mm, séparés par des virgules)
              <input maxLength={64} placeholder="09:00, 12:30, 17:00" value={times} onChange={e => setTimes(e.target.value)} />
            </label>
            <label>
              <span><input type="checkbox" checked={value.facebookAutomaticEnabled}
                onChange={e => setValue({ ...value, facebookAutomaticEnabled: e.target.checked })} /> Autoriser le traitement automatique Facebook dans cet espace</span>
            </label>
            <label>
              <span><input type="checkbox" checked={value.linkedInAutomaticEnabled}
                onChange={e => setValue({ ...value, linkedInAutomaticEnabled: e.target.checked })} /> Autoriser le traitement automatique LinkedIn dans cet espace</span>
            </label>
            <p>Ces options ne programment aucun post. Le worker serveur et l’autorisation explicite de chaque post restent nécessaires. Réactiver un réseau peut reprendre ses envois déjà autorisés.</p>
          </fieldset>
          <button type="submit" className="button primary" disabled={busy}>Enregistrer les paramètres</button>
        </form>
      )}
    </section>
  )
}
