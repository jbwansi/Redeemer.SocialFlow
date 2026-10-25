import { useState } from 'react'
import { teamApi } from './api/team'
export function InvitationAcceptance({
  code,
  email,
  done,
  logout,
}: {
  code: string
  email: string
  done: () => void
  logout: () => void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  async function accept() {
    setBusy(true)
    setError('')
    try {
      await teamApi.accept(code)
      done()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Invitation inaccessible.')
    } finally {
      setBusy(false)
    }
  }
  return (
    <main className="workspace-directory">
      <h1>Rejoindre une équipe</h1>
      <p>
        Vous êtes connecté avec {email}. Cette adresse doit correspondre au destinataire de
        l’invitation.
      </p>
      {error && <p role="alert">{error}</p>}
      <button className="button primary" disabled={busy} onClick={() => void accept()}>
        Accepter l’invitation
      </button>
      <button className="button" disabled={busy} onClick={logout}>
        Changer de compte
      </button>
      <button className="button" disabled={busy} onClick={done}>
        Retour aux espaces
      </button>
    </main>
  )
}
