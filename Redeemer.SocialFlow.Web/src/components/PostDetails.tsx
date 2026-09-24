import { useState } from 'react'
import { postsApi, type Post, type WorkflowAction } from '../api/posts'
import { dateLabel, timeLabel, timezone } from '../lib/dates'
import { ErrorNotice, Modal, PlatformLabel, StatusBadge } from './shared'

// Presentation hints only: the API validates every operation, including stale state.
const actionLabels: Record<WorkflowAction, string> = {
  'submit-for-review': 'Submit for review',
  approve: 'Approve',
  reject: 'Reject',
  schedule: 'Schedule',
  cancel: 'Cancel schedule',
}
export function PostDetails({
  post,
  close,
  edit,
  changed,
  removed,
}: {
  post: Post
  close: () => void
  edit: () => void
  changed: (post: Post) => void
  removed: () => void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()
  const [date, setDate] = useState('')
  const [confirmDelete, setConfirmDelete] = useState(false)
  const editable = post.status === 1 || post.status === 6
  const actions: WorkflowAction[] = editable
    ? ['submit-for-review']
    : post.status === 2
      ? ['approve', 'reject']
      : post.status === 3
        ? ['schedule']
        : post.status === 4
          ? ['cancel']
          : []
  async function act(action: WorkflowAction) {
    setBusy(true)
    setError(undefined)
    try {
      changed(
        await postsApi.transition(
          post.id,
          action,
          action === 'schedule' ? new Date(date).toISOString() : undefined,
        ),
      )
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }
  async function remove() {
    setBusy(true)
    setError(undefined)
    try {
      await postsApi.remove(post.id)
      removed()
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }
  return (
    <Modal title={post.title} close={close} busy={busy}>
      <div className="post-details">
        {error != null && <ErrorNotice error={error} />}
        <div className="detail-meta">
          <PlatformLabel platform={post.platform} />
          <StatusBadge status={post.status} />
        </div>
        <div className="post-content">
          {post.content || (
            <span className="muted">No content yet. Edit this draft to start writing.</span>
          )}
        </div>
        {post.callToAction && (
          <section>
            <h3>Call to action</h3>
            <p>{post.callToAction}</p>
          </section>
        )}
        {post.visualBrief && (
          <section>
            <h3>Visual brief</h3>
            <p>{post.visualBrief}</p>
          </section>
        )}
        {post.visualUrl && (
          <section>
            <h3>Visual URL</h3>
            <p className="url-text">{post.visualUrl}</p>
          </section>
        )}
        <div className="detail-dates">
          <span>Created {dateLabel(post.createdAt)}</span>
          {post.scheduledAt && (
            <span>
              Scheduled {dateLabel(post.scheduledAt)} · {timeLabel(post.scheduledAt)}
            </span>
          )}
        </div>
        {post.status === 3 && (
          <label>
            Schedule date and time
            <input
              aria-label="Schedule date and time"
              type="datetime-local"
              value={date}
              onChange={(e) => setDate(e.target.value)}
              disabled={busy}
            />
            <small>Your timezone: {timezone}.</small>
          </label>
        )}
        {confirmDelete && (
          <div className="delete-confirm" role="alert">
            <strong>Delete this post?</strong>
            <p>This permanently removes the post.</p>
            <button className="button danger" disabled={busy} onClick={remove}>
              Confirm deletion
            </button>
            <button className="button" disabled={busy} onClick={() => setConfirmDelete(false)}>
              Keep post
            </button>
          </div>
        )}
      </div>
      <footer className="modal-footer detail-actions">
        {editable && (
          <>
            <button
              className="text-button danger-text"
              disabled={busy}
              onClick={() => setConfirmDelete(true)}
            >
              Delete
            </button>
            <button className="button" disabled={busy} onClick={edit}>
              Edit post
            </button>
          </>
        )}
        {actions.map((action) => (
          <button
            key={action}
            className={`button ${action === 'reject' || action === 'cancel' ? '' : 'primary'}`}
            disabled={busy || (action === 'schedule' && !date)}
            onClick={() => act(action)}
          >
            {busy ? 'Working…' : actionLabels[action]}
          </button>
        ))}
      </footer>
    </Modal>
  )
}
