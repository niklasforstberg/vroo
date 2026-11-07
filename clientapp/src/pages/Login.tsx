import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, ApiError, setToken } from '../api'

export default function Login() {
  const navigate = useNavigate()
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const { token } = await api.login(password)
      setToken(token)
      navigate('/', { replace: true })
    } catch (err) {
      setError(err instanceof ApiError && err.status === 401 ? 'Fel lösenord.'
        : err instanceof ApiError && err.status === 429 ? 'För många försök. Vänta en minut.'
        : 'Kunde inte logga in.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login">
      <form className="card login-card" onSubmit={submit}>
        <div className="brand big">vroo</div>
        <p className="muted">Bevakning av begagnatpriser på bytbil.com</p>
        <label>
          Lösenord
          <input type="password" value={password} onChange={e => setPassword(e.target.value)} autoFocus />
        </label>
        {error && <p className="error">{error}</p>}
        <button className="primary" disabled={busy || !password}>Logga in</button>
      </form>
    </div>
  )
}
