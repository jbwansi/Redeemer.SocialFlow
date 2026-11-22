import { useEffect, useId, useState, type ReactNode } from 'react'
import { visualsApi, workspaceVisualsApi } from '../api/visuals'
import { WorkspaceEditorialProfile } from '../components/WorkspaceEditorialProfile'
import { WorkspaceEditorialAngles } from '../components/WorkspaceEditorialAngles'
import { WorkspaceTerminology } from '../components/WorkspaceTerminology'
import { ApiError } from '../api/posts'
import { emptyIdentity } from '../lib/visualIdentities'
import { VisualIdentityLibrary } from '../components/VisualIdentityLibrary'
import { ErrorNotice } from '../components/shared'
export function VisualIdentities({ workspace = false, canManage = true }: { workspace?: boolean; canManage?: boolean }) {
  const [enabled, setEnabled] = useState<boolean>()
  const [error, setError] = useState<unknown>()
  const [identity, setIdentity] = useState(emptyIdentity)
  useEffect(() => {
    const controller = new AbortController()
    void (workspace ? workspaceVisualsApi : visualsApi)
      .runtime(controller.signal)
      .then((v) => {
        if (!controller.signal.aborted) setEnabled(v.enabled)
      })
      .catch((err) => {
        if (!controller.signal.aborted) {
          if (err instanceof ApiError && err.problem.status === 404) setEnabled(false)
          else setError(err)
        }
      })
    return () => controller.abort()
  }, [workspace])
  const graphics = error ? <ErrorNotice error={error} />
    : enabled === undefined ? <p role="status">Chargement des identités graphiques…</p>
      : !enabled ? <p>Gestion des visuels désactivée sur ce poste.</p>
        : <VisualIdentityLibrary value={identity} onChange={setIdentity} workspace={workspace} canManage={canManage} />
  if (!workspace) return <div className="visual-manager">{graphics}</div>
  return (
    <div className="identities-page">
      <header className="identities-intro">
        <div><h2>Identités de l’espace</h2><p>Définissez votre voix, vos repères visuels et vos intentions de publication.</p></div>
        <span className="identity-access">{canManage ? 'Gestion de l’espace' : 'Lecture seule'}</span>
      </header>
      <p className="identities-help">Ouvrez les rubriques utiles. Replier un panneau conserve vos saisies ; pensez à les enregistrer avant de quitter cet espace.</p>
      <IdentityPanel title="Identité éditoriale" description="Mission, publics et règles permanentes de votre organisation.">
        <WorkspaceEditorialProfile canManage={canManage} />
      </IdentityPanel>
      <IdentityPanel title="Identités visuelles" description="Styles, couleurs, logos et approbation de chaque révision."
        status={error ? 'Indisponible' : enabled === undefined ? 'Chargement…' : enabled ? undefined : 'Désactivées'}>
        {graphics}
      </IdentityPanel>
      <IdentityPanel title="Terminologie professionnelle" description="Appellations exactes, variantes et sources validées pour cet espace.">
        <WorkspaceTerminology canManage={canManage} />
      </IdentityPanel>
      <IdentityPanel title="Angles éditoriaux" description="Intentions de rédaction ponctuelles, sans modifier le profil permanent.">
        <WorkspaceEditorialAngles canManage={canManage} />
      </IdentityPanel>
    </div>
  )
}

function IdentityPanel({ title, description, status, initialOpen = false, children }: {
  title: string; description: string; status?: string; initialOpen?: boolean; children: ReactNode
}) {
  const [open, setOpen] = useState(initialOpen)
  const id = useId()
  return <section className="identity-panel">
    <h3 className="identity-panel-heading"><button type="button" id={id + '-toggle'} aria-expanded={open}
      aria-controls={id + '-content'} aria-describedby={id + '-description'} onClick={() => setOpen(!open)}>
      <span>{title}</span>{status && <small className="identity-panel-status">{status}</small>}
      <span className="identity-chevron" aria-hidden="true">{open ? '−' : '+'}</span>
    </button></h3>
    <p id={id + '-description'} className="identity-panel-description">{description}</p>
    <div id={id + '-content'} className="identity-panel-content" aria-labelledby={id + '-toggle'} hidden={!open}>
      {children}
    </div>
  </section>
}
