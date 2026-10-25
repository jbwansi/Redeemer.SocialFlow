// @vitest-environment-options { "url": "https://localhost:65474/" }
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, expect, it, vi } from 'vitest'
import AccountShell from './AccountShell'
import { accountRequest, AccountError } from './api/accounts'

vi.mock('./api/accounts', async () => {
  const actual = await vi.importActual<typeof import('./api/accounts')>('./api/accounts')
  return { ...actual, accountRequest: vi.fn() }
})
vi.mock('./App', () => ({ default: () => <div>Historical business data</div> }))
beforeEach(() => {
  history.replaceState(null, '', '/#login')
  vi.mocked(accountRequest).mockReset().mockRejectedValue(new AccountError(401, 'Session requise'))
})

it('protects business routes and displays login without loading historical data', async () => {
  history.replaceState(null, '', '/#posts')
  render(<AccountShell />)
  expect(await screen.findByRole('button', { name: 'Se connecter' })).toBeInTheDocument()
  expect(screen.queryByText('Historical business data')).not.toBeInTheDocument()
  expect(accountRequest).toHaveBeenCalledWith('me')
})

it('shows only the profile of an ordinary account and handles logout', async () => {
  vi.mocked(accountRequest).mockResolvedValue({ id: 'one', email: 'person@example.test' })
  render(<AccountShell />)
  expect(await screen.findByText('person@example.test')).toBeInTheDocument()
  expect(screen.queryByText('Historical business data')).not.toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: 'Se déconnecter' }))
  expect(accountRequest).toHaveBeenCalledWith('logout', {})
  expect(await screen.findByRole('button', { name: 'Se connecter' })).toBeInTheDocument()
})

it('submits credentials without storing them and loads the profile', async () => {
  const storage = vi.spyOn(Storage.prototype, 'setItem')
  render(<AccountShell />)
  await screen.findByRole('button', { name: 'Se connecter' })
  await userEvent.type(screen.getByLabelText('Adresse email'), 'person@example.test')
  await userEvent.type(screen.getByLabelText('Mot de passe'), 'Test-Password-947!')
  vi.mocked(accountRequest)
    .mockResolvedValueOnce(undefined)
    .mockResolvedValue({ id: 'one', email: 'person@example.test' })
  await userEvent.click(screen.getByRole('button', { name: 'Se connecter' }))
  expect(await screen.findByText('person@example.test')).toBeInTheDocument()
  expect(accountRequest).toHaveBeenCalledWith('login', {
    email: 'person@example.test',
    password: 'Test-Password-947!',
  })
  expect(storage).not.toHaveBeenCalled()
})

it('clears a session when revalidation reports expiry', async () => {
  vi.mocked(accountRequest).mockResolvedValue({ id: 'one', email: 'person@example.test' })
  render(<AccountShell />)
  await screen.findByText('person@example.test')
  vi.mocked(accountRequest).mockRejectedValue(new AccountError(401, 'Session expirée'))
  fireEvent.focus(window)
  expect(await screen.findByRole('button', { name: 'Se connecter' })).toBeInTheDocument()
})

it('keeps email links in memory and requires explicit confirmation', async () => {
  history.replaceState(null, '', '/#confirm-email?email=person%40example.test&code=private-code')
  render(<AccountShell />)
  await screen.findByRole('button', { name: 'Confirmer mon adresse' })
  expect(location.hash).toBe('#confirm-email')
  expect(accountRequest).not.toHaveBeenCalledWith('confirm-email', expect.anything())
  vi.mocked(accountRequest).mockResolvedValue(undefined)
  await userEvent.click(screen.getByRole('button', { name: 'Confirmer mon adresse' }))
  expect(accountRequest).toHaveBeenCalledWith('confirm-email', {
    email: 'person@example.test',
    code: 'private-code',
  })
  expect(await screen.findByRole('status')).toHaveTextContent('Adresse confirmée')
})

it('displays server errors and re-enables submission', async () => {
  render(<AccountShell />)
  await screen.findByRole('button', { name: 'Se connecter' })
  await userEvent.type(screen.getByLabelText('Adresse email'), 'person@example.test')
  await userEvent.type(screen.getByLabelText('Mot de passe'), 'invalid')
  vi.mocked(accountRequest).mockRejectedValue(new AccountError(429, 'Trop de tentatives'))
  await userEvent.click(screen.getByRole('button', { name: 'Se connecter' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Trop de tentatives')
  await waitFor(() => expect(screen.getByRole('button', { name: 'Se connecter' })).toBeEnabled())
})
