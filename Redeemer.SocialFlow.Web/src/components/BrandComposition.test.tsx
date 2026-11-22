import { expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { workspaceVisualsApi, type Visual } from '../api/visuals'
import { workspaceIdentitiesApi } from '../api/visualIdentities'
import { PostVisuals } from './PostVisuals'
import { post } from '../test/fixtures'
it('creates and previews a separate composition without generating or selecting', async () => {
  const source: Visual = { id: 'source', socialPostId: 'post-1', origin: 1, contentType: 'image/png', width: 600, height: 400, byteLength: 100, sha256: 'hash', createdAt: '2026-11-22T00:00:00Z', validatedAt: null, selected: false, generation: null }
  let items = [source]
  vi.spyOn(workspaceVisualsApi, 'runtime').mockResolvedValue({ enabled: true, generationEnabled: true, uploadEnabled: true })
  vi.spyOn(workspaceVisualsApi, 'list').mockImplementation(async () => items)
  vi.spyOn(workspaceVisualsApi, 'image').mockResolvedValue(new Blob(['image']))
  const select = vi.spyOn(workspaceVisualsApi, 'select')
  const generate = vi.spyOn(workspaceVisualsApi, 'generate')
  const compose = vi.spyOn(workspaceVisualsApi, 'compose').mockImplementation(async () => {
    const result = { ...source, id: 'composed', height: 500, generation: { composition: { sourceVisualId: 'source' } } }; items = [result, source]; return result
  })
  vi.spyOn(workspaceIdentitiesApi, 'list').mockResolvedValue([{ id: 'approved', workspaceId: 'workspace-a', identityId: 'Brand', name: 'Brand', style: 'Flat', colors: ['#FFFFFF'], revision: 4, isActive: true, isDefault: true, approvalId: 'proof', logoId: 'logo' }])
  URL.createObjectURL = vi.fn(() => 'blob:private'); URL.revokeObjectURL = vi.fn()
  render(<PostVisuals post={post()} workspace />)
  await screen.findByRole('button', { name: 'Composer un aperçu de marque' })
  expect(screen.getByRole('button', { name: 'Composer un aperçu de marque' })).toBeDisabled()
  await waitFor(() => expect(screen.getByRole('combobox', { name: 'Présentation de marque' })).toBeEnabled())
  expect(screen.getByLabelText('Disposition de marque')).toHaveValue('Compact')
  await userEvent.selectOptions(screen.getByLabelText('Disposition de marque'), 'Classic')
  expect(screen.getByRole('img', { name: 'Schéma de la disposition classique' })).toBeInTheDocument()
  expect(compose).not.toHaveBeenCalled(); expect(generate).not.toHaveBeenCalled()
  expect(screen.getByRole('option', { name: 'Sans bandeau — zone sûre non garantie' })).toBeDisabled()
  await userEvent.selectOptions(screen.getByLabelText('Disposition de marque'), 'Compact')
  await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Présentation de marque' }), 'LogoAndSlogan')
  expect(screen.getByRole('button', { name: 'Composer un aperçu de marque' })).toBeDisabled()
  await userEvent.selectOptions(screen.getByLabelText('Le slogan figure-t-il déjà dans le logo officiel ?'), 'yes')
  await waitFor(() => expect(screen.getByRole('button', { name: 'Composer un aperçu de marque' })).toBeEnabled())
  await userEvent.click(screen.getByRole('button', { name: 'Composer un aperçu de marque' }))
  await screen.findByRole('dialog', { name: 'Aperçu du visuel' })
  expect(compose).toHaveBeenCalledWith('post-1', 'source', 'approved', 4, 'LogoAndSlogan', true, expect.any(AbortSignal), undefined, 'Compact')
  expect(select).not.toHaveBeenCalled(); expect(generate).not.toHaveBeenCalled()
})
