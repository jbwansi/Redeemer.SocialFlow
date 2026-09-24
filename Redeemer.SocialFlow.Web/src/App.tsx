import { useCallback, useEffect, useState } from 'react'
import {
  LayoutDashboard,
  CalendarDays,
  FileText,
  Plus,
  ArrowUpRight,
  RefreshCw,
} from 'lucide-react'
import { postsApi, type Post, type Status } from './api/posts'
import { Dashboard } from './pages/Dashboard'
import { Calendar } from './pages/Calendar'
import { Posts } from './pages/Posts'
import { ErrorNotice } from './components/shared'
import { PostEditor } from './components/PostEditor'
import { PostDetails } from './components/PostDetails'

type Page = 'dashboard' | 'calendar' | 'posts'
const pages = {
  dashboard: {
    title: 'Dashboard',
    description: 'A clear view of your content, from idea to impact.',
    icon: LayoutDashboard,
  },
  calendar: {
    title: 'Editorial Calendar',
    description: 'Give every story its moment.',
    icon: CalendarDays,
  },
  posts: {
    title: 'Posts',
    description: 'Create, refine, and move your content forward.',
    icon: FileText,
  },
}
function currentPage(): Page {
  const hash = window.location.hash.slice(1)
  return hash === 'calendar' || hash === 'posts' ? hash : 'dashboard'
}
export default function App() {
  const [page, setPage] = useState<Page>(currentPage)
  const [posts, setPosts] = useState<Post[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<unknown>()
  const [revision, setRevision] = useState(0)
  const [initialStatus, setInitialStatus] = useState<Status>()
  const [selected, setSelected] = useState<Post>()
  const [editor, setEditor] = useState<{ post?: Post }>()
  const [notice, setNotice] = useState('')
  useEffect(() => {
    const handle = () => {
      setPage(currentPage())
      setInitialStatus(undefined)
    }
    window.addEventListener('hashchange', handle)
    return () => window.removeEventListener('hashchange', handle)
  }, [])
  useEffect(() => {
    const controller = new AbortController()
    setLoading(true)
    setError(undefined)
    postsApi
      .list({}, controller.signal)
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
  }, [revision])
  useEffect(() => {
    if (notice) {
      const timer = setTimeout(() => setNotice(''), 5000)
      return () => clearTimeout(timer)
    }
  }, [notice])
  const refresh = useCallback(() => setRevision((value) => value + 1), [])
  function navigate(next: Page, status?: Status) {
    window.history.pushState(null, '', `#${next}`)
    setPage(next)
    setInitialStatus(status)
  }
  function changed(post: Post) {
    setSelected(post)
    setNotice('Post updated successfully.')
    refresh()
  }
  return (
    <div className="app-shell">
      <a
        className="skip-link"
        href="#main"
        onClick={(event) => {
          event.preventDefault()
          document.getElementById('main')?.focus()
        }}
      >
        Skip to content
      </a>
      <aside className="sidebar">
        <a className="brand" href="#dashboard" aria-label="Redeemer Holding SocialFlow home">
          <span className="brand-mark">
            R<span />
          </span>
          <span>
            REDEEMER<small>H O L D I N G</small>
          </span>
        </a>
        <div className="workspace-label">CONTENT WORKSPACE</div>
        <nav aria-label="Main navigation">
          {Object.entries(pages).map(([id, { title, icon: Icon }]) => (
            <a
              key={id}
              href={`#${id}`}
              aria-current={page === id ? 'page' : undefined}
              onClick={(event) => {
                event.preventDefault()
                navigate(id as Page)
              }}
            >
              <Icon size={19} />
              {title}
              {page === id && <span className="nav-dot" />}
            </a>
          ))}
        </nav>
        <div className="sidebar-note">
          <span className="small-red-line" />
          <h3>
            A stronger voice.
            <br />A shared vision.
          </h3>
          <p>
            Make space for content
            <br />
            that moves people.
          </p>
          <span>
            SocialFlow <ArrowUpRight size={14} />
          </span>
        </div>
        <div className="workspace-profile">
          <span>RH</span>
          <div>
            <strong>Redeemer Holding</strong>
            <small>Editorial workspace</small>
          </div>
        </div>
      </aside>
      <div className="workspace">
        <header className="topbar">
          <span>
            Workspace <span className="breadcrumb-slash">/</span>{' '}
            <strong>{pages[page].title}</strong>
          </span>
          <span className="workspace-indicator">
            <i />
            SocialFlow
          </span>
        </header>
        <main id="main" tabIndex={-1}>
          <div className="page-heading">
            <div>
              <span className="eyebrow">REDEEMER SOCIALFLOW</span>
              <h1>{pages[page].title}</h1>
              <p>{pages[page].description}</p>
            </div>
            <div className="heading-actions">
              <button
                className="icon-button refresh"
                aria-label="Refresh posts"
                onClick={refresh}
                disabled={loading}
              >
                <RefreshCw size={18} />
              </button>
              <button className="button primary" onClick={() => setEditor({})}>
                <Plus size={18} />
                Create post
              </button>
            </div>
          </div>
          {notice && (
            <div className="toast" role="status">
              {notice}
            </div>
          )}
          {page === 'posts' ? (
            <Posts
              key={`posts-${initialStatus ?? 'all'}`}
              open={setSelected}
              create={() => setEditor({})}
              initialStatus={initialStatus}
              revision={revision}
            />
          ) : loading ? (
            <div className="loading" role="status">
              <span className="loading-spinner" />
              Loading your workspace…
            </div>
          ) : error != null ? (
            <ErrorNotice error={error} retry={refresh} />
          ) : page === 'dashboard' ? (
            <Dashboard
              posts={posts}
              open={setSelected}
              navigate={navigate}
              create={() => setEditor({})}
            />
          ) : (
            <Calendar posts={posts} open={setSelected} />
          )}
          <footer className="workspace-footer">
            <span>REDEEMER HOLDING</span>
            <span>Thoughtful content. Meaningful connections.</span>
          </footer>
        </main>
      </div>
      {editor && (
        <PostEditor
          post={editor.post}
          close={() => setEditor(undefined)}
          saved={(post) => {
            setEditor(undefined)
            setSelected(post)
            setNotice(editor.post ? 'Changes saved.' : 'Your draft is ready.')
            refresh()
          }}
        />
      )}{' '}
      {!editor && selected && (
        <PostDetails
          key={selected.id}
          post={selected}
          close={() => setSelected(undefined)}
          edit={() => setEditor({ post: selected })}
          changed={changed}
          removed={() => {
            setSelected(undefined)
            setNotice('Post deleted.')
            refresh()
          }}
        />
      )}
    </div>
  )
}
