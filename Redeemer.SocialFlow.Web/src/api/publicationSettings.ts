import { apiRequest } from './posts'

export interface PublicationSettings {
  facebookConnectionId: string | null
  linkedInConnectionId: string | null
  timeZone: string
  suggestedTimes: string[]
  facebookAutomaticEnabled: boolean
  linkedInAutomaticEnabled: boolean
}

const path = '/workspace-publication-settings'
export const publicationSettingsApi = {
  get: (signal?: AbortSignal) => apiRequest<PublicationSettings>(path, { signal }),
  save: (settings: PublicationSettings, signal?: AbortSignal) =>
    apiRequest<PublicationSettings>(path, { signal, method: 'PUT', body: JSON.stringify(settings) }),
}
