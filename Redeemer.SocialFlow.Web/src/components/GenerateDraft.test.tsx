import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import App from '../App'
import { ApiError, postsApi, type GenerateSocialPostDraftResult } from '../api/posts'
import { post } from '../test/fixtures'
import { GenerateDraft } from './GenerateDraft'

async function fillBrief() {
  await userEvent.type(screen.getByLabelText('Sujet'), 'Notre communauté')
  await userEvent.type(screen.getByLabelText('Objectif'), 'Inviter')
  await userEvent.type(screen.getByLabelText('Public cible'), 'Familles')
  await userEvent.selectOptions(screen.getByLabelText('Plateforme'), '3')
}

describe('AI draft generation', () => {
  it('validates the brief and blocks duplicate submissions and dismissal while pending', async () => {
    let resolve!: (result: GenerateSocialPostDraftResult) => void
    const generate = vi.spyOn(postsApi, 'generateDraft').mockReturnValue(
      new Promise((done) => {
        resolve = done
      }),
    )
    const saved = vi.fn()
    const close = vi.fn()
    render(<GenerateDraft saved={saved} close={close} />)
    expect(screen.getByRole('button', { name: 'Générer le brouillon' })).toBeDisabled()
    await fillBrief()
    await userEvent.click(screen.getByRole('button', { name: 'Générer le brouillon' }))
    expect(screen.getByRole('status')).toHaveTextContent('Génération du brouillon en cours')
    expect(screen.getByLabelText('Sujet')).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Génération…' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Annuler' })).toBeDisabled()
    fireEvent.submit(screen.getByLabelText('Sujet').closest('form')!)
    fireEvent(screen.getByRole('dialog'), new Event('cancel', { cancelable: true }))
    expect(close).not.toHaveBeenCalled()
    expect(generate).toHaveBeenCalledTimes(1)
    expect(generate).toHaveBeenCalledWith({
      subject: 'Notre communauté',
      objective: 'Inviter',
      audience: 'Familles',
      platform: 3,
    })
    const result = { post: post(), warnings: [] }
    await act(async () => resolve(result))
    expect(saved).toHaveBeenCalledWith(result)
  })

  it('retains inputs and displays ProblemDetails, then allows retry', async () => {
    const generate = vi
      .spyOn(postsApi, 'generateDraft')
      .mockRejectedValueOnce(
        new ApiError({
          title: 'Draft generation failed',
          detail: 'Please try again later.',
          traceId: 'ai-trace',
          errors: { subject: ['Check subject'] },
        }),
      )
      .mockResolvedValueOnce({ post: post(), warnings: [] })
    const saved = vi.fn()
    render(<GenerateDraft saved={saved} close={vi.fn()} />)
    await fillBrief()
    await userEvent.click(screen.getByRole('button', { name: 'Générer le brouillon' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Please try again later.')
    expect(screen.getByRole('alert')).toHaveTextContent('ai-trace')
    expect(screen.getByRole('alert')).toHaveTextContent('Check subject')
    expect(screen.getByLabelText('Sujet')).toHaveValue('Notre communauté')
    expect(saved).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Générer le brouillon' }))
    expect(generate).toHaveBeenCalledTimes(2)
    expect(saved).toHaveBeenCalled()
  })

  it.each([{ warnings: [] }, { warnings: ['Vérifier les faits', 'Vérifier les faits'] }])(
    'opens the saved draft for editing, refreshes posts and displays warnings %j',
    async ({ warnings }) => {
      window.history.replaceState(null, '', '#posts')
      const generated = post({ title: 'Brouillon IA', content: 'Texte généré', status: 1 })
      let persisted = false
      const list = vi
        .spyOn(postsApi, 'list')
        .mockImplementation(async () => (persisted ? [generated] : []))
      vi.spyOn(postsApi, 'generateDraft').mockImplementation(async () => {
        persisted = true
        return { post: generated, warnings }
      })
      const transition = vi.spyOn(postsApi, 'transition')
      const update = vi.spyOn(postsApi, 'update').mockResolvedValue(generated)
      render(<App />)
      await screen.findByText('Your next story starts here')
      const initialCalls = list.mock.calls.length
      await userEvent.click(screen.getByRole('button', { name: 'Générer avec l’IA' }))
      await fillBrief()
      await userEvent.click(screen.getByRole('button', { name: 'Générer le brouillon' }))
      const editor = await screen.findByRole('dialog', { name: 'Edit post' })
      expect(within(editor).getByLabelText('Post title')).toHaveValue('Brouillon IA')
      expect(within(editor).getByLabelText('Content')).toHaveValue('Texte généré')
      expect(within(editor).getByRole('note')).toHaveTextContent('relecture humaine nécessaire')
      if (warnings.length) expect(within(editor).getAllByText('Vérifier les faits')).toHaveLength(2)
      else expect(within(editor).queryByText('Avertissements de l’IA')).not.toBeInTheDocument()
      await waitFor(() => expect(list.mock.calls.length).toBeGreaterThan(initialCalls))
      expect(screen.getByRole('button', { name: 'Brouillon IA' })).toBeInTheDocument()
      await userEvent.click(within(editor).getByRole('button', { name: 'Save changes' }))
      expect(update).toHaveBeenCalledWith(
        generated.id,
        expect.objectContaining({ content: 'Texte généré' }),
      )
      const details = await screen.findByRole('dialog', { name: 'Brouillon IA' })
      expect(within(details).getByText('Draft')).toBeInTheDocument()
      expect(within(details).getByRole('note')).toBeInTheDocument()
      expect(transition).not.toHaveBeenCalled()
    },
  )
})
