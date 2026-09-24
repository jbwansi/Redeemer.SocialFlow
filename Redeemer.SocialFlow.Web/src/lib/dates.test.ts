import { describe, expect, it } from 'vitest'
import { dateBoundary, dayKey } from './dates'

describe('local date handling', () => {
  it('omits empty date filters', () => {
    expect(dateBoundary('')).toBeUndefined()
  })
  it('uses local midnight and includes the last .NET tick of the selected day', () => {
    expect(dateBoundary('2026-09-24')).toBe(new Date(2026, 8, 24).toISOString())
    expect(dateBoundary('2026-09-24', true)).toBe(
      new Date(2026, 8, 24, 23, 59, 59, 999).toISOString().replace('.999Z', '.9999999Z'),
    )
  })
  it('groups calendar days in local time instead of slicing the UTC date', () => {
    expect(dayKey(new Date(2026, 8, 24, 0, 1))).toBe('2026-09-24')
    expect(dayKey(new Date(2026, 8, 24, 23, 59))).toBe('2026-09-24')
  })
})
