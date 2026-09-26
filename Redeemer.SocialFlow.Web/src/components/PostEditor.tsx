import { useState, type FormEvent } from 'react'
import { postsApi, platforms, type Post, type Platform } from '../api/posts'
import { ErrorNotice, Modal } from './shared'
import { AiReviewNotice } from './GenerateDraft'

export function PostEditor({
  post,
  close,
  saved,
  warnings,
}: {
  post?: Post
  close: () => void
  saved: (post: Post) => void
  warnings?: string[]
}) {
  const [title, setTitle] = useState(post?.title || '')
  const [content, setContent] = useState(post?.content || '')
  const [platform, setPlatform] = useState<Platform>(post?.platform || 2)
  const [cta, setCta] = useState(post?.callToAction || '')
  const [brief, setBrief] = useState(post?.visualBrief || '')
  const [url, setUrl] = useState(post?.visualUrl || '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()
  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(undefined)
    try {
      const result = post
        ? await postsApi.update(post.id, {
            title,
            content,
            callToAction: cta || null,
            visualBrief: brief || null,
            visualUrl: url || null,
          })
        : await postsApi.create({ title, content, platform })
      saved(result)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }
  return (
    <Modal title={post ? 'Edit post' : 'Create a post'} close={close} busy={busy}>
      <form onSubmit={submit} className="editor-form">
        {warnings && <AiReviewNotice warnings={warnings} />}
        {error != null && <ErrorNotice error={error} />}
        <fieldset disabled={busy}>
          <label>
            Post title
            <input
              autoFocus
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Give your next idea a title"
            />
          </label>
          <label>
            Platform
            <select
              value={platform}
              disabled={!!post}
              onChange={(e) => setPlatform(Number(e.target.value) as Platform)}
            >
              {Object.entries(platforms).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>
          <label>
            Content
            <textarea
              rows={6}
              value={content}
              onChange={(e) => setContent(e.target.value)}
              placeholder="What would you like to share?"
            />
          </label>
          {post ? (
            <>
              <label>
                Call to action <span>(optional)</span>
                <input value={cta} onChange={(e) => setCta(e.target.value)} />
              </label>
              <label>
                Visual brief <span>(optional)</span>
                <textarea rows={3} value={brief} onChange={(e) => setBrief(e.target.value)} />
              </label>
              <label>
                Visual URL <span>(optional)</span>
                <input value={url} onChange={(e) => setUrl(e.target.value)} />
              </label>
            </>
          ) : (
            <p className="field-note">
              Start with a draft. You can add a call to action and visual details when editing.
            </p>
          )}
        </fieldset>
        <footer className="modal-footer">
          <button type="button" className="button" disabled={busy} onClick={close}>
            Cancel
          </button>
          <button className="button primary" disabled={busy}>
            {busy ? 'Saving…' : post ? 'Save changes' : 'Create draft'}
          </button>
        </footer>
      </form>
    </Modal>
  )
}
