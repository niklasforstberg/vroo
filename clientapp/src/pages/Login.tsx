import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api, ApiError, setToken } from '../api'

function AuthCard({ children, onSubmit }: { children: React.ReactNode; onSubmit?: (e: React.FormEvent) => void }) {
  return (
    <div className="login">
      <form className="card login-card" onSubmit={e => { e.preventDefault(); onSubmit?.(e) }}>
        <Link to="/login" className="brand big">vroo</Link>
        {children}
      </form>
    </div>
  )
}

const tooMany = (err: unknown) => err instanceof ApiError && err.status === 429
const message = (err: unknown, fallback: string) =>
  tooMany(err) ? 'För många försök. Vänta en minut.' : err instanceof ApiError && err.status < 500 ? err.message : fallback

export default function Login() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [unverified, setUnverified] = useState(false)
  const [resent, setResent] = useState(false)
  const [busy, setBusy] = useState(false)

  async function submit() {
    setBusy(true)
    setError(null)
    setUnverified(false)
    try {
      const { token } = await api.login(email, password)
      setToken(token)
      navigate('/', { replace: true })
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) setUnverified(true)
      else setError(err instanceof ApiError && err.status === 401 ? 'Fel e-post eller lösenord.' : message(err, 'Kunde inte logga in.'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthCard onSubmit={submit}>
      {params.has('verified')
        ? <p>E-postadressen är bekräftad. Logga in med lösenordet du valde.</p>
        : <p className="muted">Bevakning av begagnatpriser på bytbil.com</p>}
      <label>
        E-post
        <input type="email" autoComplete="username" value={email} onChange={e => setEmail(e.target.value)} autoFocus />
      </label>
      <label>
        Lösenord
        <input type="password" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} />
      </label>
      {error && <p className="error">{error}</p>}
      {unverified && (
        <p className="muted small">
          Du behöver bekräfta din e-postadress först. Kolla inkorgen.{' '}
          {resent ? 'Ny länk skickad.' : (
            <button type="button" className="link" onClick={() => api.resendVerification(email).then(() => setResent(true))}>
              Skicka länken igen
            </button>
          )}
        </p>
      )}
      <button className="primary" disabled={busy || !email || !password}>Logga in</button>
      <p className="muted small auth-links">
        <Link to="/register">Skapa konto</Link>
        <Link to="/forgot-password">Glömt lösenordet?</Link>
      </p>
    </AuthCard>
  )
}

export function Register() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      await api.register(email, password)
      setDone(true)
    } catch (err) {
      setError(message(err, 'Kunde inte skapa kontot.'))
    } finally {
      setBusy(false)
    }
  }

  if (done) return (
    <AuthCard>
      <p>Vi har skickat en länk till <b>{email}</b>. Klicka på den för att bekräfta adressen.</p>
      <p className="muted small"><Link to="/login">Tillbaka till inloggningen</Link></p>
    </AuthCard>
  )

  return (
    <AuthCard onSubmit={submit}>
      <p className="muted">Skapa ett konto</p>
      <label>
        E-post
        <input type="email" autoComplete="username" value={email} onChange={e => setEmail(e.target.value)} autoFocus />
      </label>
      <label>
        Lösenord (minst 8 tecken)
        <input type="password" autoComplete="new-password" value={password} onChange={e => setPassword(e.target.value)} />
      </label>
      {error && <p className="error">{error}</p>}
      <button className="primary" disabled={busy || !email || password.length < 8}>Skapa konto</button>
      <p className="muted small"><Link to="/login">Har du redan ett konto? Logga in</Link></p>
    </AuthCard>
  )
}

export function VerifyEmail() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [error, setError] = useState<string | null>(null)
  const started = useRef(false)

  useEffect(() => {
    if (started.current) return // StrictMode runs effects twice; the token works once
    started.current = true
    api.verifyEmail(params.get('email') ?? '', params.get('token') ?? '')
      .then(() => navigate('/login?verified', { replace: true }))
      .catch(err => setError(message(err, 'Kunde inte bekräfta adressen.')))
  }, [params, navigate])

  return (
    <AuthCard>
      {error
        ? <>
            <p className="error">{error}</p>
            <p className="muted small">Logga in för att få en ny länk. <Link to="/login">Till inloggningen</Link></p>
          </>
        : <p className="muted">Bekräftar…</p>}
    </AuthCard>
  )
}

export function ForgotPassword() {
  const [email, setEmail] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      await api.forgotPassword(email)
      setDone(true)
    } catch (err) {
      setError(message(err, 'Något gick fel.'))
    } finally {
      setBusy(false)
    }
  }

  if (done) return (
    <AuthCard>
      <p>Om det finns ett konto för <b>{email}</b> har vi skickat en länk för att välja nytt lösenord. Den gäller i en timme.</p>
      <p className="muted small"><Link to="/login">Tillbaka till inloggningen</Link></p>
    </AuthCard>
  )

  return (
    <AuthCard onSubmit={submit}>
      <p className="muted">Glömt lösenordet? Ange din e-post så skickar vi en länk.</p>
      <label>
        E-post
        <input type="email" autoComplete="username" value={email} onChange={e => setEmail(e.target.value)} autoFocus />
      </label>
      {error && <p className="error">{error}</p>}
      <button className="primary" disabled={busy || !email}>Skicka länk</button>
      <p className="muted small"><Link to="/login">Tillbaka till inloggningen</Link></p>
    </AuthCard>
  )
}

export function ResetPassword() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      const { token } = await api.resetPassword(params.get('email') ?? '', params.get('token') ?? '', password)
      setToken(token)
      navigate('/', { replace: true })
    } catch (err) {
      setError(message(err, 'Kunde inte byta lösenord.'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthCard onSubmit={submit}>
      <p className="muted">Välj ett nytt lösenord för {params.get('email')}</p>
      <label>
        Nytt lösenord (minst 8 tecken)
        <input type="password" autoComplete="new-password" value={password} onChange={e => setPassword(e.target.value)} autoFocus />
      </label>
      {error && <p className="error">{error}</p>}
      <button className="primary" disabled={busy || password.length < 8}>Spara och logga in</button>
    </AuthCard>
  )
}
