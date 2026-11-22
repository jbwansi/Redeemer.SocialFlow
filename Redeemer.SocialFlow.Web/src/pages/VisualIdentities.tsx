import { useEffect, useState } from 'react'
import { visualsApi, workspaceVisualsApi } from '../api/visuals'
import { WorkspaceEditorialProfile } from '../components/WorkspaceEditorialProfile'
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
  if (error) return <ErrorNotice error={error} />
  if (enabled === undefined) return <p role="status">Chargement…</p>
  if (!enabled) return <>{workspace && <><WorkspaceEditorialProfile canManage={canManage} /><WorkspaceTerminology canManage={canManage} /></>}<p>Gestion des visuels désactivée sur ce poste.</p></>
  return (
    <div className="visual-manager">
      {workspace && <WorkspaceEditorialProfile canManage={canManage} />}
      {workspace && <WorkspaceTerminology canManage={canManage} />}
      <VisualIdentityLibrary value={identity} onChange={setIdentity} workspace={workspace} canManage={canManage} />
    </div>
  )
}
