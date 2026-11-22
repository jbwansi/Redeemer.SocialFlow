import { useState } from 'react'
import { beforeEach, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { VisualLightbox } from './VisualLightbox'
import { workspaceVisualsApi, visualsApi, type Visual } from '../api/visuals'
const candidate = (id: string, selected = false): Visual => ({ id, socialPostId: 'post-a', origin: 1, contentType: 'image/png', width: 800, height: 600, byteLength: 100, sha256: 'hash', createdAt: '2026-11-22T00:00:00Z', validatedAt: null, selected, generation: null })
const images = [candidate('a'), candidate('b', true)]
function Harness() { const [open, setOpen] = useState(false); return <><button onClick={() => setOpen(true)}>Agrandir</button>{open && <VisualLightbox postId="post-a" visuals={images} initialId="a" workspace close={() => setOpen(false)} />}</> }
beforeEach(() => {
  vi.spyOn(workspaceVisualsApi, 'image').mockResolvedValue(new Blob(['image']))
  vi.spyOn(workspaceVisualsApi, 'select')
  vi.spyOn(workspaceVisualsApi, 'generate')
  vi.spyOn(visualsApi, 'image')
  URL.createObjectURL = vi.fn(() => 'blob:authenticated')
  URL.revokeObjectURL = vi.fn()
})
it('loads authenticated images, zooms, pans, navigates and closes without selection', async () => {
  render(<Harness />)
  await userEvent.click(screen.getByRole('button', { name: 'Agrandir' }))
  await screen.findByAltText('Candidat en grand format')
  expect(workspaceVisualsApi.image).toHaveBeenCalledWith('post-a', 'a', expect.any(AbortSignal))
  expect(visualsApi.image).not.toHaveBeenCalled()
  await userEvent.click(screen.getByRole('button', { name: 'Zoomer' }))
  expect(screen.getByLabelText('Zoom')).toHaveTextContent('150 %')
  const region = screen.getByRole('region', { name: /Image agrandie/ })
  fireEvent.keyDown(region, { key: 'ArrowRight' })
  expect(region.scrollLeft).toBe(80)
  await userEvent.click(screen.getByRole('button', { name: 'Suivant' }))
  await screen.findByAltText('Visuel sélectionné en grand format')
  expect(screen.getByLabelText('Zoom')).toHaveTextContent('100 %')
  expect(URL.revokeObjectURL).toHaveBeenCalled()
  fireEvent(screen.getByRole('dialog'), new Event('cancel', { bubbles: true, cancelable: true }))
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Agrandir' })).toHaveFocus()
  expect(workspaceVisualsApi.select).not.toHaveBeenCalled()
  expect(workspaceVisualsApi.generate).not.toHaveBeenCalled()
})
it('shows loading and errors without exposing a public image URL', async () => {
  let fail!: (e: Error) => void
  vi.mocked(workspaceVisualsApi.image).mockImplementationOnce(() => new Promise((_, reject) => { fail = reject }))
  render(<Harness />)
  await userEvent.click(screen.getByRole('button', { name: 'Agrandir' }))
  expect(screen.getByText('Chargement de l’image…')).toHaveAttribute('role', 'status')
  fail(new Error('Unavailable'))
  await screen.findByRole('alert')
  expect(screen.queryByRole('img')).not.toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: 'Close dialog' }))
  expect(screen.getByRole('button', { name: 'Agrandir' })).toHaveFocus()
})
it('aborts obsolete requests when the workspace boundary remounts the viewer', async () => {
  let finish!: (b: Blob) => void
  vi.mocked(workspaceVisualsApi.image).mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
  const view = render(<VisualLightbox key="a" postId="post-a" visuals={images} initialId="a" workspace close={vi.fn()} />)
  const oldSignal = vi.mocked(workspaceVisualsApi.image).mock.calls[0][2]!
  view.rerender(<VisualLightbox key="b" postId="post-b" visuals={[candidate('c')]} initialId="c" workspace close={vi.fn()} />)
  await screen.findByRole('img')
  expect(oldSignal.aborted).toBe(true)
  finish(new Blob(['old']))
  await waitFor(() => expect(URL.createObjectURL).toHaveBeenCalledTimes(1))
})
