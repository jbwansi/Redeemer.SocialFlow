import { useEffect, useState } from 'react'
import { teamApi, roleNames, type Role, type Team as TeamData } from '../api/team'

export function Team({
  workspaceId,
  email,
  left,
}: {
  workspaceId: string
  email: string
  left: () => void
}) {
  const [data, setData] = useState<TeamData>()
  const [revision, refresh] = useState(0)
  const [address, setAddress] = useState('')
  const [role, setRole] = useState<Role>(2)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [confirmation, setConfirmation] = useState<{
    label: string
    action: () => Promise<void>
    leave?: boolean
  }>()
  useEffect(() => {
    const controller = new AbortController()
    teamApi
      .get(workspaceId, controller.signal)
      .then((value) => {
        if (!controller.signal.aborted) setData(value)
      })
      .catch(() => {
        if (!controller.signal.aborted) setError('Équipe inaccessible. Rechargez vos espaces.')
      })
    return () => controller.abort()
  }, [workspaceId, revision])
  async function run(action: () => Promise<void>, leave = false) {
    setBusy(true)
    setError('')
    setMessage('')
    try {
      await action()
      setConfirmation(undefined)
      if (leave) {
        left()
        return
      }
      setMessage('Modification enregistrée.')
      refresh((x) => x + 1)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Opération impossible.')
      refresh((x) => x + 1)
    } finally {
      setBusy(false)
    }
  }
  if (!data)
    return (
      <section>
        <h1>Équipe</h1>
        <p role={error ? 'alert' : 'status'}>{error || 'Chargement…'}</p>
      </section>
    )
  const manage = data.myRole === 1 || data.myRole === 3
  const roles = ([1, 3, 2, 4] as Role[]).filter((r) => data.myRole === 1 || r !== 1)
  const own = data.members.find((m) => m.email.toLowerCase() === email.toLowerCase())
  return (
    <section className="workspace-directory">
      <h1>Équipe</h1>
      <p>
        Votre rôle : {roleNames[data.myRole]}. Les personnes autorisées peuvent approuver leurs
        propres publications.
      </p>
      {error && <p role="alert">{error}</p>}
      {message && <p role="status">{message}</p>}
      {confirmation && (
        <div role="alert">
          <p>{confirmation.label}</p>
          <button
            className="button danger"
            disabled={busy}
            onClick={() => void run(confirmation.action, confirmation.leave)}
          >
            Confirmer
          </button>
          <button className="button" disabled={busy} onClick={() => setConfirmation(undefined)}>
            Annuler
          </button>
        </div>
      )}
      <ul>
        {data.members.map((member) => (
          <li key={member.userId}>
            <strong>{member.email}</strong> · {roleNames[member.role]}{' '}
            {manage && (data.myRole === 1 || member.role !== 1) && (
              <>
                <select
                  aria-label={`Rôle de ${member.email}`}
                  value={member.role}
                  disabled={busy}
                  onChange={(e) => {
                    const next = Number(e.target.value) as Role
                    setConfirmation({
                      label: `Attribuer le rôle ${roleNames[next]} à ${member.email} ?`,
                      action: () => teamApi.change(workspaceId, member, next),
                    })
                  }}
                >
                  {roles.map((r) => (
                    <option key={r} value={r}>
                      {roleNames[r]}
                    </option>
                  ))}
                </select>
                <button
                  className="button"
                  disabled={busy}
                  onClick={() =>
                    setConfirmation({
                      label: `Retirer ${member.email} de cet espace ?`,
                      action: () => teamApi.remove(workspaceId, member),
                      leave: member.userId === own?.userId,
                    })
                  }
                >
                  Retirer
                </button>
              </>
            )}
          </li>
        ))}
      </ul>
      {manage && (
        <>
          <form
            className="workspace-create"
            onSubmit={(e) => {
              e.preventDefault()
              void run(() => teamApi.invite(workspaceId, address, role))
            }}
          >
            <h2>Inviter un collaborateur</h2>
            <label>
              Email
              <input
                type="email"
                maxLength={254}
                required
                value={address}
                onChange={(e) => setAddress(e.target.value)}
              />
            </label>
            <label>
              Rôle
              <select value={role} onChange={(e) => setRole(Number(e.target.value) as Role)}>
                {roles.map((r) => (
                  <option key={r} value={r}>
                    {roleNames[r]}
                  </option>
                ))}
              </select>
            </label>
            <p>Le lien personnel expire après une heure.</p>
            <button className="button primary" disabled={busy}>
              Envoyer l’invitation
            </button>
          </form>
          <h2>Invitations en attente</h2>
          {data.invitations.length === 0 && <p>Aucune invitation en attente.</p>}
          <ul>
            {data.invitations.map((invitation) => (
              <li key={invitation.id}>
                {invitation.email} · {roleNames[invitation.role]} · expire le{' '}
                {new Date(invitation.expiresAtUtc).toLocaleString()}{' '}
                {(data.myRole === 1 || invitation.role !== 1) && (
                  <button
                    className="button"
                    disabled={busy}
                    onClick={() =>
                      setConfirmation({
                        label: `Annuler l’invitation de ${invitation.email} ?`,
                        action: () => teamApi.revoke(workspaceId, invitation),
                      })
                    }
                  >
                    Annuler l’invitation
                  </button>
                )}
              </li>
            ))}
          </ul>
        </>
      )}
      {own && (
        <button
          className="button danger"
          disabled={busy}
          onClick={() =>
            setConfirmation({
              label:
                'Quitter cet espace ? Le dernier Owner doit d’abord désigner un autre propriétaire.',
              action: () => teamApi.leave(workspaceId, own.revision),
              leave: true,
            })
          }
        >
          Quitter l’espace
        </button>
      )}
    </section>
  )
}
