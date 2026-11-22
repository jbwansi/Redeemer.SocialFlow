import { expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import * as api from '../api/posts'
import { WorkspaceTerminology } from './WorkspaceTerminology'
import { PostTerminologyCheck } from './PostTerminologyCheck'
import { post } from '../test/fixtures'

const term = { id: 'term-a', workspaceId: 'workspace-a', revision: 1, canonical: 'LEGO® SERIOUS PLAY®', aliases: ['lego serious play'],
  category: 2, language: 'fr', usageContext: '', note: '', source: 'https://reference.test', status: 1, verifiedAt: null }

it('creates a proposed term without automatically validating it', async () => {
  const request = vi.spyOn(api, 'apiRequest').mockResolvedValueOnce([]).mockResolvedValueOnce(term).mockResolvedValueOnce([term])
  render(<WorkspaceTerminology canManage />)
  await waitFor(() => expect(screen.getByLabelText('Appellation canonique exacte')).toBeEnabled())
  await userEvent.type(screen.getByLabelText('Appellation canonique exacte'), term.canonical)
  await userEvent.type(screen.getByLabelText(/Variantes reconnues/), 'lego serious play')
  await userEvent.type(screen.getByLabelText('Source de référence'), term.source)
  await userEvent.click(screen.getByRole('button', { name: 'Enregistrer comme proposé' }))
  await screen.findByText(/Proposé · version 1/)
  expect(request.mock.calls.map(x => x[0])).toEqual(['/workspace-terminology', '/workspace-terminology', '/workspace-terminology'])
  expect(JSON.parse(request.mock.calls[1][1]!.body as string)).toMatchObject({ canonical: term.canonical, aliases: term.aliases, revision: 0 })
})
it('validates only the saved version with a source and requires a new save after edits', async () => {
  const validated = { ...term, status: 2, revision: 2, verifiedAt: '2026-11-22T00:00:00Z' }
  const request = vi.spyOn(api, 'apiRequest').mockResolvedValueOnce([term]).mockResolvedValueOnce(validated).mockResolvedValueOnce([validated])
  render(<WorkspaceTerminology canManage />)
  await screen.findByRole('option', { name: /LEGO/ })
  await userEvent.selectOptions(screen.getByLabelText('Terme enregistré'), term.id)
  const validate = screen.getByRole('button', { name: 'Valider la graphie et la source enregistrées' })
  await userEvent.type(screen.getByLabelText('Source de référence'), '/changed')
  expect(validate).toBeDisabled()
  await userEvent.selectOptions(screen.getByLabelText('Terme enregistré'), '')
  await userEvent.selectOptions(screen.getByLabelText('Terme enregistré'), term.id)
  await userEvent.click(screen.getByRole('button', { name: 'Valider la graphie et la source enregistrées' }))
  await screen.findByText(/Validé dans cet espace · version 2/)
  expect(request.mock.calls[1][0]).toBe('/workspace-terminology/term-a/validate')
  expect(JSON.parse(request.mock.calls[1][1]!.body as string)).toEqual({ revision: 1 })
})
it('allows read-only browsing without exposing write actions', async () => {
  vi.spyOn(api, 'apiRequest').mockResolvedValue([term])
  render(<WorkspaceTerminology canManage={false} />)
  await screen.findByRole('option', { name: /LEGO/ })
  await userEvent.selectOptions(screen.getByLabelText('Terme enregistré'), term.id)
  expect(screen.getByLabelText('Appellation canonique exacte')).toBeDisabled()
  expect(screen.queryByRole('button', { name: /Valider/ })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /Enregistrer/ })).not.toBeInTheDocument()
})
it('checks saved content on explicit request without writing or generating', async () => {
  const request = vi.spyOn(api, 'apiRequest').mockResolvedValue({ warnings: ['Terminologie ambiguë : revue humaine requise.'] })
  render(<PostTerminologyCheck post={post()} disabled={false} />)
  expect(request).not.toHaveBeenCalled()
  await userEvent.click(screen.getByRole('button', { name: /Vérifier la terminologie/ }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Terminologie ambiguë')
  expect(request).toHaveBeenCalledTimes(1)
  expect(request.mock.calls[0][0]).toBe('/posts/post-1/terminology-check')
  expect(request.mock.calls[0][1]!.method).toBeUndefined()
})
