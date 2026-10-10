import { useState } from 'react'
import { beforeEach, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { VisualIdentityEditor } from './VisualIdentityEditor'
import {
  emptyIdentity,
  identityStorageKey,
  loadIdentities,
  saveIdentity,
} from '../lib/visualIdentities'

function Editor() {
  const [value, setValue] = useState(emptyIdentity)
  return <VisualIdentityEditor value={value} onChange={setValue} />
}
beforeEach(() => localStorage.clear())
it('synchronizes native swatches and manual codes, validating incomplete input', async () => {
  render(<Editor />)
  fireEvent.change(screen.getByLabelText('Choisir la couleur 1'), { target: { value: '#da2e29' } })
  expect(screen.getByLabelText('Code hexadécimal 1')).toHaveValue('#DA2E29')
  await userEvent.clear(screen.getByLabelText('Code hexadécimal 1'))
  await userEvent.type(screen.getByLabelText('Code hexadécimal 1'), '#d4af37')
  expect(screen.getByLabelText('Choisir la couleur 1')).toHaveValue('#d4af37')
  await userEvent.clear(screen.getByLabelText('Code hexadécimal 1'))
  await userEvent.type(screen.getByLabelText('Code hexadécimal 1'), '#XYZ')
  expect(screen.getByLabelText('Code hexadécimal 1')).toHaveAttribute('aria-invalid', 'true')
  expect(screen.getByText(/Format attendu/)).toBeInTheDocument()
})
it('enforces one to eight colors and keeps remaining values when deleting', async () => {
  render(<Editor />)
  expect(screen.getByRole('button', { name: 'Supprimer la couleur 1' })).toBeDisabled()
  for (let i = 0; i < 7; i++)
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter une couleur' }))
  expect(screen.getAllByLabelText(/Choisir la couleur/)).toHaveLength(8)
  expect(screen.getByRole('button', { name: 'Ajouter une couleur' })).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Choisir la couleur 8'), { target: { value: '#ffffff' } })
  await userEvent.click(screen.getByRole('button', { name: 'Supprimer la couleur 2' }))
  expect(screen.getByLabelText('Code hexadécimal 7')).toHaveValue('#FFFFFF')
  expect(screen.getByRole('button', { name: 'Ajouter une couleur' })).toBeEnabled()
})
it('offers Redeemer as an explicit editable preset without imposing it on other identities', async () => {
  render(<Editor />)
  expect(screen.getByLabelText('Nom de l’identité')).toHaveValue('')
  await userEvent.click(screen.getByRole('button', { name: 'Utiliser le modèle Redeemer Holding' }))
  expect(
    screen.getAllByLabelText(/Code hexadécimal/).map((e) => (e as HTMLInputElement).value),
  ).toEqual(['#DA2E29', '#D4AF37', '#000000', '#FFFFFF'])
  await userEvent.clear(screen.getByLabelText('Code hexadécimal 1'))
  await userEvent.type(screen.getByLabelText('Code hexadécimal 1'), '#123456')
  expect(localStorage.getItem(identityStorageKey)).toBeNull()
  await userEvent.click(screen.getByRole('button', { name: 'Nouvelle identité' }))
  expect(screen.getAllByLabelText(/Code hexadécimal/)).toHaveLength(1)
  expect(screen.getByLabelText('Nom de l’identité')).toHaveValue('')
})
it('saves explicitly and restores identities across mounts without mixing their colors', async () => {
  const view = render(<Editor />)
  await userEvent.click(screen.getByRole('button', { name: 'Utiliser le modèle Redeemer Holding' }))
  await userEvent.type(screen.getByLabelText('Style visuel'), 'Sobre')
  await userEvent.click(screen.getByRole('button', { name: 'Enregistrer l’identité localement' }))
  expect(await screen.findByText('Identité enregistrée sur ce navigateur.')).toBeInTheDocument()
  view.unmount()
  saveIdentity({ identityId: 'other', name: 'Other', style: 'Flat', colors: ['#112233'] })
  render(<Editor />)
  await userEvent.selectOptions(
    screen.getByLabelText('Identités enregistrées sur ce navigateur'),
    'redeemer-holding',
  )
  expect(screen.getByLabelText('Code hexadécimal 1')).toHaveValue('#DA2E29')
  await userEvent.selectOptions(
    screen.getByLabelText('Identités enregistrées sur ce navigateur'),
    'other',
  )
  expect(screen.getAllByLabelText(/Code hexadécimal/)).toHaveLength(1)
  expect(screen.getByLabelText('Code hexadécimal 1')).toHaveValue('#112233')
})
it('whitelists persisted fields and updates a saved identity by identifier', () => {
  const value = {
    identityId: 'one',
    name: 'One',
    style: 'Flat',
    colors: ['#abcdef'],
    apiKey: 'never-save',
    visualBrief: 'private brief',
  }
  saveIdentity(value)
  expect(JSON.parse(localStorage.getItem(identityStorageKey)!)[0]).toEqual({
    identityId: 'one',
    name: 'One',
    style: 'Flat',
    colors: ['#ABCDEF'],
    slogan: '',
  })
  saveIdentity({ ...value, colors: ['#000000'] })
  expect(loadIdentities()).toHaveLength(1)
})
it('reports unavailable storage without blocking editing', async () => {
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
    throw new Error('Quota')
  })
  render(<Editor />)
  await userEvent.click(screen.getByRole('button', { name: 'Utiliser le modèle Redeemer Holding' }))
  await userEvent.type(screen.getByLabelText('Style visuel'), 'Sobre')
  await userEvent.click(screen.getByRole('button', { name: 'Enregistrer l’identité localement' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Sauvegarde locale indisponible')
  expect(screen.getByLabelText('Code hexadécimal 1')).toBeEnabled()
})
it('handles corrupt storage and rejects invalid palettes', () => {
  localStorage.setItem(identityStorageKey, 'not json')
  render(<Editor />)
  expect(screen.getByRole('status')).toHaveTextContent('ne peuvent pas être chargées')
  expect(() =>
    saveIdentity({ identityId: 'x', name: 'X', style: 'Flat', colors: ['red'] }),
  ).toThrow()
})
