import { useEffect, useRef, useState } from 'react'
import { identitiesApi, workspaceIdentitiesApi, effectiveIdentity, type LibraryIdentity } from '../api/visualIdentities'
import type { VisualIdentity } from '../api/visuals'
import { ApiError } from '../api/posts'
import { cleanIdentity, emptyIdentity, loadIdentities } from '../lib/visualIdentities'
import { VisualIdentityEditor } from './VisualIdentityEditor'
import { ErrorNotice } from './shared'

export function IdentityLogoPreview({ identity, workspace = false }: { identity: LibraryIdentity; workspace?: boolean }) {
  const api = workspace ? workspaceIdentitiesApi : identitiesApi
  const [url, setUrl] = useState('')
  const [error, setError] = useState<unknown>()
  useEffect(() => {
    const controller = new AbortController()
    let objectUrl: string | undefined
    setUrl('')
    setError(undefined)
    if (identity.logoId)
      void api
        .logo(identity.id, identity.logoId, controller.signal)
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
  }, [api, identity.id, identity.logoId])
  if (error) return <ErrorNotice error={error} />
  return url ? (
    <img className="visual-preview" src={url} alt="Logo officiel de l’identité" />
  ) : (
    <p className="field-note">
      {identity.logoId ? 'Chargement du logo…' : 'Aucun logo officiel importé.'}
    </p>
  )
}

export function VisualIdentityLibrary({
  value,
  onChange,
  onBusy,
  workspace = false,
  canManage = true,
}: {
  workspace?: boolean
  canManage?: boolean
  value: VisualIdentity
  onChange: (value: VisualIdentity) => void
  onBusy?: (busy: boolean) => void
}) {
  const api = workspace ? workspaceIdentitiesApi : identitiesApi
  const [legacy, setLegacy] = useState<VisualIdentity[]>([])
  const [entries, setEntries] = useState<LibraryIdentity[]>([])
  const [busy, setBusy] = useState(true)
  const [error, setError] = useState<unknown>()
  const [notice, setNotice] = useState('')
  const active = useRef<AbortController | null>(null)
  const callbacks = useRef({ onChange, onBusy })
  callbacks.current = { onChange, onBusy }
  const selected = entries.find((v) => v.id === value.libraryId)
  useEffect(() => {
    const controller = new AbortController()
    active.current = controller
    callbacks.current.onBusy?.(true)
    void api
      .list(controller.signal)
      .then((list) => {
        if (controller.signal.aborted) return
        setEntries(list)
        const initial = list.find((v) => v.isDefault && v.isActive)
        if (initial) callbacks.current.onChange(effectiveIdentity(initial))
      })
      .catch((err) => {
        if (!controller.signal.aborted) setError(err)
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          active.current = null
          setBusy(false)
          callbacks.current.onBusy?.(false)
        }
      })
    return () => {
      controller.abort()
      active.current?.abort()
      callbacks.current.onBusy?.(false)
    }
  }, [api])
  async function run(
    action: (signal: AbortSignal) => Promise<LibraryIdentity | void>,
    preserveParameters = false,
  ) {
    if (active.current) return
    const controller = new AbortController()
    active.current = controller
    setBusy(true)
    onBusy?.(true)
    setError(undefined)
    setNotice('')
    try {
      const result = await action(controller.signal)
      const list = await api.list(controller.signal)
      if (controller.signal.aborted) return
      setEntries(list)
      onChange(
        result
          ? preserveParameters
            ? {
                ...value,
                libraryId: result.id,
                revision: result.revision,
                isActive: result.isActive,
              }
            : effectiveIdentity(result)
          : emptyIdentity(),
      )
      setNotice('Bibliothèque mise à jour.')
    } catch (err) {
      if (!controller.signal.aborted) setError(err)
    } finally {
      if (!controller.signal.aborted) {
        active.current = null
        setBusy(false)
        callbacks.current.onBusy?.(false)
      }
    }
  }
  function save() {
    if (!cleanIdentity(value)) {
      setError(
        new ApiError({
          title: 'Identité incomplète',
          detail: 'Renseignez un identifiant, un nom, un style et 1 à 8 couleurs #RRGGBB.',
        }),
      )
      return
    }
    void run((signal) =>
      selected
        ? api.update(selected.id, value.revision ?? selected.revision, value, signal)
        : api.create(value, signal),
    )
  }
  return (
    <section aria-label="Bibliothèque d’identités visuelles">
      {error != null && <ErrorNotice error={error} />}
      {notice && <p role="status">{notice}</p>}
      {workspace && <p>Identités de cet espace. Enregistrer ne signifie pas approuver.</p>}
      {selected && <p role="status">{selected.name} · révision {selected.revision} · {selected.isActive ? 'Active' : 'Inactive'} · {selected.approvalId ? 'Approuvée' : 'Enregistrée, non approuvée'}</p>}
      {workspace && selected && canManage && <button type="button" className="button primary" disabled={busy || !selected.isActive || !!selected.approvalId || JSON.stringify(value) !== JSON.stringify(effectiveIdentity(selected))} onClick={() => void run(async signal => {
        await api.approve(selected.id, selected.revision, signal)
        return (await api.list(signal)).find(x => x.id === selected.id)
      })}>Approuver cette révision enregistrée</button>}
      {busy && <p role="status">Chargement de la bibliothèque…</p>}
      {!busy && error == null && !entries.length && <p className="identity-empty">Aucune identité visuelle enregistrée dans cette bibliothèque.</p>}
      <fieldset disabled={busy}>
        <label>
          Identité de la bibliothèque
          <select
            value={value.libraryId || ''}
            onChange={(e) => {
              const identity = entries.find((v) => v.id === e.target.value)
              onChange(identity ? effectiveIdentity(identity) : emptyIdentity())
              setError(undefined)
              setNotice('')
            }}
          >
            <option value="">Nouvelle identité / paramètres ponctuels</option>
            {entries.map((v) => (
              <option key={v.id} value={v.id}>
                {v.name}
                {v.isDefault ? ' · Par défaut' : ''}
                {!v.isActive ? ' · Désactivée' : ''}
              </option>
            ))}
          </select>
        </label>
        <p className="field-note">
          Les ajustements ci-dessous concernent la prochaine génération. La bibliothèque ne change
          que si vous cliquez sur Enregistrer.
        </p>
        <fieldset disabled={!canManage}>
        <VisualIdentityEditor value={value} onChange={onChange} localLibrary={false} />
        <div className="visual-actions">
          <button type="button" className="button primary" onClick={save}>
            {selected ? 'Enregistrer les modifications de l’identité' : 'Créer cette identité'}
          </button>
          <button type="button" className="button" onClick={() => onChange(emptyIdentity())}>
            Nouvelle identité
          </button>
          {selected && (
            <>
              <button
                type="button"
                className="button"
                onClick={() => onChange(effectiveIdentity(selected))}
              >
                Rétablir les paramètres enregistrés
              </button>
              <button
                type="button"
                className="button"
                disabled={!selected.isActive || selected.isDefault}
                onClick={() =>
                  void run(
                    (signal) =>
                      api.setDefault(
                        selected.id,
                        value.revision ?? selected.revision,
                        signal,
                      ),
                    true,
                  )
                }
              >
                Définir par défaut
              </button>
              <button
                type="button"
                className="button"
                onClick={() =>
                  void run(
                    (signal) =>
                      api.update(
                        selected.id,
                        value.revision ?? selected.revision,
                        { ...effectiveIdentity(selected), isActive: !selected.isActive },
                        signal,
                      ),
                    true,
                  )
                }
              >
                {selected.isActive ? 'Désactiver l’identité' : 'Réactiver l’identité'}
              </button>
              <button
                type="button"
                className="button danger"
                onClick={() => {
                  if (
                    window.confirm(
                      'Supprimer cette identité ? Une identité déjà utilisée doit être désactivée.',
                    )
                  )
                    void run((signal) =>
                      api.remove(
                        selected.id,
                        value.revision ?? selected.revision,
                        signal,
                      ),
                    )
                }}
              >
                Supprimer l’identité
              </button>
            </>
          )}
          <button
            type="button"
            className="text-button"
            onClick={() =>
              void run(async (signal) => {
                const list = await api.list(signal)
                return list.find((v) => v.id === value.libraryId)
              })
            }
          >
            Actualiser la bibliothèque
          </button>
        </div>
        </fieldset>
        {selected && (
          <>
            {!selected.isActive && (
              <p role="status">
                Cette identité est désactivée et ne peut pas servir à une génération.
              </p>
            )}
            <IdentityLogoPreview identity={selected} workspace={workspace} />
            <label className="button">
              Importer le logo officiel
              <input
                type="file"
                disabled={!canManage}
                className="visual-file-input"
                aria-label="Importer le logo officiel"
                accept="image/png,image/jpeg"
                onChange={(e) => {
                  const file = e.target.files?.[0]
                  e.target.value = ''
                  if (!file) return
                  if (
                    !['image/png', 'image/jpeg'].includes(file.type) ||
                    !file.size ||
                    file.size > 20 * 1024 * 1024
                  ) {
                    setError(
                      new ApiError({
                        title: 'Logo non accepté',
                        detail: 'Choisissez un PNG ou JPEG non vide, de 20 Mio maximum.',
                      }),
                    )
                    return
                  }
                  void run(
                    (signal) =>
                      api.uploadLogo(
                        selected.id,
                        value.revision ?? selected.revision,
                        file,
                        signal,
                      ),
                    true,
                  )
                }}
              />
            </label>
            <p className="field-note">
              Le fichier original est conservé. Le logo n’est ni généré ni ajouté automatiquement
              aux visuels.
              {workspace && <> Dans une publication, choisissez une présentation de marque puis « Composer un aperçu de marque » sur un candidat.</>}
            </p>
          </>
        )}
        {!workspace && <button
          type="button"
          className="text-button"
          onClick={() => {
            try {
              const local = loadIdentities()
              setLegacy(local)
              setNotice(
                local.length
                  ? 'Choisissez une identité locale à importer.'
                  : 'Aucune identité locale enregistrée.',
              )
            } catch {
              setError(new ApiError({ title: 'Sauvegarde locale indisponible' }))
            }
          }}
        >
          Récupérer les anciennes identités de ce navigateur
        </button>}
        {legacy.length > 0 && (
          <label>
            Identité locale à récupérer
            <select
              value=""
              onChange={(e) => {
                const item = legacy.find((v) => v.identityId === e.target.value)
                if (item) onChange({ ...item, colors: [...item.colors] })
              }}
            >
              <option value="">Choisir…</option>
              {legacy.map((v) => (
                <option key={v.identityId} value={v.identityId}>
                  {v.name}
                </option>
              ))}
            </select>
          </label>
        )}
      </fieldset>
    </section>
  )
}
