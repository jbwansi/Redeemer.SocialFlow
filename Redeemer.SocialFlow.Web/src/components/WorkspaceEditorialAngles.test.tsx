import { expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import * as api from '../api/posts'
import { generatedDraft } from '../test/fixtures'
import { WorkspaceEditorialAngles } from './WorkspaceEditorialAngles'
import { GenerateDraft } from './GenerateDraft'
const angle = { id: 'angle-a', workspaceId: 'a', revision: 1, name: 'Leadership', description: 'Prendre du recul', objective: 'Explain', instructions: 'Be clear', callToAction: 'Ask', active: true, displayOrder: 0 }
it('imports configured defaults only after an explicit owner action', async () => {
  const request = vi.spyOn(api, 'apiRequest').mockResolvedValueOnce([]).mockResolvedValueOnce(undefined).mockResolvedValueOnce([angle])
  render(<WorkspaceEditorialAngles canManage />)
  const initialize = await screen.findByRole('button', { name: /Importer le catalogue/ })
  expect(request).toHaveBeenCalledTimes(1)
  await userEvent.click(initialize)
  await screen.findByRole('option', { name: /Leadership/ })
  expect(request.mock.calls[1][0]).toBe('/workspace-editorial-angles/initialize')
  expect(request.mock.calls[1][1]!.method).toBe('POST')
})
it('allows read-only browsing and hides mutation controls', async () => {
  vi.spyOn(api, 'apiRequest').mockResolvedValue([angle])
  render(<WorkspaceEditorialAngles canManage={false} />)
  await screen.findByRole('option', { name: /Leadership/ })
  await userEvent.selectOptions(screen.getByLabelText('Angle enregistré'), angle.id)
  expect(screen.getByLabelText('Nom de l’angle')).toBeDisabled()
  expect(screen.queryByRole('button', { name: /Enregistrer/ })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /Importer/ })).not.toBeInTheDocument()
})
it('saves activation and ordering using the saved revision', async () => {
  const request = vi.spyOn(api, 'apiRequest').mockResolvedValueOnce([angle]).mockResolvedValueOnce(angle).mockResolvedValueOnce([{ ...angle, active: false, revision: 2 }])
  render(<WorkspaceEditorialAngles canManage />)
  await screen.findByRole('option', { name: /Leadership/ })
  await userEvent.selectOptions(screen.getByLabelText('Angle enregistré'), angle.id)
  await userEvent.click(screen.getByLabelText('Angle actif'))
  await userEvent.click(screen.getByRole('button', { name: 'Enregistrer l’angle' }))
  await screen.findByRole('option', { name: /Inactif · v2/ })
  expect(JSON.parse(request.mock.calls[1][1]!.body as string)).toMatchObject({ revision: 1, active: false, displayOrder: 0 })
})
it.each(['', 'angle-a'])('generates with optional ID %s, never browser instructions', async id => {
  vi.spyOn(api, 'apiRequest').mockImplementation(async path => path.startsWith('/workspace-editorial-angles') ? [angle] : { workspaceId: 'a', profile: null })
  const generate = vi.spyOn(api.postsApi, 'generateDraft').mockResolvedValue(generatedDraft())
  render(<GenerateDraft workspace saved={vi.fn()} close={vi.fn()} />)
  await screen.findByRole('option', { name: 'Leadership' })
  expect(screen.getByLabelText('Angle éditorial (facultatif)')).toHaveValue('')
  await userEvent.selectOptions(screen.getByLabelText('Angle éditorial (facultatif)'), id)
  if (id) expect(screen.getByText(angle.description)).toBeInTheDocument()
  await userEvent.selectOptions(screen.getByLabelText('Mode de génération'), '1')
  await userEvent.type(screen.getByLabelText('Sujet'), 'Subject')
  await userEvent.type(screen.getByLabelText('Objectif'), 'Goal')
  await userEvent.type(screen.getByLabelText('Public cible'), 'Readers')
  await userEvent.click(screen.getByRole('button', { name: 'Générer le brouillon' }))
  await waitFor(() => expect(generate).toHaveBeenCalledTimes(1))
  expect(generate.mock.calls[0][0].editorialAngleId).toBe(id || undefined)
  expect(generate.mock.calls[0][0]).not.toHaveProperty('editorialAngle')
})
it('keeps generation without an angle available when the optional catalog fails', async () => {
  vi.spyOn(api, 'apiRequest').mockRejectedValue(new Error('Unavailable'))
  render(<GenerateDraft workspace saved={vi.fn()} close={vi.fn()} />)
  await screen.findByText(/Angles indisponibles/)
  expect(screen.getByLabelText('Angle éditorial (facultatif)')).toHaveValue('')
})
