import { render, screen, within, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { postsApi, ApiError } from '../api/posts'
import { post } from '../test/fixtures'
import { Dashboard } from './Dashboard'
import { Calendar } from './Calendar'
import { Posts } from './Posts'
import { dateBoundary, dayKey } from '../lib/dates'
import App from '../App'

describe('workspace screens', () => {
  it('shows real workflow counts and only future scheduled posts in upcoming', () => {
    const posts = [
      post(),
      post({ id: '2', status: 2 }),
      post({
        id: '3',
        status: 4,
        title: 'Next story',
        scheduledAt: new Date(Date.now() + 86400000).toISOString(),
      }),
      post({ id: '4', status: 4, title: 'Past schedule', scheduledAt: '2020-01-01T12:00:00Z' }),
    ]
    render(<Dashboard posts={posts} open={vi.fn()} navigate={vi.fn()} create={vi.fn()} />)
    const overview = screen.getByRole('region', { name: 'Post overview' })
    expect(within(overview).getByRole('button', { name: /All posts/ })).toHaveTextContent('4')
    expect(within(overview).getByRole('button', { name: /Ready for review/ })).toHaveTextContent(
      '1',
    )
    const upcoming = screen.getByRole('heading', { name: 'Coming up next' }).closest('section')!
    expect(within(upcoming).getByText('Next story')).toBeInTheDocument()
    expect(within(upcoming).queryByText('Past schedule')).not.toBeInTheDocument()
  })
  it('places calendar posts on the local scheduled day and supports month navigation', async () => {
    const date = new Date(new Date().getFullYear(), new Date().getMonth(), 15, 12, 30)
    const open = vi.fn()
    const planned = post({ status: 4, title: 'Calendar story', scheduledAt: date.toISOString() })
    render(
      <Calendar posts={[planned, post({ id: 'draft', title: 'Unscheduled draft' })]} open={open} />,
    )
    const button = screen.getByRole('button', { name: /Calendar story/ })
    expect(button.parentElement?.querySelector('time')).toHaveAttribute('datetime', dayKey(date))
    expect(screen.queryByText('Unscheduled draft')).not.toBeInTheDocument()
    await userEvent.click(button)
    expect(open).toHaveBeenCalledWith(planned)
    await userEvent.click(screen.getByRole('button', { name: 'Next month' }))
    expect(screen.getByText('A fresh page for this month')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Today' }))
    expect(screen.getByRole('button', { name: /Calendar story/ })).toBeInTheDocument()
  })
  it('sends filters to the API and supports title/content search', async () => {
    const list = vi.spyOn(postsApi, 'list').mockResolvedValue([post()])
    render(<Posts open={vi.fn()} create={vi.fn()} revision={0} />)
    await screen.findByRole('button', { name: 'A shared vision' })
    await userEvent.selectOptions(screen.getByLabelText('Platform'), '2')
    await userEvent.selectOptions(screen.getByLabelText('Status'), '1')
    await userEvent.type(screen.getByLabelText('Created from'), '2026-01-01')
    await userEvent.type(screen.getByLabelText('Created to'), '2026-01-31')
    await waitFor(() =>
      expect(list).toHaveBeenLastCalledWith(
        {
          platform: 2,
          status: 1,
          createdFrom: dateBoundary('2026-01-01'),
          createdTo: dateBoundary('2026-01-31', true),
        },
        expect.any(AbortSignal),
      ),
    )
    await userEvent.type(screen.getByLabelText('Search posts'), 'no match')
    expect(screen.getByText('No posts match these filters')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Reset filters' }))
    expect(await screen.findByRole('button', { name: 'A shared vision' })).toBeInTheDocument()
  })
  it('shows loading, failed and recovered list states', async () => {
    const list = vi
      .spyOn(postsApi, 'list')
      .mockRejectedValueOnce(new ApiError({ title: 'Connection unavailable' }))
      .mockResolvedValueOnce([])
    render(<Posts open={vi.fn()} create={vi.fn()} revision={0} />)
    expect(screen.getByRole('status')).toHaveTextContent('Loading posts')
    expect(await screen.findByRole('alert')).toHaveTextContent('Connection unavailable')
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByText('Your next story starts here')).toBeInTheDocument()
    expect(list).toHaveBeenCalledTimes(2)
  })
  it('navigates the app and refreshes after creating a post', async () => {
    window.history.replaceState(null, '', '#dashboard')
    const list = vi.spyOn(postsApi, 'list').mockResolvedValue([])
    vi.spyOn(postsApi, 'create').mockResolvedValue(post())
    render(<App />)
    await screen.findByText('Your story starts here')
    await userEvent.click(screen.getByRole('link', { name: 'Posts' }))
    expect(await screen.findByText('Your next story starts here')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Create post' }))
    await userEvent.type(screen.getByLabelText('Post title'), 'A shared vision')
    const before = list.mock.calls.length
    await userEvent.click(screen.getByRole('button', { name: 'Create draft' }))
    expect(await screen.findByRole('heading', { name: 'A shared vision' })).toBeInTheDocument()
    await waitFor(() => expect(list.mock.calls.length).toBeGreaterThan(before))
    expect(screen.getByRole('button', { name: 'Submit for review' })).toBeInTheDocument()
  })
})
