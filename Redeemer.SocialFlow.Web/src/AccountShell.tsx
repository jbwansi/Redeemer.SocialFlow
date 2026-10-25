import { useEffect, useState, type FormEvent } from 'react'
import App from './App'
import { InvitationAcceptance } from './InvitationAcceptance'
import { WorkspaceShell } from './WorkspaceShell'
import { AccountError, accountRequest, type Account } from './api/accounts'
import { setLocalAdministration } from './api/posts'
import './accounts.css'

const pages = [
  'login',
  'register',
  'forgot-password',
  'reset-password',
  'confirm-email',
  'resend-confirmation',
] as const
type Page = (typeof pages)[number]
const titles: Record<Page, string> = {
  login: 'Heureux de vous retrouver',
  register: 'Créez votre compte',
  'forgot-password': 'Retrouvez votre accès',
  'reset-password': 'Un nouveau mot de passe',
  'confirm-email': 'Confirmez votre adresse',
  'resend-confirmation': 'Recevez un nouveau lien',
}
function route() {
  const [name, query] = location.hash.slice(1).split('?')
  return {
    page: pages.includes(name as Page) ? (name as Page) : ('login' as Page),
    params: new URLSearchParams(query),
  }
}

export default function AccountShell() {
  const [initial] = useState(route)
  const [invitation, setInvitation] = useState(() =>
    location.hash.startsWith('#accept-invitation?')
      ? new URLSearchParams(location.hash.split('?')[1]).get('code')
      : null,
  )
  const [page, setPage] = useState<Page>(initial.page)
  const [email, setEmail] = useState(initial.params.get('email') ?? '')
  const [code] = useState(initial.params.get('code') ?? '')
  const [password, setPassword] = useState('')
  const [account, setAccount] = useState<Account | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [local, setLocal] = useState(false)
  const secure = location.protocol === 'https:'

  useEffect(() => {
    if (initial.params.has('code')) history.replaceState(null, '', `/#${initial.page}`)
    const change = () => {
      setPage(route().page)
      setError('')
      setMessage('')
      setPassword('')
    }
    window.addEventListener('hashchange', change)
    return () => window.removeEventListener('hashchange', change)
  }, [initial])

  useEffect(() => {
    let active = true
    async function refresh() {
      if (!secure) {
        if (active) setLoading(false)
        return
      }
      try {
        const profile = await accountRequest<Account>('me')
        if (active) {
          setAccount(profile)
          setError('')
        }
      } catch (failure) {
        if (active) {
          setAccount(null)
          if (!(failure instanceof AccountError && failure.status === 401))
            setError('Impossible de vérifier votre session. Réessayez.')
        }
      } finally {
        if (active) setLoading(false)
      }
    }
    void refresh()
    window.addEventListener('focus', refresh)
    const interval = window.setInterval(refresh, 60_000)
    return () => {
      active = false
      window.removeEventListener('focus', refresh)
      clearInterval(interval)
    }
  }, [secure])

  async function enterLocal() {
    setError('')
    try {
      const response = await fetch('/local-visuals/status', {
        headers: { 'X-SocialFlow-Local': '1' },
      })
      const status = (await response.json()) as { enabled?: boolean }
      if (!response.ok || !status.enabled) throw new Error()
      setLocalAdministration(true)
      setLocal(true)
    } catch {
      setError('Administration locale indisponible. Utilisez le lanceur local configuré.')
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError('')
    setMessage('')
    try {
      const body =
        page === 'confirm-email'
          ? { email, code }
          : page === 'reset-password'
            ? { email, code, password }
            : page === 'login' || page === 'register'
              ? { email, password }
              : { email }
      await accountRequest(page, body)
      setPassword('')
      if (page === 'login') setAccount(await accountRequest<Account>('me'))
      else
        setMessage(
          page === 'confirm-email'
            ? 'Adresse confirmée. Vous pouvez vous connecter.'
            : page === 'reset-password'
              ? 'Mot de passe modifié. Connectez-vous avec votre nouveau mot de passe.'
              : 'Si cette demande est applicable, un email vous sera envoyé. Consultez aussi vos courriers indésirables.',
        )
    } catch (failure) {
      setError(
        failure instanceof AccountError
          ? failure.message
          : 'Connexion au serveur impossible. Réessayez.',
      )
    } finally {
      setBusy(false)
    }
  }

  async function logout() {
    setBusy(true)
    setError('')
    try {
      await accountRequest('logout', {})
      setAccount(null)
      location.hash = 'login'
    } catch (failure) {
      if (failure instanceof AccountError && failure.status === 401) setAccount(null)
      else setError('Déconnexion impossible. Réessayez.')
    } finally {
      setBusy(false)
    }
  }

  if (local)
    return (
      <>
        <aside className="local-admin-banner">
          Administration locale · Données historiques
          <button
            onClick={() => {
              setLocalAdministration(false)
              setLocal(false)
              location.hash = 'login'
            }}
          >
            Quitter l’administration
          </button>
        </aside>
        <App />
      </>
    )

  if (!loading && account && invitation)
    return (
      <InvitationAcceptance
        code={invitation}
        email={account.email}
        logout={() => void logout()}
        done={() => {
          setInvitation(null)
          location.hash = 'workspaces'
        }}
      />
    )
  const hasPassword = ['login', 'register', 'reset-password'].includes(page)
  const tokenPage = page === 'confirm-email' || page === 'reset-password'
  if (!loading && account && !tokenPage)
    return (
      <>
        {error && <p role="alert">{error}</p>}
        <WorkspaceShell
          key={account.id}
          email={account.email}
          logout={() => void logout()}
          busy={busy}
        />
      </>
    )
  return (
    <main className="account-layout">
      {invitation && (
        <p role="status">
          Connectez-vous avec l’adresse invitée, ou créez votre compte. Après confirmation de votre
          adresse, rouvrez le lien d’invitation reçu par email.
        </p>
      )}
      <section className="account-story">
        <a className="account-brand" href="#login">
          REDEEMER<span>SocialFlow</span>
        </a>
        <div>
          <span className="account-eyebrow">VOTRE ESPACE CRÉATIF</span>
          <h1>
            Vos idées.
            <br />
            Leur prochain chapitre.
          </h1>
          <p>Un point de départ pour organiser votre présence et donner vie à vos contenus.</p>
        </div>
        <small>Une connexion sécurisée, un espace qui vous ressemble.</small>
      </section>
      <section className="account-panel" aria-label="Votre compte">
        {loading ? (
          <p role="status">Vérification de votre session…</p>
        ) : (
          <>
            <span className="account-eyebrow">REDEEMER SOCIALFLOW</span>
            <h2>{titles[page]}</h2>
            {!secure ? (
              <p>
                Pour accéder à votre compte, ouvrez{' '}
                <a
                  href={
                    import.meta.env.DEV
                      ? 'https://localhost:65474/#login'
                      : `https://${location.host}/#login`
                  }
                >
                  SocialFlow en HTTPS
                </a>
                .
              </p>
            ) : (
              <>
                <p>
                  {page === 'login'
                    ? 'Connectez-vous avec votre adresse email confirmée.'
                    : 'Votre adresse email vous permet de sécuriser votre compte.'}
                </p>
                <form onSubmit={submit}>
                  <label htmlFor="account-email">Adresse email</label>
                  <input
                    id="account-email"
                    type="email"
                    autoComplete="email"
                    required
                    maxLength={254}
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                  />
                  {hasPassword && (
                    <>
                      <label htmlFor="account-password">Mot de passe</label>
                      <input
                        id="account-password"
                        type="password"
                        required
                        maxLength={128}
                        minLength={page === 'login' ? undefined : 12}
                        autoComplete={page === 'login' ? 'current-password' : 'new-password'}
                        value={password}
                        onChange={(e) => setPassword(e.target.value)}
                        aria-describedby={page === 'login' ? undefined : 'password-policy'}
                      />
                      {page !== 'login' && (
                        <small id="password-policy">
                          Au moins 12 caractères, avec majuscule, minuscule, chiffre et symbole.
                        </small>
                      )}
                    </>
                  )}
                  {tokenPage && !code && (
                    <p role="alert">Ouvrez le lien reçu par email ou demandez un nouveau lien.</p>
                  )}
                  <button disabled={busy || (tokenPage && !code)} type="submit">
                    {busy
                      ? 'Veuillez patienter…'
                      : page === 'login'
                        ? 'Se connecter'
                        : page === 'register'
                          ? 'Créer mon compte'
                          : page === 'confirm-email'
                            ? 'Confirmer mon adresse'
                            : page === 'reset-password'
                              ? 'Enregistrer le mot de passe'
                              : 'Envoyer le lien'}
                  </button>
                </form>
                <nav className="account-links" aria-label="Accès au compte">
                  <a href="#login">Connexion</a>
                  <a href="#register">Créer un compte</a>
                  <a href="#forgot-password">Mot de passe oublié</a>
                  <a href="#resend-confirmation">Renvoyer la confirmation</a>
                </nav>
              </>
            )}
          </>
        )}
        {error && (
          <p className="account-error" role="alert">
            {error}
          </p>
        )}
        {message && (
          <p className="account-success" role="status">
            {message}
          </p>
        )}
        {import.meta.env.DEV && (
          <button className="account-local" onClick={enterLocal}>
            Administration locale
          </button>
        )}
      </section>
    </main>
  )
}
