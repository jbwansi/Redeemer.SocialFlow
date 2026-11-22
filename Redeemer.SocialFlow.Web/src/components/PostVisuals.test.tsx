import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { visualsApi, type Visual } from '../api/visuals'
import { ApiError, postsApi, type Status } from '../api/posts'
import { post } from '../test/fixtures'
import { identitiesApi } from '../api/visualIdentities'
import { PostVisuals } from './PostVisuals'
import { PostEditor } from './PostEditor'
import { PostDetails } from './PostDetails'

const visual = (id = 'v1', selected = false): Visual => ({
  id,
  socialPostId: 'post-1',
  origin: 1,
  contentType: 'image/png',
  width: 16,
  height: 12,
  byteLength: 100,
  sha256: 'hash',
  createdAt: '2026-10-10T00:00:00Z',
  validatedAt: null,
  selected,
  generation: null,
})
let items: Visual[]
beforeEach(() => {
  vi.spyOn(identitiesApi, 'list').mockResolvedValue([])
  items = []
  vi.spyOn(visualsApi, 'prepare').mockResolvedValue({ subject: 'Saved title', message: 'Saved message', scene: 'Scene', brief: 'Garden', style: 'Flat', exclusions: ['No invented logo'], conceptToken: 'approved-concept' })
  vi.mocked(visualsApi.runtime).mockResolvedValue({ enabled: true })
  vi.spyOn(visualsApi, 'list').mockImplementation(async () => [...items])
  vi.spyOn(visualsApi, 'image').mockResolvedValue(new Blob(['png'], { type: 'image/png' }))
  vi.spyOn(visualsApi, 'generate').mockImplementation(async () => {
    const v = visual('v' + (items.length + 1))
    items.push(v)
    return v
  })
  vi.spyOn(visualsApi, 'upload').mockImplementation(async () => {
    const v = visual()
    items.push(v)
    return v
  })
  vi.spyOn(visualsApi, 'select').mockImplementation(async (_, id) => {
    items = items.map((v) => ({ ...v, selected: v.id === id }))
    return items.find((v) => v.id === id)!
  })
  vi.spyOn(visualsApi, 'remove').mockImplementation(async (_, id) => {
    items = items.filter((v) => v.id !== id)
  })
  URL.createObjectURL = vi.fn(() => 'blob:private-preview')
  URL.revokeObjectURL = vi.fn()
})
async function identity() {
  await userEvent.click(screen.getByText('Identité visuelle pour la génération'))
  await userEvent.type(screen.getByLabelText('Identifiant de l’identité'), 'brand')
  await userEvent.type(screen.getByLabelText('Nom de l’identité'), 'Brand')
  await userEvent.type(screen.getByLabelText('Style visuel'), 'Flat')
  await userEvent.clear(screen.getByLabelText('Code hexadécimal 1'))
  await userEvent.type(screen.getByLabelText('Code hexadécimal 1'), '#123456')
}
async function approveConcept() {
  await userEvent.click(screen.getByRole('button', { name: 'Préparer le concept (sans coût)' }))
  await userEvent.click(await screen.findByRole('checkbox', { name: 'Je valide ce concept avant la génération payante' }))
}
describe('visual management', () => {
  it('shows terminology warnings before human confirmation and a paid request', async () => {
    vi.mocked(visualsApi.prepare).mockResolvedValue({ subject: 'Subject', message: 'Message', scene: 'Scene', brief: 'Garden', style: 'Flat', exclusions: [], conceptToken: 'reviewed', warnings: ['Terminologie ambiguë : validation humaine requise.'] })
    render(<PostVisuals post={post()} brief="Garden" />)
    await screen.findByText('Aucun visuel pour cette publication.')
    await identity()
    await userEvent.click(screen.getByRole('button', { name: 'Préparer le concept (sans coût)' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Terminologie ambiguë')
    expect(screen.getByRole('button', { name: 'Générer un visuel' })).toBeDisabled()
    expect(visualsApi.generate).not.toHaveBeenCalled()
  })
  it('does not generate on load/save; preserves editorial fields through generation and regeneration', async () => {
    const save = vi.spyOn(postsApi, 'update').mockResolvedValue(post())
    render(<PostEditor post={post({ visualBrief: 'Garden' })} close={vi.fn()} saved={vi.fn()} />)
    await screen.findByText('Aucun visuel pour cette publication.')
    expect(visualsApi.generate).not.toHaveBeenCalled()
    const content = (screen.getByLabelText('Content') as HTMLTextAreaElement).value
    await identity()
    await approveConcept()
    await userEvent.click(screen.getByRole('button', { name: 'Générer un visuel' }))
    await screen.findByAltText('Visuel candidat')
    expect(visualsApi.generate).toHaveBeenCalledWith(
      'post-1',
      expect.objectContaining({ identityId: 'brand', colors: ['#123456'] }),
      'Garden',
      expect.any(AbortSignal),
      'approved-concept',
    )
    await approveConcept()
    await userEvent.click(screen.getByRole('button', { name: 'Régénérer' }))
    await waitFor(() => expect(screen.getAllByAltText('Visuel candidat')).toHaveLength(2))
    expect(screen.getByLabelText('Content')).toHaveValue(content)
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    expect(save).toHaveBeenCalled()
    expect(visualsApi.generate).toHaveBeenCalledTimes(2)
  })
  it('previews freely, requires confirmation and invalidates on identity changes', async () => {
    render(<PostVisuals post={post()} brief="Garden" />)
    await screen.findByText('Aucun visuel pour cette publication.')
    await identity()
    expect(screen.getByRole('button', { name: 'Générer un visuel' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Préparer le concept (sans coût)' }))
    await screen.findByRole('region', { name: 'Concept visuel' })
    expect(visualsApi.generate).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Générer un visuel' })).toBeDisabled()
    await userEvent.click(screen.getByRole('checkbox'))
    expect(screen.getByRole('button', { name: 'Générer un visuel' })).toBeEnabled()
    await userEvent.type(screen.getByLabelText('Style visuel'), ' changed')
    expect(screen.queryByRole('region', { name: 'Concept visuel' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Générer un visuel' })).toBeDisabled()
  })
  it('requires saving editorial edits before preparing a concept', async () => {
    render(<PostEditor post={post({ visualBrief: 'Garden' })} close={vi.fn()} saved={vi.fn()} />)
    await screen.findByText('Aucun visuel pour cette publication.')
    await userEvent.type(screen.getByLabelText('Content'), ' changed')
    expect(screen.getByRole('button', { name: 'Préparer le concept (sans coût)' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Générer un visuel' })).toBeDisabled()
    expect(visualsApi.prepare).not.toHaveBeenCalled()
    expect(visualsApi.generate).not.toHaveBeenCalled()
  })
  it('uploads, previews, selects, protects selected and deletes an unused version', async () => {
    items = [visual('old')]
    const view = render(<PostVisuals post={post()} brief="Garden" />)
    await screen.findByAltText('Visuel candidat')
    await userEvent.upload(
      screen.getByLabelText('Importer une image'),
      new File(['png'], 'image.png', { type: 'image/png' }),
    )
    await waitFor(() => expect(screen.getAllByRole('article')).toHaveLength(2))
    await userEvent.click(
      within(screen.getAllByRole('article')[1]).getByRole('button', {
        name: 'Sélectionner ce visuel',
      }),
    )
    await screen.findByAltText('Visuel sélectionné')
    expect(
      within(screen.getAllByRole('article')[1]).getByRole('button', { name: 'Supprimer' }),
    ).toBeDisabled()
    await userEvent.click(
      within(screen.getAllByRole('article')[0]).getByRole('button', { name: 'Supprimer' }),
    )
    await waitFor(() => expect(screen.getAllByRole('article')).toHaveLength(1))
    expect(visualsApi.remove).toHaveBeenCalledWith('post-1', 'old', expect.any(AbortSignal))
    view.unmount()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:private-preview')
  })
  it.each([2, 3, 4, 5, 7, 8] as Status[])(
    'prevents visual mutations for status %s',
    async (status) => {
      items = [visual()]
      render(<PostVisuals post={post({ status })} />)
      await screen.findByAltText('Visuel candidat')
      expect(screen.queryByRole('button', { name: 'Régénérer' })).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Sélectionner ce visuel' })).toBeDisabled()
      expect(screen.getByRole('button', { name: 'Supprimer' })).toBeDisabled()
      expect(visualsApi.generate).not.toHaveBeenCalled()
    },
  )
  it('shows only the selected image in details without changing publication actions', async () => {
    items = [visual('one'), visual('two', true)]
    render(
      <PostDetails
        post={post()}
        close={vi.fn()}
        edit={vi.fn()}
        changed={vi.fn()}
        removed={vi.fn()}
      />,
    )
    await screen.findByAltText('Visuel sélectionné')
    expect(screen.queryByAltText('Visuel candidat')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Submit for review' })).toBeEnabled()
    expect(visualsApi.generate).not.toHaveBeenCalled()
  })
  it('reports API failures without discarding inputs or retrying paid requests', async () => {
    vi.mocked(visualsApi.generate).mockRejectedValue(
      new ApiError({ status: 429, title: 'Limite de génération atteinte' }),
    )
    render(<PostVisuals post={post()} brief="Garden" />)
    await screen.findByText('Aucun visuel pour cette publication.')
    await identity()
    await approveConcept()
    await userEvent.click(screen.getByRole('button', { name: 'Générer un visuel' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Limite de génération')
    expect(screen.getByLabelText('Nom de l’identité')).toHaveValue('Brand')
    expect(visualsApi.generate).toHaveBeenCalledTimes(1)
  })
  it('cancels waiting with an AbortSignal and explains uncertain completion', async () => {
    vi.mocked(visualsApi.generate).mockImplementation(
      (_, __, ___, signal) =>
        new Promise((_, reject) => {
          signal!.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')))
        }),
    )
    render(<PostVisuals post={post()} brief="Garden" />)
    await screen.findByText('Aucun visuel pour cette publication.')
    await identity()
    await approveConcept()
    await userEvent.click(screen.getByRole('button', { name: 'Générer un visuel' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Annuler l’attente' }))
    await screen.findByText(/L’opération peut déjà avoir été traitée/)
    expect(screen.getByRole('button', { name: 'Générer un visuel' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Préparer le concept (sans coût)' })).toBeEnabled()
  })
  it('handles disabled gateway and empty posts', async () => {
    vi.mocked(visualsApi.runtime).mockResolvedValue({ enabled: false })
    render(<PostVisuals post={post()} />)
    await screen.findByText('Gestion des visuels désactivée sur ce poste.')
    expect(visualsApi.list).not.toHaveBeenCalled()
    expect(visualsApi.generate).not.toHaveBeenCalled()
  })
  it('rejects unsupported imports before sending bytes', async () => {
    render(<PostVisuals post={post()} />)
    await screen.findByText('Aucun visuel pour cette publication.')
    const user = userEvent.setup({ applyAccept: false })
    await user.upload(
      screen.getByLabelText('Importer une image'),
      new File(['svg'], 'bad.svg', { type: 'image/svg+xml' }),
    )
    expect(await screen.findByRole('alert')).toHaveTextContent('Image non acceptée')
    expect(visualsApi.upload).not.toHaveBeenCalled()
  })
  it('does not create a blob URL after unmount even if a late response arrives', async () => {
    items = [visual()]
    let finish!: (blob: Blob) => void
    vi.mocked(visualsApi.image).mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    const view = render(<PostVisuals post={post()} />)
    await waitFor(() => expect(visualsApi.image).toHaveBeenCalled())
    const signal = vi.mocked(visualsApi.image).mock.calls[0][2]
    view.unmount()
    finish(new Blob(['png']))
    await Promise.resolve()
    expect(signal!.aborted).toBe(true)
    expect(URL.createObjectURL).not.toHaveBeenCalled()
  })
  it('shows stale-status conflicts and preserves candidate history', async () => {
    items = [visual()]
    vi.mocked(visualsApi.select).mockRejectedValue(
      new ApiError({ status: 409, title: 'Publication non modifiable' }),
    )
    render(<PostVisuals post={post()} />)
    await screen.findByAltText('Visuel candidat')
    await userEvent.click(screen.getByRole('button', { name: 'Sélectionner ce visuel' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Publication non modifiable')
    expect(screen.getAllByRole('article')).toHaveLength(1)
  })
  it('allows rejected posts and reports preview failures', async () => {
    items = [visual()]
    vi.mocked(visualsApi.image).mockRejectedValue(
      new ApiError({ status: 500, title: 'Image indisponible' }),
    )
    render(<PostVisuals post={post({ status: 6 })} />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Image indisponible')
    expect(screen.getByRole('button', { name: 'Sélectionner ce visuel' })).toBeEnabled()
  })
  it('does not generate with an invalid manually edited color', async () => {
    render(<PostVisuals post={post()} brief="Garden" />)
    await screen.findByText('Aucun visuel pour cette publication.')
    await identity()
    await userEvent.clear(screen.getByLabelText('Code hexadécimal 1'))
    await userEvent.type(screen.getByLabelText('Code hexadécimal 1'), '#XYZ')
    await userEvent.click(screen.getByRole('button', { name: 'Préparer le concept (sans coût)' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Informations manquantes')
    expect(visualsApi.generate).not.toHaveBeenCalled()
  })
})
