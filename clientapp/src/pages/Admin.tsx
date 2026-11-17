import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api'
import { dateTime, num } from '../format'

export default function Admin() {
  const qc = useQueryClient()
  const users = useQuery({ queryKey: ['admin-users'], queryFn: api.adminUsers })
  const models = useQuery({ queryKey: ['admin-models'], queryFn: api.adminModels })
  const deleteUser = useMutation({
    mutationFn: api.deleteUser,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-users'] }),
  })
  const deleteModel = useMutation({
    mutationFn: api.deleteModel,
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['admin-models'] }); qc.invalidateQueries({ queryKey: ['models'] }) },
  })

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Admin</h1>
          <p className="muted">Användare och alla bevakade modeller.</p>
        </div>
      </div>

      <section className="card">
        <h2>Användare</h2>
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>E-post</th>
                <th>Skapad</th>
                <th>Senast inloggad</th>
                <th className="num">Modeller</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {users.data?.map(u => (
                <tr key={u.id} className="static">
                  <td>
                    {u.email || <i className="muted">(väntar på Auth:AdminEmail)</i>}
                    {u.isAdmin && <>{' '}<span className="badge">admin</span></>}
                    {!u.verified && <span className="muted small"> · ej bekräftad</span>}
                  </td>
                  <td>{dateTime(u.createdAt)}</td>
                  <td>{u.lastSeenAt ? dateTime(u.lastSeenAt) : '–'}</td>
                  <td className="num">{num(u.models)}</td>
                  <td className="num">
                    {!u.isAdmin && (
                      <button
                        className="link danger"
                        onClick={() => window.confirm(`Ta bort ${u.email}? Användarens bevakningar försvinner, men modellernas historik finns kvar.`) && deleteUser.mutate(u.id)}
                      >
                        Ta bort
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {deleteUser.error && <p className="error small">{(deleteUser.error as Error).message}</p>}
        </div>
      </section>

      <section className="card">
        <h2>Bevakade modeller</h2>
        <p className="muted small">En modell hämtas varje natt så länge minst en följare har den aktiv. Utan följare ligger historiken kvar.</p>
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Modell</th>
                <th className="num">Följare</th>
                <th className="num">Aktiva</th>
                <th className="num">Bilar</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {models.data?.map(m => (
                <tr key={m.id} className="static">
                  <td><Link to={`/models/${m.id}`}>{m.make} {m.model}</Link></td>
                  <td className="num">{num(m.followers)}</td>
                  <td className="num">{num(m.activeFollowers)}</td>
                  <td className="num">{num(m.cars)}</td>
                  <td className="num">
                    <button
                      className="link danger"
                      onClick={() => window.confirm(`Radera ${m.make} ${m.model} och all dess historik för alla användare?`) && deleteModel.mutate(m.id)}
                    >
                      Radera
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {deleteModel.error && <p className="error small">{(deleteModel.error as Error).message}</p>}
        </div>
      </section>
    </>
  )
}
