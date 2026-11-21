import { beforeEach, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { workspaceVisualsApi } from '../api/visuals'
import { ApiError } from '../api/posts'
import { post } from '../test/fixtures'
import { PostVisuals } from './PostVisuals'

beforeEach(() => {
  vi.spyOn(workspaceVisualsApi, 'list').mockResolvedValue([])
  vi.spyOn(workspaceVisualsApi, 'generate')
  vi.spyOn(workspaceVisualsApi, 'upload')
})

it.each([false, true])('reports disabled module in either view (readOnly=%s)', async readOnly => {
  vi.spyOn(workspaceVisualsApi, 'runtime').mockResolvedValue({ enabled: false })
  render(<PostVisuals post={post()} workspace readOnly={readOnly} />)
  expect(await screen.findByText('Les visuels sont désactivés pour cet environnement.')).toBeInTheDocument()
  expect(workspaceVisualsApi.list).not.toHaveBeenCalled()
  expect(workspaceVisualsApi.generate).not.toHaveBeenCalled()
  expect(workspaceVisualsApi.upload).not.toHaveBeenCalled()
  expect(screen.queryByRole('button', { name: 'Générer un visuel' })).not.toBeInTheDocument()
})

it.each([true, false])('separates generation configuration from upload (configured=%s)', async configured => {
  vi.spyOn(workspaceVisualsApi, 'runtime').mockResolvedValue({ enabled: true, uploadEnabled: true,
    generationEnabled: configured, reason: configured ? null : 'ImageGenerationNotConfigured' })
  render(<PostVisuals post={post()} workspace />)
  await screen.findByText('Aucun visuel pour cette publication.')
  expect(screen.getByRole('button', { name: 'Générer un visuel' })).toHaveProperty('disabled', !configured)
  expect(screen.getByLabelText('Importer une image')).toBeEnabled()
  expect(workspaceVisualsApi.generate).not.toHaveBeenCalled()
})

it.each(['status', 'list'])('locks mutations on %s loading failure', async stage => {
  const failure = new ApiError({ status: 403, title: 'Accès non autorisé' })
  const runtime = vi.spyOn(workspaceVisualsApi, 'runtime')
  if (stage === 'status') runtime.mockRejectedValue(failure)
  else {
    runtime.mockResolvedValue({ enabled: true, uploadEnabled: true, generationEnabled: true })
    vi.mocked(workspaceVisualsApi.list).mockRejectedValue(failure)
  }
  render(<PostVisuals post={post()} workspace />)
  await screen.findByText('Accès aux visuels refusé pour cette opération.')
  expect(screen.getByRole('button', { name: 'Générer un visuel' })).toBeDisabled()
  expect(screen.getByLabelText('Importer une image')).toBeDisabled()
})
