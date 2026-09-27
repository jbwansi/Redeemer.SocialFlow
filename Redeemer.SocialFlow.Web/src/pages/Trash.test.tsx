import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ApiError, postsApi, type TrashedPost } from '../api/posts'
import { post } from '../test/fixtures'
import { dateLabel, timeLabel } from '../lib/dates'
import { Trash } from './Trash'
import { PostDetails } from '../components/PostDetails'
import App from '../App'

const deleted: TrashedPost = { ...post(), deletedAt: '2026-09-27T10:35:00Z' }

describe('Trash', () => {
  it('shows loading then read-only rows with local deletion dates', async () => {
    let resolve!: (posts: TrashedPost[]) => void
    const trash = vi.spyOn(postsApi, 'trash').mockReturnValue(
      new Promise((done) => {
        resolve = done
      }),
    )
    render(<Trash revision={0} />)
    expect(screen.getByRole('status')).toHaveTextContent('Chargement')
    await act(async () =>
      resolve([deleted, { ...deleted, id: 'other', title: 'Other post', status: 6 }]),
    )
    expect(screen.getByText(deleted.title)).toBeInTheDocument()
    expect(screen.getByText('Other post')).toBeInTheDocument()
    expect(
      screen.getAllByText(`${dateLabel(deleted.deletedAt)} · ${timeLabel(deleted.deletedAt)}`),
    ).toHaveLength(2)
    expect(screen.getByText('Rejected')).toBeInTheDocument()
    expect(screen.getByText(/ne sont plus actives/)).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(trash).toHaveBeenCalledWith(expect.any(AbortSignal))
  })
  it('shows an empty state', async () => {
    vi.spyOn(postsApi, 'trash').mockResolvedValue([])
    render(<Trash revision={0} />)
    expect(await screen.findByText('La corbeille est vide')).toBeInTheDocument()
  })
  it('renders ProblemDetails and retries', async () => {
    vi.spyOn(postsApi, 'trash')
      .mockRejectedValueOnce(
        new ApiError({ title: 'Unavailable', detail: 'Try later', traceId: 'trash-trace' }),
      )
      .mockResolvedValueOnce([])
    render(<Trash revision={0} />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Try later')
    expect(screen.getByRole('alert')).toHaveTextContent('trash-trace')
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByText('La corbeille est vide')).toBeInTheDocument()
  })
  it('confirms deletion, refreshes active posts and navigates to Corbeille', async () => {
    window.history.replaceState(null, '', '#posts')
    let removed = false
    vi.spyOn(postsApi, 'list').mockImplementation(async () => (removed ? [] : [post()]))
    vi.spyOn(postsApi, 'trash').mockResolvedValue([deleted])
    const remove = vi.spyOn(postsApi, 'remove').mockImplementation(async () => {
      removed = true
    })
    render(<App />)
    await userEvent.click(await screen.findByRole('button', { name: post().title }))
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    expect(screen.getByRole('alert')).toHaveTextContent('history is preserved')
    await userEvent.click(screen.getByRole('button', { name: 'Keep post' }))
    expect(remove).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirm deletion' }))
    expect(remove).toHaveBeenCalledExactlyOnceWith(post().id)
    expect(await screen.findByText('Your next story starts here')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('link', { name: 'Corbeille' }))
    expect(await screen.findByText(deleted.title)).toBeInTheDocument()
    expect(window.location.hash).toBe('#trash')
  })
  it('blocks duplicate deletion while pending and displays server rejection', async () => {
    let reject!: (reason: unknown) => void
    const remove = vi.spyOn(postsApi, 'remove').mockReturnValue(
      new Promise((_, fail) => {
        reject = fail
      }),
    )
    const removed = vi.fn()
    render(
      <PostDetails
        post={post()}
        close={vi.fn()}
        edit={vi.fn()}
        changed={vi.fn()}
        removed={removed}
      />,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirm deletion' }))
    const pending = screen.getByRole('button', { name: 'Deleting…' })
    expect(pending).toBeDisabled()
    await userEvent.click(pending)
    expect(screen.getByRole('button', { name: 'Keep post' })).toBeDisabled()
    expect(remove).toHaveBeenCalledTimes(1)
    await act(async () =>
      reject(
        new ApiError({ title: 'Cannot delete', detail: 'Status changed', traceId: 'delete-trace' }),
      ),
    )
    expect(within(screen.getByRole('dialog')).getByText('Status changed')).toBeInTheDocument()
    expect(screen.getByText('Reference: delete-trace')).toBeInTheDocument()
    expect(removed).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Confirm deletion' })).toBeEnabled()
  })
})
