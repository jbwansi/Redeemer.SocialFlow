import { useEffect, useState } from 'react'
import { visualsApi } from '../api/visuals'
import { ApiError } from '../api/posts'
import { emptyIdentity } from '../lib/visualIdentities'
import { VisualIdentityLibrary } from '../components/VisualIdentityLibrary'
import { ErrorNotice } from '../components/shared'
export function VisualIdentities() {
  const [enabled, setEnabled] = useState<boolean>()
  const [error, setError] = useState<unknown>()
  const [identity, setIdentity] = useState(emptyIdentity)
  useEffect(() => {
    const controller = new AbortController()
    void visualsApi
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
  }, [])
  if (error) return <ErrorNotice error={error} />
  if (enabled === undefined) return <p role="status">Chargement…</p>
  if (!enabled) return <p>Gestion des visuels désactivée sur ce poste.</p>
  return (
    <div className="visual-manager">
      <VisualIdentityLibrary value={identity} onChange={setIdentity} />
    </div>
  )
}
