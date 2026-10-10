import type { VisualIdentity } from '../api/visuals'

export const identityStorageKey = 'socialflow.visual-identities.v1'
export const validColor = (value: string) => /^#[0-9a-f]{6}$/i.test(value)
export const emptyIdentity = (): VisualIdentity => ({
  identityId: '',
  name: '',
  style: '',
  colors: ['#000000'],
  slogan: '',
})
export const redeemerIdentity = (): VisualIdentity => ({
  ...emptyIdentity(),
  identityId: 'redeemer-holding',
  name: 'Redeemer Holding',
  colors: ['#DA2E29', '#D4AF37', '#000000', '#FFFFFF'],
})
// Whitelist only identity fields. Never persist credentials, briefs or image data.
export function cleanIdentity(value: unknown): VisualIdentity | null {
  if (!value || typeof value !== 'object') return null
  const v = value as Record<string, unknown>
  if (
    typeof v.identityId !== 'string' ||
    !v.identityId.trim() ||
    v.identityId.length > 100 ||
    typeof v.name !== 'string' ||
    !v.name.trim() ||
    v.name.length > 200 ||
    typeof v.style !== 'string' ||
    !v.style.trim() ||
    v.style.length > 1000 ||
    !Array.isArray(v.colors) ||
    v.colors.length < 1 ||
    v.colors.length > 8 ||
    !v.colors.every((c) => typeof c === 'string' && validColor(c)) ||
    (v.slogan !== undefined && (typeof v.slogan !== 'string' || v.slogan.length > 300))
  )
    return null
  return {
    identityId: v.identityId.trim(),
    name: v.name.trim(),
    style: v.style.trim(),
    colors: v.colors.map((c) => c.toUpperCase()),
    slogan: (v.slogan as string | undefined)?.trim() || '',
  }
}
export function loadIdentities(): VisualIdentity[] {
  const raw = localStorage.getItem(identityStorageKey)
  if (!raw) return []
  const parsed: unknown = JSON.parse(raw)
  if (!Array.isArray(parsed) || parsed.length > 50) throw new Error('Invalid identity store')
  const entries = parsed.map(cleanIdentity)
  if (entries.some((v) => v === null)) throw new Error('Invalid identity store')
  return entries as VisualIdentity[]
}
export function saveIdentity(identity: VisualIdentity): VisualIdentity[] {
  const clean = cleanIdentity(identity)
  if (!clean) throw new Error('Invalid identity')
  const entries = loadIdentities().filter((v) => v.identityId !== clean.identityId)
  if (entries.length >= 50) throw new Error('Identity store full')
  entries.push(clean)
  localStorage.setItem(identityStorageKey, JSON.stringify(entries))
  return entries
}
