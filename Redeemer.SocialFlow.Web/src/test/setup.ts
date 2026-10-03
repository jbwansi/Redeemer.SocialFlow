import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, vi } from 'vitest'

// Optional Development integrations are unavailable unless explicitly mocked by a test.
vi.mock('../api/linkedin', async () => {
  const actual = await vi.importActual<typeof import('../api/linkedin')>('../api/linkedin')
  const { ApiError } = await import('../api/posts')
  const unavailable = () => Promise.reject(new ApiError({ status: 404, title: 'Unavailable' }))
  return {
    ...actual,
    linkedInApi: {
      runtime: vi.fn(unavailable),
      publication: vi.fn(unavailable),
      preview: vi.fn(unavailable),
      publish: vi.fn(unavailable),
    },
  }
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.unstubAllEnvs()
})
HTMLDialogElement.prototype.showModal = function () {
  this.setAttribute('open', '')
}
HTMLDialogElement.prototype.close = function () {
  this.removeAttribute('open')
}
