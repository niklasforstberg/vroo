import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, setToken } from '../api'

export default function Layout() {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const me = useQuery({ queryKey: ['me'], queryFn: api.me, staleTime: Infinity })
  return (
    <>
      <header className="topbar">
        <div className="wrap topbar-inner">
          <NavLink to="/" className="brand">vroo</NavLink>
          <nav>
            <NavLink to="/" end>Bevakningar</NavLink>
            {me.data?.isAdmin && <NavLink to="/runs">Körningar</NavLink>}
            {me.data?.isAdmin && <NavLink to="/admin">Admin</NavLink>}
          </nav>
          <span className="muted small">{me.data?.email}</span>
          <button className="link" onClick={() => { setToken(null); qc.clear(); navigate('/login') }}>Logga ut</button>
        </div>
      </header>
      <main className="wrap">
        <Outlet />
      </main>
    </>
  )
}
