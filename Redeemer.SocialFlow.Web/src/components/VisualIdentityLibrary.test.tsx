import { useState } from 'react'
import { beforeEach, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { VisualIdentityLibrary } from './VisualIdentityLibrary'
import { identitiesApi, type LibraryIdentity } from '../api/visualIdentities'
import { visualsApi } from '../api/visuals'
import { ApiError } from '../api/posts'
import { emptyIdentity } from '../lib/visualIdentities'
import { PostVisuals } from './PostVisuals'
import { post } from '../test/fixtures'
const identity = (): LibraryIdentity => ({
  id: 'rh',
  identityId: 'RH',
  name: 'Redeemer Holding',
  style: 'Modern',
  colors: ['#DA2E29'],
  slogan: 'Transformer par les valeurs',
  instructions: 'Clean',
  isActive: true,
  isDefault: true,
  revision: 1,
  logoId: null,
})
let entries: LibraryIdentity[]
function Editor() {
  const [value, onChange] = useState(emptyIdentity)
  return <VisualIdentityLibrary value={value} onChange={onChange} />
}
beforeEach(() => {
  entries = [identity()]
  vi.spyOn(identitiesApi, 'list').mockImplementation(async () => [...entries])
  vi.spyOn(identitiesApi, 'update').mockImplementation(async (id, revision, value) => {
    const result = {
      ...entries.find((v) => v.id === id)!,
      ...value,
      revision: revision + 1,
    } as LibraryIdentity
    entries = entries.map((v) => (v.id === id ? result : v))
    return result
  })
  vi.spyOn(identitiesApi, 'create').mockImplementation(async (value) => {
    const result = {
      ...value,
      id: 'new',
      revision: 1,
      isDefault: false,
      isActive: true,
      logoId: null,
    }
    entries.push(result)
    return result
  })
  vi.spyOn(identitiesApi, 'remove').mockImplementation(async (id) => {
    entries = entries.filter((v) => v.id !== id)
  })
  vi.spyOn(identitiesApi, 'setDefault').mockImplementation(async (id) => {
    entries = entries.map((v) => ({ ...v, isDefault: v.id === id }))
    return entries.find((v) => v.id === id)!
  })
  vi.spyOn(visualsApi, 'generate').mockRejectedValue(new Error('Should not generate automatically'))
  vi.spyOn(identitiesApi, 'logo').mockResolvedValue(new Blob(['png'], { type: 'image/png' }))
  URL.createObjectURL = vi.fn(() => 'blob:logo')
  URL.revokeObjectURL = vi.fn()
})
it('loads default; temporary adjustments do not update library; explicit save persists them', async () => {
  render(<Editor />)
  await waitFor(() =>
    expect(screen.getByLabelText('Nom de l’identité')).toHaveValue('Redeemer Holding'),
  )
  await userEvent.clear(screen.getByLabelText('Style visuel'))
  await userEvent.type(screen.getByLabelText('Style visuel'), 'Temporary')
  expect(identitiesApi.update).not.toHaveBeenCalled()
  expect(visualsApi.generate).not.toHaveBeenCalled()
  await userEvent.click(
    screen.getByRole('button', { name: 'Enregistrer les modifications de l’identité' }),
  )
  await screen.findByText('Bibliothèque mise à jour.')
  expect(identitiesApi.update).toHaveBeenCalledWith(
    'rh',
    1,
    expect.objectContaining({ style: 'Temporary' }),
    expect.any(AbortSignal),
  )
  expect(visualsApi.generate).not.toHaveBeenCalled()
})
it('creates another identity, chooses default, deactivates and deletes an unused identity', async () => {
  render(<Editor />)
  await screen.findByRole('button', { name: 'Désactiver l’identité' })
  await userEvent.click(screen.getByRole('button', { name: 'Nouvelle identité' }))
  await userEvent.type(screen.getByLabelText('Identifiant de l’identité'), 'ACME')
  await userEvent.type(screen.getByLabelText('Nom de l’identité'), 'Acme')
  await userEvent.type(screen.getByLabelText('Style visuel'), 'Flat')
  await userEvent.click(screen.getByRole('button', { name: 'Créer cette identité' }))
  await waitFor(() =>
    expect(screen.getByLabelText('Identité de la bibliothèque')).toHaveValue('new'),
  )
  await userEvent.click(screen.getByRole('button', { name: 'Définir par défaut' }))
  await waitFor(() =>
    expect(screen.getByRole('button', { name: 'Définir par défaut' })).toBeDisabled(),
  )
  await userEvent.click(screen.getByRole('button', { name: 'Désactiver l’identité' }))
  await screen.findByRole('button', { name: 'Réactiver l’identité' })
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  await userEvent.click(screen.getByRole('button', { name: 'Supprimer l’identité' }))
  await waitFor(() => expect(screen.getByLabelText('Identité de la bibliothèque')).toHaveValue(''))
  expect(identitiesApi.remove).toHaveBeenCalled()
})
it('shows a protected logo and releases its object URL', async () => {
  entries[0].logoId = 'logo'
  const view = render(<Editor />)
  await screen.findByAltText('Logo officiel de l’identité')
  expect(identitiesApi.logo).toHaveBeenCalledWith('rh', 'logo', expect.any(AbortSignal))
  view.unmount()
  expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:logo')
})
it('uploads an official logo only after explicit file selection', async () => {
  vi.spyOn(identitiesApi, 'uploadLogo').mockResolvedValue({
    ...identity(),
    logoId: 'logo',
    revision: 2,
  })
  render(<Editor />)
  await screen.findByLabelText('Importer le logo officiel')
  expect(identitiesApi.uploadLogo).not.toHaveBeenCalled()
  await userEvent.upload(
    screen.getByLabelText('Importer le logo officiel'),
    new File(['png'], 'official.png', { type: 'image/png' }),
  )
  await waitFor(() =>
    expect(identitiesApi.uploadLogo).toHaveBeenCalledWith(
      'rh',
      1,
      expect.any(File),
      expect.any(AbortSignal),
    ),
  )
})
it('keeps edited fields after a revision conflict', async () => {
  vi.mocked(identitiesApi.update).mockRejectedValue(
    new ApiError({ status: 409, title: 'Version périmée' }),
  )
  render(<Editor />)
  await screen.findByRole('button', { name: 'Désactiver l’identité' })
  await userEvent.type(screen.getByLabelText('Nom de l’identité'), ' adjusted')
  await userEvent.click(
    screen.getByRole('button', { name: 'Enregistrer les modifications de l’identité' }),
  )
  await screen.findByText('Version périmée')
  expect(screen.getByLabelText('Nom de l’identité')).toHaveValue('Redeemer Holding adjusted')
})
it('sends selected identity revision and temporary settings only on explicit generation', async () => {
  vi.mocked(visualsApi.runtime).mockResolvedValue({ enabled: true })
  vi.spyOn(visualsApi, 'list').mockResolvedValue([])
  render(<PostVisuals post={post()} brief="Garden" />)
  await waitFor(() =>
    expect(screen.getByLabelText('Nom de l’identité')).toHaveValue('Redeemer Holding'),
  )
  expect(visualsApi.generate).not.toHaveBeenCalled()
  await userEvent.click(screen.getByText('Identité visuelle pour la génération'))
  await userEvent.type(screen.getByLabelText('Style visuel'), ' adjusted')
  await userEvent.click(screen.getByRole('button', { name: 'Générer un visuel' }))
  expect(visualsApi.generate).toHaveBeenCalledWith(
    'post-1',
    expect.objectContaining({ libraryId: 'rh', revision: 1, style: 'Modern adjusted' }),
    'Garden',
    expect.any(AbortSignal),
  )
  expect(identitiesApi.update).not.toHaveBeenCalled()
})
it('disables generation for an inactive identity', async () => {
  entries[0] = { ...identity(), isActive: false, isDefault: false }
  vi.mocked(visualsApi.runtime).mockResolvedValue({ enabled: true })
  vi.spyOn(visualsApi, 'list').mockResolvedValue([])
  render(<PostVisuals post={post()} brief="Garden" />)
  await screen.findByText('Identité visuelle pour la génération')
  await userEvent.click(screen.getByText('Identité visuelle pour la génération'))
  await userEvent.selectOptions(screen.getByLabelText('Identité de la bibliothèque'), 'rh')
  expect(screen.getByRole('button', { name: 'Générer un visuel' })).toBeDisabled()
  expect(visualsApi.generate).not.toHaveBeenCalled()
})

it('preserves temporary fields and their revision when a logo upload meets a concurrent edit', async () => {
  vi.spyOn(identitiesApi, 'uploadLogo').mockImplementation(async () => {
    // Another editor writes revision 3 before the post-upload list refresh completes.
    entries = [{ ...identity(), revision: 3, name: 'Other editor', logoId: 'logo' }]
    return { ...identity(), revision: 2, logoId: 'logo' }
  })
  render(<Editor />)
  await screen.findByLabelText('Importer le logo officiel')
  await userEvent.type(screen.getByLabelText('Style visuel'), ' temporary')
  await userEvent.upload(
    screen.getByLabelText('Importer le logo officiel'),
    new File(['png'], 'official.png', { type: 'image/png' }),
  )
  await screen.findByText('Bibliothèque mise à jour.')
  expect(screen.getByLabelText('Style visuel')).toHaveValue('Modern temporary')
  await userEvent.click(
    screen.getByRole('button', { name: 'Enregistrer les modifications de l’identité' }),
  )
  await waitFor(() =>
    expect(identitiesApi.update).toHaveBeenCalledWith(
      'rh',
      2,
      expect.objectContaining({ name: 'Redeemer Holding', style: 'Modern temporary' }),
      expect.any(AbortSignal),
    ),
  )
})
