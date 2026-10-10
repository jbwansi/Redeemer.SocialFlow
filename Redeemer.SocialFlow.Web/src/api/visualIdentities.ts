import { visualRequest, type VisualIdentity } from './visuals'
export interface LibraryIdentity extends VisualIdentity {
  id: string
  revision: number
  isActive: boolean
  isDefault: boolean
  logoId: string | null
}
const root = '/visual-identities'
const path = (id: string) => root + '/' + encodeURIComponent(id)
// Only identity fields enter the write contract; no credentials or private storage paths.
const input = (v: VisualIdentity) => ({
  identityId: v.identityId,
  name: v.name,
  style: v.style,
  colors: v.colors,
  slogan: v.slogan || null,
  instructions: v.instructions || null,
  isActive: v.isActive ?? true,
})
export const identitiesApi = {
  list: (signal?: AbortSignal) => visualRequest<LibraryIdentity[]>(root, { signal }),
  create: (value: VisualIdentity, signal?: AbortSignal) =>
    visualRequest<LibraryIdentity>(root, {
      method: 'POST',
      body: JSON.stringify(input(value)),
      signal,
    }),
  update: (id: string, revision: number, value: VisualIdentity, signal?: AbortSignal) =>
    visualRequest<LibraryIdentity>(path(id), {
      method: 'PUT',
      body: JSON.stringify({ identity: input(value), revision }),
      signal,
    }),
  setDefault: (id: string, revision: number, signal?: AbortSignal) =>
    visualRequest<LibraryIdentity>(path(id) + '/default', {
      method: 'POST',
      body: JSON.stringify({ revision }),
      signal,
    }),
  remove: (id: string, revision: number, signal?: AbortSignal) =>
    visualRequest<void>(path(id), { method: 'DELETE', body: JSON.stringify({ revision }), signal }),
  uploadLogo: (id: string, revision: number, file: File, signal?: AbortSignal) => {
    const body = new FormData()
    body.append('file', file)
    body.append('revision', String(revision))
    return visualRequest<LibraryIdentity>(path(id) + '/logo', { method: 'POST', body, signal })
  },
  logo: (id: string, logoId: string, signal?: AbortSignal) =>
    visualRequest<Blob>(path(id) + '/logos/' + encodeURIComponent(logoId), { signal }, true),
}
export const effectiveIdentity = (v: LibraryIdentity): VisualIdentity => ({
  identityId: v.identityId,
  name: v.name,
  style: v.style,
  colors: [...v.colors],
  slogan: v.slogan || '',
  instructions: v.instructions || '',
  libraryId: v.id,
  revision: v.revision,
  isActive: v.isActive,
})
