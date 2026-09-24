import { AlertCircle, X, Inbox } from 'lucide-react'
import { useEffect, useRef, type ReactNode } from 'react'
import { ApiError, platforms, statuses, type Platform, type Status } from '../api/posts'

export function StatusBadge({ status }: { status: Status }) {
  return (
    <span className={`badge status-${status}`}>
      <i />
      {statuses[status]}
    </span>
  )
}
export function PlatformLabel({ platform }: { platform: Platform }) {
  return (
    <span className="platform">
      <span className={`platform-icon platform-${platform}`}>
        {platform === 1 ? 'f' : platform === 2 ? 'in' : '◎'}
      </span>
      {platforms[platform]}
    </span>
  )
}
export function ErrorNotice({ error, retry }: { error: unknown; retry?: () => void }) {
  const problem =
    error instanceof ApiError
      ? error.problem
      : { title: 'Something went wrong', detail: 'Please try again.' }
  return (
    <div className="error-notice" role="alert">
      <AlertCircle size={20} />
      <div>
        <strong>{problem.title || 'Request failed'}</strong>
        {problem.detail && <p>{problem.detail}</p>}
        {problem.errors && (
          <ul>
            {Object.entries(problem.errors).flatMap(([field, messages]) =>
              messages.map((message, index) => <li key={`${field}-${index}`}>{message}</li>),
            )}
          </ul>
        )}
        {problem.traceId && <small>Reference: {problem.traceId}</small>}
        {retry && (
          <button className="text-button" onClick={retry}>
            Try again
          </button>
        )}
      </div>
    </div>
  )
}
export function EmptyState({
  title,
  children,
  action,
}: {
  title: string
  children: ReactNode
  action?: ReactNode
}) {
  return (
    <div className="empty">
      <span className="empty-icon">
        <Inbox size={26} />
      </span>
      <h3>{title}</h3>
      <p>{children}</p>
      {action}
    </div>
  )
}
export function Modal({
  title,
  children,
  close,
  busy = false,
}: {
  title: string
  children: ReactNode
  close: () => void
  busy?: boolean
}) {
  const ref = useRef<HTMLDialogElement>(null)
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    const dialog = ref.current!
    dialog.showModal()
    return () => {
      dialog.close()
      previous?.focus()
    }
  }, [])
  return (
    <dialog
      ref={ref}
      className="modal"
      onCancel={(event) => {
        event.preventDefault()
        if (!busy) close()
      }}
      aria-labelledby="modal-title"
    >
      <div className="modal-heading">
        <div>
          <span className="eyebrow">EDITORIAL WORKSPACE</span>
          <h2 id="modal-title">{title}</h2>
        </div>
        <button className="icon-button" aria-label="Close dialog" disabled={busy} onClick={close}>
          <X size={20} />
        </button>
      </div>
      {children}
    </dialog>
  )
}
