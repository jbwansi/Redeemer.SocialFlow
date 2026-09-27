import { useEffect, useState } from 'react'
import { postsApi, type TrashedPost } from '../api/posts'
import { EmptyState, ErrorNotice, PlatformLabel, StatusBadge } from '../components/shared'
import { dateLabel, timeLabel, timezone } from '../lib/dates'

export function Trash({ revision }: { revision: number }) {
  const [posts, setPosts] = useState<TrashedPost[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<unknown>()
  const [retry, setRetry] = useState(0)
  useEffect(() => {
    const controller = new AbortController()
    setLoading(true)
    setError(undefined)
    postsApi
      .trash(controller.signal)
      .then((result) => {
        if (!controller.signal.aborted) setPosts(result)
      })
      .catch((err) => {
        if (!controller.signal.aborted) setError(err)
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => controller.abort()
  }, [revision, retry])
  return (
    <section className="panel posts-panel" aria-label="Deleted posts">
      <div className="panel-padding">
        <p>
          Ces publications supprimées sont archivées et ne sont plus actives. Consultation
          uniquement.
        </p>
      </div>
      {loading ? (
        <p className="loading" role="status">
          Chargement de la corbeille…
        </p>
      ) : error != null ? (
        <div className="panel-padding">
          <ErrorNotice error={error} retry={() => setRetry((value) => value + 1)} />
        </div>
      ) : posts.length === 0 ? (
        <EmptyState title="La corbeille est vide">
          Les publications supprimées apparaîtront ici.
        </EmptyState>
      ) : (
        <>
          <div
            className="table-scroll"
            role="region"
            aria-label="Publications supprimées"
            tabIndex={0}
          >
            <table>
              <thead>
                <tr>
                  <th>Title</th>
                  <th>Platform</th>
                  <th>Status before deletion</th>
                  <th>Deleted at</th>
                </tr>
              </thead>
              <tbody>
                {posts.map((post) => (
                  <tr key={post.id}>
                    <td>{post.title}</td>
                    <td>
                      <PlatformLabel platform={post.platform} />
                    </td>
                    <td>
                      <StatusBadge status={post.status} />
                    </td>
                    <td className="nowrap">
                      <time dateTime={post.deletedAt}>
                        {dateLabel(post.deletedAt)} · {timeLabel(post.deletedAt)}
                      </time>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="table-footer">
            {posts.length} publication(s) supprimée(s) · Heure locale : {timezone}
          </div>
        </>
      )}
    </section>
  )
}
