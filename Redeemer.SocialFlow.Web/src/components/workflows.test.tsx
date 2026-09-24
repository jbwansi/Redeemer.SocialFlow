import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { ApiError, postsApi, type Status } from '../api/posts'
import { PostEditor } from './PostEditor'
import { PostDetails } from './PostDetails'
import { post } from '../test/fixtures'

describe('post editor', () => {
  it('creates a draft and leaves domain validation to the API', async () => {
    const create = vi.spyOn(postsApi, 'create').mockResolvedValue(post())
    const saved = vi.fn()
    render(<PostEditor close={vi.fn()} saved={saved} />)
    await userEvent.type(screen.getByLabelText('Post title'), 'A shared vision')
    await userEvent.selectOptions(screen.getByLabelText('Platform'), '1')
    await userEvent.click(screen.getByRole('button', { name: 'Create draft' }))
    expect(create).toHaveBeenCalledWith({ title: 'A shared vision', content: '', platform: 1 })
    expect(saved).toHaveBeenCalledWith(post())
  })
  it('retains input and renders server validation detail after a failure', async () => {
    vi.spyOn(postsApi, 'create').mockRejectedValue(
      new ApiError({
        title: 'Validation failed',
        detail: 'Le titre est obligatoire.',
        traceId: 'trace-123',
        errors: { title: ['Title required'] },
      }),
    )
    render(<PostEditor close={vi.fn()} saved={vi.fn()} />)
    await userEvent.type(screen.getByLabelText('Content'), 'Keep my draft')
    await userEvent.click(screen.getByRole('button', { name: 'Create draft' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Le titre est obligatoire.')
    expect(screen.getByRole('alert')).toHaveTextContent('trace-123')
    expect(screen.getByLabelText('Content')).toHaveValue('Keep my draft')
  })
  it('edits all fields and clears optional values with null', async () => {
    const update = vi.spyOn(postsApi, 'update').mockResolvedValue(post())
    render(
      <PostEditor
        post={post({ callToAction: 'Old CTA', visualBrief: 'Old brief', visualUrl: 'old-url' })}
        close={vi.fn()}
        saved={vi.fn()}
      />,
    )
    expect(screen.getByLabelText('Platform')).toBeDisabled()
    await userEvent.clear(screen.getByLabelText(/Call to action/))
    await userEvent.clear(screen.getByLabelText(/Visual brief/))
    await userEvent.clear(screen.getByLabelText(/Visual URL/))
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    expect(update).toHaveBeenCalledWith(
      'post-1',
      expect.objectContaining({ callToAction: null, visualBrief: null, visualUrl: null }),
    )
  })
})

describe('workflow actions', () => {
  it.each([
    [1, 'Submit for review', 'submit-for-review'],
    [6, 'Submit for review', 'submit-for-review'],
    [2, 'Approve', 'approve'],
    [2, 'Reject', 'reject'],
    [4, 'Cancel schedule', 'cancel'],
  ] as const)('dispatches %s / %s', async (status, label, action) => {
    const transition = vi.spyOn(postsApi, 'transition').mockResolvedValue(post())
    const changed = vi.fn()
    render(
      <PostDetails
        post={post({ status })}
        close={vi.fn()}
        edit={vi.fn()}
        changed={changed}
        removed={vi.fn()}
      />,
    )
    await userEvent.click(screen.getByRole('button', { name: label }))
    expect(transition).toHaveBeenCalledWith('post-1', action, undefined)
    expect(changed).toHaveBeenCalledWith(post())
  })
  it('converts local schedule input to an ISO instant', async () => {
    const transition = vi.spyOn(postsApi, 'transition').mockResolvedValue(post({ status: 4 }))
    render(
      <PostDetails
        post={post({ status: 3 })}
        close={vi.fn()}
        edit={vi.fn()}
        changed={vi.fn()}
        removed={vi.fn()}
      />,
    )
    expect(screen.getByRole('button', { name: 'Schedule' })).toBeDisabled()
    await userEvent.type(screen.getByLabelText('Schedule date and time'), '2027-03-14T12:30')
    await userEvent.click(screen.getByRole('button', { name: 'Schedule' }))
    expect(transition).toHaveBeenCalledWith(
      'post-1',
      'schedule',
      new Date('2027-03-14T12:30').toISOString(),
    )
  })
  it('requires confirmation before deletion', async () => {
    const remove = vi.spyOn(postsApi, 'remove').mockResolvedValue()
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
    expect(remove).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Confirm deletion' }))
    expect(remove).toHaveBeenCalledWith('post-1')
    expect(removed).toHaveBeenCalled()
  })
  it.each([5, 7, 8] as Status[])('has read-only controls for status %s', (status) => {
    render(
      <PostDetails
        post={post({ status })}
        close={vi.fn()}
        edit={vi.fn()}
        changed={vi.fn()}
        removed={vi.fn()}
      />,
    )
    expect(screen.queryByRole('button', { name: 'Edit post' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
    expect(screen.getAllByRole('button')).toHaveLength(1)
  })
  it('shows server rejection for a stale action without changing the post', async () => {
    vi.spyOn(postsApi, 'transition').mockRejectedValue(
      new ApiError({ title: 'Domain validation failed', detail: 'The post cannot be approved.' }),
    )
    const changed = vi.fn()
    render(
      <PostDetails
        post={post({ status: 2 })}
        close={vi.fn()}
        edit={vi.fn()}
        changed={changed}
        removed={vi.fn()}
      />,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Approve' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('The post cannot be approved.')
    expect(changed).not.toHaveBeenCalled()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Approve' })).toBeEnabled())
  })
})
