import { useEffect, useState } from 'react'
import { Search, SlidersHorizontal, ArrowUpRight } from 'lucide-react'
import {
  postsApi,
  platforms,
  statuses,
  type Post,
  type Platform,
  type Status,
  type Filters,
} from '../api/posts'
import { dateBoundary, dateLabel } from '../lib/dates'
import { EmptyState, ErrorNotice, PlatformLabel, StatusBadge } from '../components/shared'

export function Posts({
  open,
  create,
  initialStatus,
  revision,
}: {
  open: (post: Post) => void
  create: () => void
  initialStatus?: Status
  revision: number
}) {
  const [platform, setPlatform] = useState('')
  const [status, setStatus] = useState(initialStatus ? String(initialStatus) : '')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [search, setSearch] = useState('')
  const [posts, setPosts] = useState<Post[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<unknown>()
  const [retry, setRetry] = useState(0)
  useEffect(() => {
    const controller = new AbortController()
    setLoading(true)
    setError(undefined)
    const filters: Filters = {
      platform: platform ? (Number(platform) as Platform) : undefined,
      status: status ? (Number(status) as Status) : undefined,
      createdFrom: dateBoundary(from),
      createdTo: dateBoundary(to, true),
    }
    postsApi
      .list(filters, controller.signal)
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
  }, [platform, status, from, to, revision, retry])
  const visible = posts.filter((post) =>
    `${post.title} ${post.content}`.toLocaleLowerCase().includes(search.toLocaleLowerCase()),
  )
  function reset() {
    setPlatform('')
    setStatus('')
    setFrom('')
    setTo('')
    setSearch('')
  }
  return (
    <section className="panel posts-panel">
      <div className="posts-toolbar">
        <div className="search-field">
          <Search size={18} />
          <input
            aria-label="Search posts"
            placeholder="Search your posts…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <span className="filter-heading">
          <SlidersHorizontal size={16} />
          Filter your library
        </span>
      </div>
      <div className="filters">
        <label>
          Platform
          <select value={platform} onChange={(e) => setPlatform(e.target.value)}>
            <option value="">All platforms</option>
            {Object.entries(platforms).map(([value, name]) => (
              <option key={value} value={value}>
                {name}
              </option>
            ))}
          </select>
        </label>
        <label>
          Status
          <select value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="">All statuses</option>
            {Object.entries(statuses).map(([value, name]) => (
              <option key={value} value={value}>
                {name}
              </option>
            ))}
          </select>
        </label>
        <label>
          Created from
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label>
          Created to
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <button className="text-button" onClick={reset}>
          Reset filters
        </button>
      </div>
      {loading ? (
        <p className="loading" role="status">
          Loading posts…
        </p>
      ) : error != null ? (
        <div className="panel-padding">
          <ErrorNotice error={error} retry={() => setRetry((value) => value + 1)} />
        </div>
      ) : visible.length ? (
        <>
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Post</th>
                  <th>Platform</th>
                  <th>Status</th>
                  <th>Created</th>
                  <th>Scheduled</th>
                  <th>
                    <span className="sr-only">Open post</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {visible.map((post) => (
                  <tr key={post.id}>
                    <td>
                      <button className="post-title" onClick={() => open(post)}>
                        {post.title}
                      </button>
                      <p className="post-excerpt">{post.content || 'No content yet'}</p>
                    </td>
                    <td>
                      <PlatformLabel platform={post.platform} />
                    </td>
                    <td>
                      <StatusBadge status={post.status} />
                    </td>
                    <td className="nowrap">{dateLabel(post.createdAt)}</td>
                    <td className="nowrap">
                      {post.scheduledAt ? dateLabel(post.scheduledAt) : '—'}
                    </td>
                    <td>
                      <button
                        className="icon-button"
                        onClick={() => open(post)}
                        aria-label={`Open ${post.title}`}
                      >
                        <ArrowUpRight size={18} />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="table-footer">
            {visible.length} {visible.length === 1 ? 'post' : 'posts'} · All dates in your local
            timezone
          </div>
        </>
      ) : (
        <EmptyState
          title={
            platform || status || from || to || search
              ? 'No posts match these filters'
              : 'Your next story starts here'
          }
          action={
            <button className="button" onClick={create}>
              Create a post
            </button>
          }
        >
          Try a different filter or create something new.
        </EmptyState>
      )}
    </section>
  )
}
