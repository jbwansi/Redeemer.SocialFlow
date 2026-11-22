import { expect, it, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { VisualIdentities } from './VisualIdentities'
import { workspaceVisualsApi, visualsApi } from '../api/visuals'
import * as api from '../api/posts'

function renderExpanded(ui: React.ReactElement) {
  const view = render(ui)
  for (const name of ['Identités visuelles', 'Terminologie professionnelle', 'Angles éditoriaux'])
    fireEvent.click(screen.getByRole('button', { name: new RegExp('^' + name) }))
  return view
}

vi.mock('../components/VisualIdentityLibrary', () => ({ VisualIdentityLibrary: () => <p>Bibliothèque graphique</p> }))
vi.mock('../components/WorkspaceEditorialProfile', () => ({ WorkspaceEditorialProfile: () => <p>Profil permanent</p> }))
vi.mock('../components/WorkspaceTerminology', () => ({ WorkspaceTerminology: () => <p>Terminologie</p> }))

it('shows workspace angles even while visual status is pending', async () => {
  vi.spyOn(workspaceVisualsApi, 'runtime').mockImplementation(() => new Promise(() => {}))
  const request = vi.spyOn(api, 'apiRequest').mockResolvedValue([])
  renderExpanded(<VisualIdentities workspace canManage />)
  expect(screen.getByRole('region', { name: 'Angles éditoriaux' })).toBeInTheDocument()
  await screen.findByRole('button', { name: /Importer le catalogue initial/ })
  expect(request).toHaveBeenCalledWith('/workspace-editorial-angles', expect.objectContaining({ signal: expect.any(AbortSignal) }))
  expect(screen.queryByText('Bibliothèque graphique')).not.toBeInTheDocument()
})
it.each([403, 500])('keeps angles visible after visual status fails with %s', async status => {
  vi.spyOn(workspaceVisualsApi, 'runtime').mockRejectedValue(new api.ApiError({ status, title: 'Statut visuel indisponible' }))
  vi.spyOn(api, 'apiRequest').mockResolvedValue([])
  renderExpanded(<VisualIdentities workspace canManage />)
  await screen.findByText('Statut visuel indisponible')
  expect(screen.getByRole('region', { name: 'Angles éditoriaux' })).toBeInTheDocument()
  await screen.findByRole('button', { name: /Importer le catalogue initial/ })
  expect(screen.queryByText('Bibliothèque graphique')).not.toBeInTheDocument()
})
it.each([true, false])('shows an empty catalog with visuals enabled=%s', async enabled => {
  vi.spyOn(workspaceVisualsApi, 'runtime').mockResolvedValue({ enabled })
  vi.spyOn(api, 'apiRequest').mockResolvedValue([])
  renderExpanded(<VisualIdentities workspace canManage />)
  await screen.findByRole('button', { name: /Importer le catalogue initial/ })
  expect(screen.getByRole('region', { name: 'Angles éditoriaux' })).toBeInTheDocument()
  if (enabled) expect(screen.getByText('Bibliothèque graphique')).toBeInTheDocument()
  else expect(screen.getByText(/Gestion des visuels désactivée/)).toBeInTheDocument()
})
it('shows a catalog error inside the section, including an unavailable database schema', async () => {
  vi.spyOn(workspaceVisualsApi, 'runtime').mockResolvedValue({ enabled: false })
  vi.spyOn(api, 'apiRequest').mockRejectedValue(new api.ApiError({ status: 500, title: 'Visual operation failed' }))
  renderExpanded(<VisualIdentities workspace canManage />)
  await screen.findByText('Visual operation failed')
  expect(screen.getByRole('region', { name: 'Angles éditoriaux' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /Importer/ })).not.toBeInTheDocument()
})
it('keeps the section visible to read-only members without write actions', async () => {
  vi.spyOn(workspaceVisualsApi, 'runtime').mockResolvedValue({ enabled: true })
  vi.spyOn(api, 'apiRequest').mockResolvedValue([])
  renderExpanded(<VisualIdentities workspace canManage={false} />)
  await screen.findByText('Bibliothèque graphique')
  expect(screen.getByRole('region', { name: 'Angles éditoriaux' })).toBeInTheDocument()
  expect(screen.getByLabelText('Nom de l’angle')).toBeDisabled()
  expect(screen.queryByRole('button', { name: /Enregistrer l’angle|Importer/ })).not.toBeInTheDocument()
})
it('does not expose workspace controls or endpoints in historical local administration', async () => {
  vi.spyOn(visualsApi, 'runtime').mockResolvedValue({ enabled: true })
  const request = vi.spyOn(api, 'apiRequest')
  render(<VisualIdentities />)
  await screen.findByText('Bibliothèque graphique')
  expect(screen.queryByRole('region', { name: 'Angles éditoriaux' })).not.toBeInTheDocument()
  expect(request).not.toHaveBeenCalled()
})

it('offers four keyboard-accessible panels and preserves unsaved angle fields when collapsed', async () => {
  vi.spyOn(workspaceVisualsApi, 'runtime').mockResolvedValue({ enabled: false })
  const request = vi.spyOn(api, 'apiRequest').mockResolvedValue([])
  render(<VisualIdentities workspace canManage />)
  const editorial = screen.getByRole('button', { name: 'Identité éditoriale' })
  expect(editorial).toHaveAttribute('aria-expanded', 'false')
  for (const name of ['Identités visuelles', 'Terminologie professionnelle', 'Angles éditoriaux'])
    expect(screen.getByRole('button', { name: new RegExp('^' + name) })).toHaveAttribute('aria-expanded', 'false')
  const angles = screen.getByRole('button', { name: 'Angles éditoriaux' })
  angles.focus()
  await userEvent.keyboard('{Enter}')
  await screen.findByRole('button', { name: /Importer le catalogue/ })
  await userEvent.type(screen.getByLabelText('Nom de l’angle'), 'Saisie non enregistrée')
  await userEvent.click(angles)
  expect(document.getElementById(angles.getAttribute('aria-controls')!)).toHaveAttribute('hidden')
  expect(screen.queryByRole('textbox', { name: 'Nom de l’angle' })).not.toBeInTheDocument()
  angles.focus()
  await userEvent.keyboard(' ')
  expect(screen.getByLabelText('Nom de l’angle')).toHaveValue('Saisie non enregistrée')
  expect(request).toHaveBeenCalledTimes(1)
  expect(request.mock.calls.every(([, options]) => !options?.method)).toBe(true)
})
it('resets the workspace forms on the existing workspace key boundary', async () => {
  vi.spyOn(workspaceVisualsApi, 'runtime').mockResolvedValue({ enabled: false })
  vi.spyOn(api, 'apiRequest').mockResolvedValue([])
  const view = render(<VisualIdentities key="a" workspace canManage />)
  await userEvent.click(screen.getByRole('button', { name: 'Angles éditoriaux' }))
  await screen.findByRole('button', { name: /Importer le catalogue/ })
  await userEvent.type(screen.getByLabelText('Nom de l’angle'), 'Espace A uniquement')
  view.rerender(<VisualIdentities key="b" workspace canManage={false} />)
  expect(screen.getByRole('button', { name: 'Angles éditoriaux' })).toHaveAttribute('aria-expanded', 'false')
  await userEvent.click(screen.getByRole('button', { name: 'Angles éditoriaux' }))
  expect(screen.getByLabelText('Nom de l’angle')).toHaveValue('')
  expect(screen.getByLabelText('Nom de l’angle')).toBeDisabled()
})
