import { ApiError } from '../api/posts'

// The wall time can be nonexistent (spring change) or ambiguous (autumn change).
// Require an unambiguous choice rather than silently dispatching at a different instant.
export function publicationInstant(value: string, zone: string): string {
  const wall = Date.parse(value + ':00Z')
  const format = new Intl.DateTimeFormat('en-CA', {
    timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
  })
  function local(instant: number) {
    const parts = Object.fromEntries(format.formatToParts(instant).map(x => [x.type, x.value]))
    return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`
  }
  const candidates = new Set<number>()
  for (const delta of [-86400000, 0, 86400000]) {
    const sample = wall + delta
    const offset = Date.parse(local(sample) + ':00Z') - sample
    const instant = wall - offset
    if (local(instant) === value) candidates.add(instant)
  }
  if (candidates.size !== 1) throw new ApiError({ title: 'Heure de planification invalide', detail: 'Cette heure est inexistante ou ambiguë dans le fuseau de l’espace. Choisissez une autre heure.' })
  return new Date([...candidates][0]).toISOString()
}
