import { ArrowUpRight, CalendarDays, FileText, Clock3, CheckCircle2 } from 'lucide-react'
import { statuses, type Post, type Status } from '../api/posts'
import { dateLabel, timeLabel } from '../lib/dates'
import { EmptyState, PlatformLabel, StatusBadge } from '../components/shared'

export function Dashboard({
  posts,
  open,
  navigate,
  create,
}: {
  posts: Post[]
  open: (post: Post) => void
  navigate: (page: 'posts' | 'calendar', status?: Status) => void
  create: () => void
}) {
  const count = (status: Status) => posts.filter((post) => post.status === status).length
  const upcoming = posts
    .filter(
      (post) =>
        post.status === 4 && post.scheduledAt && new Date(post.scheduledAt).getTime() >= Date.now(),
    )
    .sort((a, b) => Date.parse(a.scheduledAt!) - Date.parse(b.scheduledAt!))
    .slice(0, 5)
  const recent = [...posts]
    .sort((a, b) => Date.parse(b.updatedAt) - Date.parse(a.updatedAt))
    .slice(0, 4)
  const stats = [
    { title: 'All posts', value: posts.length, icon: FileText, hint: 'Your editorial library' },
    {
      title: 'Ready for review',
      value: count(2),
      icon: Clock3,
      hint: 'Waiting for a fresh pair of eyes',
      status: 2 as Status,
    },
    {
      title: 'Scheduled',
      value: count(4),
      icon: CalendarDays,
      hint: 'On the editorial calendar',
      status: 4 as Status,
    },
    {
      title: 'Published',
      value: count(5),
      icon: CheckCircle2,
      hint: 'Marked as published',
      status: 5 as Status,
    },
  ]
  return (
    <>
      <section className="welcome-banner">
        <div>
          <span className="eyebrow">MAKE EVERY MESSAGE MATTER</span>
          <h2>
            Good ideas deserve
            <br />a clear plan.
          </h2>
          <p>Your content, your team’s next steps, one shared workspace.</p>
          <button className="button dark" onClick={create}>
            Start a new post <ArrowUpRight size={17} />
          </button>
        </div>
        <div className="banner-art" aria-hidden="true">
          <div className="art-orbit" />
          <div className="art-card art-back">
            <i />
            <i />
            <i />
          </div>
          <div className="art-card art-front">
            <span className="art-logo">R</span>
            <i />
            <i />
            <div className="art-check">
              <CheckCircle2 size={22} />
            </div>
          </div>
          <span className="art-dot" />
        </div>
      </section>
      <section className="stats-grid" aria-label="Post overview">
        {stats.map(({ title, value, icon: Icon, hint, status }) => (
          <button className="stat-card" key={title} onClick={() => navigate('posts', status)}>
            <div className="stat-top">
              <span>{title}</span>
              <Icon size={19} />
            </div>
            <strong>{value}</strong>
            <small>{hint}</small>
          </button>
        ))}
      </section>
      <div className="dashboard-columns">
        <section className="panel">
          <div className="panel-heading">
            <div>
              <h2>Coming up next</h2>
              <p>Upcoming scheduled posts</p>
            </div>
            <button className="text-button" onClick={() => navigate('calendar')}>
              View calendar <ArrowUpRight size={15} />
            </button>
          </div>
          {upcoming.length ? (
            <div className="upcoming-list">
              {upcoming.map((post) => (
                <button className="upcoming-item" key={post.id} onClick={() => open(post)}>
                  <span className="date-tile">
                    <small>
                      {new Date(post.scheduledAt!).toLocaleDateString(undefined, {
                        month: 'short',
                      })}
                    </small>
                    <strong>{new Date(post.scheduledAt!).getDate()}</strong>
                  </span>
                  <span className="upcoming-content">
                    <strong>{post.title}</strong>
                    <PlatformLabel platform={post.platform} />
                  </span>
                  <span className="muted">{timeLabel(post.scheduledAt!)}</span>
                </button>
              ))}
            </div>
          ) : (
            <EmptyState title="A little room for your next idea">
              Scheduled posts will appear here once they’re approved and planned.
            </EmptyState>
          )}
        </section>
        <section className="panel workflow-panel">
          <div className="panel-heading">
            <div>
              <h2>Workflow at a glance</h2>
              <p>From first draft to final post</p>
            </div>
          </div>
          {Object.entries(statuses).map(([id, label]) => (
            <button
              className="workflow-row"
              key={id}
              onClick={() => navigate('posts', Number(id) as Status)}
            >
              <span className={`workflow-dot status-${id}`} />
              <span>{label}</span>
              <strong>{count(Number(id) as Status)}</strong>
            </button>
          ))}
        </section>
      </div>
      <section className="panel">
        <div className="panel-heading">
          <div>
            <h2>Recently updated</h2>
            <p>Pick up where you left off</p>
          </div>
          <button className="text-button" onClick={() => navigate('posts')}>
            All posts <ArrowUpRight size={15} />
          </button>
        </div>
        {recent.length ? (
          <div className="recent-list">
            {recent.map((post) => (
              <button key={post.id} onClick={() => open(post)} className="recent-item">
                <span>
                  <strong>{post.title}</strong>
                  <small>{dateLabel(post.updatedAt)}</small>
                </span>
                <PlatformLabel platform={post.platform} />
                <StatusBadge status={post.status} />
              </button>
            ))}
          </div>
        ) : (
          <EmptyState
            title="Your story starts here"
            action={
              <button className="button" onClick={create}>
                Create your first post
              </button>
            }
          >
            Save your first draft to start building your content library.
          </EmptyState>
        )}
      </section>
    </>
  )
}
