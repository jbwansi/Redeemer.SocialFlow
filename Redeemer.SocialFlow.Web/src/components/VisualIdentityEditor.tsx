import { useId, useState } from 'react'
import type { VisualIdentity } from '../api/visuals'
import { ApiError } from '../api/posts'
import {
  cleanIdentity,
  emptyIdentity,
  loadIdentities,
  redeemerIdentity,
  saveIdentity,
  validColor,
} from '../lib/visualIdentities'
import { ErrorNotice } from './shared'

export function VisualIdentityEditor({
  value,
  onChange,
  localLibrary = true,
}: {
  localLibrary?: boolean
  value: VisualIdentity
  onChange: (identity: VisualIdentity) => void
}) {
  const prefix = useId()
  const [stored, setStored] = useState(() => {
    try {
      return { entries: loadIdentities(), failed: false }
    } catch {
      return { entries: [] as VisualIdentity[], failed: true }
    }
  })
  const [message, setMessage] = useState('')
  const [error, setError] = useState<unknown>()
  function change(identity: VisualIdentity) {
    setMessage('')
    setError(undefined)
    onChange(identity)
  }
  function colorAt(index: number, color: string) {
    change({ ...value, colors: value.colors.map((c, i) => (i === index ? color : c)) })
  }
  return (
    <>
      {localLibrary && (
        <>
          <label>
            Identités enregistrées sur ce navigateur
            <select
              value=""
              onChange={(e) => {
                const identity = stored.entries.find((v) => v.identityId === e.target.value)
                if (identity) change({ ...identity, colors: [...identity.colors] })
              }}
            >
              <option value="">Choisir une identité…</option>
              {stored.entries.map((v) => (
                <option key={v.identityId} value={v.identityId}>
                  {v.name}
                </option>
              ))}
            </select>
          </label>
          <div className="visual-actions">
            <button type="button" className="button" onClick={() => change(redeemerIdentity())}>
              Utiliser le modèle Redeemer Holding
            </button>
            <button type="button" className="button" onClick={() => change(emptyIdentity())}>
              Nouvelle identité
            </button>
          </div>
        </>
      )}
      <label>
        Identifiant de l’identité
        <input
          value={value.identityId}
          maxLength={100}
          onChange={(e) => change({ ...value, identityId: e.target.value })}
        />
      </label>
      <label>
        Nom de l’identité
        <input
          value={value.name}
          maxLength={200}
          onChange={(e) => change({ ...value, name: e.target.value })}
        />
      </label>
      <label>
        Style visuel
        <textarea
          value={value.style}
          maxLength={1000}
          onChange={(e) => change({ ...value, style: e.target.value })}
        />
      </label>
      <label>
        Instructions visuelles (facultatif)
        <textarea
          value={value.instructions || ''}
          maxLength={4000}
          onChange={(e) => change({ ...value, instructions: e.target.value })}
        />
      </label>
      <div className="identity-palette" role="group" aria-label="Palette de couleurs">
        {value.colors.map((color, index) => {
          const valid = validColor(color)
          const errorId = prefix + '-color-' + index
          return (
            <div className="identity-color" key={index}>
              <input
                type="color"
                className="color-swatch"
                aria-label={'Choisir la couleur ' + (index + 1)}
                value={valid ? color : '#000000'}
                onChange={(e) => colorAt(index, e.target.value.toUpperCase())}
              />
              <label>
                Code hexadécimal {index + 1}
                <input
                  className="color-code"
                  type="text"
                  value={color}
                  maxLength={7}
                  spellCheck={false}
                  aria-invalid={!valid}
                  aria-describedby={!valid ? errorId : undefined}
                  onChange={(e) => colorAt(index, e.target.value.toUpperCase())}
                />
              </label>
              <button
                type="button"
                className="text-button danger-text"
                aria-label={'Supprimer la couleur ' + (index + 1)}
                disabled={value.colors.length === 1}
                onClick={() =>
                  change({ ...value, colors: value.colors.filter((_, i) => i !== index) })
                }
              >
                Supprimer
              </button>
              {!valid && (
                <small id={errorId} className="color-error">
                  Format attendu : #RRGGBB (exemple : #DA2E29).
                </small>
              )}
            </div>
          )
        })}
        <button
          type="button"
          className="button"
          disabled={value.colors.length >= 8}
          onClick={() => change({ ...value, colors: [...value.colors, '#000000'] })}
        >
          Ajouter une couleur
        </button>
        <p className="field-note">{value.colors.length} / 8 couleurs — une couleur minimum.</p>
      </div>
      <label>
        Slogan (facultatif)
        <input
          value={value.slogan || ''}
          maxLength={300}
          onChange={(e) => change({ ...value, slogan: e.target.value })}
        />
      </label>
      {localLibrary && (
        <>
          <button
            type="button"
            className="button"
            onClick={() => {
              if (!cleanIdentity(value)) {
                setError(
                  new ApiError({
                    title: 'Identité incomplète',
                    detail:
                      'Renseignez un identifiant, un nom, un style et 1 à 8 couleurs valides au format #RRGGBB.',
                  }),
                )
                return
              }
              try {
                setStored({ entries: saveIdentity(value), failed: false })
                setError(undefined)
                setMessage('Identité enregistrée sur ce navigateur.')
              } catch {
                setError(
                  new ApiError({
                    title: 'Sauvegarde locale indisponible',
                    detail:
                      'Le stockage est bloqué, plein ou contient des données invalides. Vous pouvez continuer sans sauvegarder.',
                  }),
                )
              }
            }}
          >
            Enregistrer l’identité localement
          </button>
          <p className="field-note">
            Sauvegarde sur cet appareil et cette adresse uniquement. Aucun secret ni contenu de
            publication n’est enregistré. Un même identifiant remplace sa version sauvegardée.
          </p>
          {stored.failed && (
            <p role="status">
              Les identités locales ne peuvent pas être chargées. Le formulaire reste utilisable.
            </p>
          )}
        </>
      )}
      {error != null && <ErrorNotice error={error} />}
      {message && <p role="status">{message}</p>}
    </>
  )
}
