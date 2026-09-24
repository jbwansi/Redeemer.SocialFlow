export const dateLabel = (value: string) =>
  new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
export const timeLabel = (value: string) =>
  new Date(value).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
export function dayKey(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}
export function dateBoundary(value: string, end = false) {
  if (!value) return undefined
  const instant = new Date(`${value}T${end ? '23:59:59.999' : '00:00:00'}`).toISOString()
  // The API stores .NET ticks, so include the final fraction of the selected day.
  return end ? instant.replace('.999Z', '.9999999Z') : instant
}
export const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone
