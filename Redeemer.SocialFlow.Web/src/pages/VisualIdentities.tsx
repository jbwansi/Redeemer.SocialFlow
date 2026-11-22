import { useEffect, useState } from 'react'
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
  return (
    <div className="visual-manager">
      {workspace && <WorkspaceEditorialProfile canManage={canManage} />}
      {workspace && <WorkspaceTerminology canManage={canManage} />}
      {workspace && <WorkspaceEditorialAngles canManage={canManage} />}
      {error ? <ErrorNotice error={error} />
        : enabled === undefined ? <p role="status">Chargement des identités graphiques…</p>
          : !enabled ? <p>Gestion des visuels désactivée sur ce poste.</p>
            : <VisualIdentityLibrary value={identity} onChange={setIdentity} workspace={workspace} canManage={canManage} />}
    </div>
  )
}
