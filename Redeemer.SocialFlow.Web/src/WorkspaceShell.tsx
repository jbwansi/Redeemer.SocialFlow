import { useEffect, useRef, useState, type FormEvent } from 'react'
import App from './App'
import { Team } from './pages/Team'
import { roleNames } from './api/team'
import { workspacesApi, type Workspace } from './api/workspaces'
import { setActiveWorkspace } from './api/workspaceTransport'
import { setLocalAdministration } from './api/posts'
import './workspaces.css'

export function WorkspaceShell({
  email,
  logout,
  busy,
}: {
  email: string
  logout: () => void
  busy: boolean
}) {
  const [workspaces, setWorkspaces] = useState<Workspace[]>([])
  const [active, setActive] = useState<Workspace | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [name, setName] = useState('')
  const [revision, setRevision] = useState(0)
  const [directory, setDirectory] = useState(true)
  const [team, setTeam] = useState(false)
  const pending = useRef<AbortController | null>(null)

  useEffect(() => {
    setLocalAdministration(false)
    setActiveWorkspace(null)
    const controller = new AbortController()
    pending.current = controller
    setLoading(true)
    setError('')
    async function load() {
      try {
        await workspacesApi.personal(controller.signal)
        const list = await workspacesApi.list(controller.signal)
        if (!controller.signal.aborted) setWorkspaces(list)
      } catch {
        if (!controller.signal.aborted)
          setError('Vos espaces sont indisponibles. Réessayez ou contactez votre administrateur.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    const lost = () => {
      pending.current?.abort()
      setActiveWorkspace(null)
      setActive(null)
      setDirectory(true)
      setLoading(false)
      setError('Accès à cet espace refusé ou session expirée. Rechargez vos espaces.')
    }
    window.addEventListener('workspace-access-lost', lost)
    return () => {
      controller.abort()
      pending.current?.abort()
      setActiveWorkspace(null)
      window.removeEventListener('workspace-access-lost', lost)
    }
  }, [revision])

  async function select(id: string) {
    setTeam(false)
    pending.current?.abort()
    const controller = new AbortController()
    pending.current = controller
    setActiveWorkspace(null)
    setActive(null)
    setLoading(true)
    setError('')
    try {
      const selected = await workspacesApi.select(id, controller.signal)
      if (controller.signal.aborted) return
      setActiveWorkspace(selected.id)
      setActive(selected)
      setDirectory(false)
      location.hash = 'dashboard'
    } catch {
      if (!controller.signal.aborted) {
        setDirectory(true)
        setError('Cet espace est inaccessible.')
      }
    } finally {
      if (!controller.signal.aborted) setLoading(false)
    }
  }
  async function create(event: FormEvent) {
    event.preventDefault()
    pending.current?.abort()
    const controller = new AbortController()
    pending.current = controller
    setLoading(true)
    setError('')
    try {
      const workspace = await workspacesApi.create(name.trim(), controller.signal)
      if (controller.signal.aborted) return
      setWorkspaces((list) => [...list, workspace])
      setName('')
      await select(workspace.id)
    } catch {
      if (!controller.signal.aborted) setError('Impossible de créer cet espace. Réessayez.')
    } finally {
      if (!controller.signal.aborted) setLoading(false)
    }
  }
  function showDirectory() {
    setTeam(false)
    pending.current?.abort()
    setActiveWorkspace(null)
    setActive(null)
    setDirectory(true)
    setLoading(false)
    location.hash = 'workspaces'
  }
  const dashboard = !loading && !directory && active && !team
  const workspaceHeader = (
    <header className="workspace-switcher">
      <button className="button" onClick={showDirectory}>
        Mes espaces
      </button>
      <label>
        Espace actif{' '}
        <select
          aria-label="Espace actif"
          value={active?.id ?? ''}
          onChange={(event) => void select(event.target.value)}
          disabled={loading}
        >
          <option value="" disabled>
            Sélectionner un espace
          </option>
          {workspaces.map((workspace) => (
            <option key={workspace.id} value={workspace.id}>
              {workspace.name}
            </option>
          ))}
        </select>
      </label>
      <span>{email}</span>
      {active?.type === 2 && (
        <button
          className="button"
          onClick={() => {
            if (team) void select(active.id)
            else setTeam(true)
          }}
        >
          {team ? 'Publications' : 'Équipe'}
        </button>
      )}
      <button
        className="button"
        disabled={busy}
        onClick={() => {
          showDirectory()
          logout()
        }}
      >
        Se déconnecter
      </button>
    </header>
  )
  return (
    <>
      {!dashboard && workspaceHeader}
      {error && (
        <p role="alert" className="workspace-access-error">
          {error} <button onClick={() => setRevision((value) => value + 1)}>Réessayer</button>
        </p>
      )}
      {loading ? (
        <p role="status" className="workspace-directory">
          Chargement des espaces…
        </p>
      ) : directory || !active ? (
        <main className="workspace-directory">
          <span className="eyebrow">REDEEMER SOCIALFLOW</span>
          <h1>Mes espaces</h1>
          <p>Chaque espace possède ses propres publications et son calendrier.</p>
          <div className="workspace-cards">
            {workspaces.map((workspace) => (
              <article key={workspace.id}>
                <span>{workspace.type === 1 ? 'Personnel' : 'Professionnel'}</span>
                <h2>{workspace.name}</h2>
                <p>{roleNames[workspace.role]}</p>
                <button className="button primary" onClick={() => void select(workspace.id)}>
                  Ouvrir {workspace.name}
                </button>
              </article>
            ))}
          </div>
          <form className="workspace-create" onSubmit={create}>
            <h2>Créer un espace professionnel</h2>
            <label htmlFor="workspace-name">Nom de l’espace</label>
            <input
              id="workspace-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              maxLength={120}
              required
            />
            <button className="button primary" type="submit" disabled={!name.trim()}>
              Créer l’espace
            </button>
          </form>
        </main>
      ) : (
        <>
          {team ? (
            <Team
              key={active.id}
              workspaceId={active.id}
              email={email}
              left={() => {
                showDirectory()
                setRevision((value) => value + 1)
              }}
            />
          ) : (
            <App
              key={active.id}
              workspaceName={active.name}
              workspaceHeader={workspaceHeader}
              localAdministration={false}
              canEdit={active.role !== 4}
            />
          )}
        </>
      )}
    </>
  )
}
