import { useEffect, useRef, useState } from 'react'
import { apiRequest, type Post } from '../api/posts'
import { ErrorNotice } from './shared'

export function PostTerminologyCheck({ post, disabled }: { post: Post; disabled: boolean }) {
  const [warnings, setWarnings] = useState<string[]>()
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)
  const active = useRef<AbortController | null>(null)
  useEffect(() => {
    setWarnings(undefined); setError(undefined); setBusy(false)
    return () => active.current?.abort()
  }, [post.id, post.updatedAt])
  return <section aria-label="Contrôle terminologique">
    <button type="button" disabled={disabled || busy} onClick={async () => {
      const controller = new AbortController(); active.current = controller; setBusy(true); setError(undefined)
      try {
        const result = await apiRequest<{ warnings: string[] }>('/posts/' + encodeURIComponent(post.id) + '/terminology-check', { signal: controller.signal })
        if (!controller.signal.aborted) setWarnings(result.warnings)
      } catch (e) { if (!controller.signal.aborted) setError(e) }
      finally { if (!controller.signal.aborted) setBusy(false) }
    }}>Vérifier la terminologie enregistrée (sans coût)</button>
    {error != null && <ErrorNotice error={error} />}
    {warnings && (warnings.length ? <div role="alert"><strong>Revue humaine requise</strong><ul>{warnings.map((w, i) => <li key={i}>{w}</li>)}</ul></div>
      : <p>Aucun écart détecté parmi les termes connus. Les termes inconnus et les faits ne sont pas certifiés.</p>)}
  </section>
}
