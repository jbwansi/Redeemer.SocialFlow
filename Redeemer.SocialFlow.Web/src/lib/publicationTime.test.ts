import { expect, it } from 'vitest'
import { publicationInstant } from './publicationTime'

it('converts preferences in the workspace zone independently of the browser zone', () => {
  expect(publicationInstant('2026-01-05T09:30', 'Europe/Zurich')).toBe('2026-01-05T08:30:00.000Z')
  expect(publicationInstant('2026-07-05T09:30', 'Europe/Zurich')).toBe('2026-07-05T07:30:00.000Z')
})

it('rejects skipped and repeated local wall times', () => {
  expect(() => publicationInstant('2026-03-29T02:30', 'Europe/Zurich')).toThrow()
  expect(() => publicationInstant('2026-10-25T02:30', 'Europe/Zurich')).toThrow()
})
