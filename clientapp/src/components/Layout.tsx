import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { setToken } from '../api'

export default function Layout() {
  const navigate = useNavigate()
  return (
    <>
      <header className="topbar">
        <div className="wrap topbar-inner">
          <NavLink to="/" className="brand">vroo</NavLink>
          <nav>
            <NavLink to="/" end>Bevakningar</NavLink>
            <NavLink to="/runs">Körningar</NavLink>
          </nav>
          <button className="link" onClick={() => { setToken(null); navigate('/login') }}>Logga ut</button>
        </div>
      </header>
      <main className="wrap">
        <Outlet />
      </main>
    </>
  )
}
