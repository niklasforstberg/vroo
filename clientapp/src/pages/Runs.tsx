import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api'
import { dateTime, duration, num } from '../format'

const triggerLabel = { schedule: 'Schema', manual: 'Manuell', import: 'Import' } as const

export default function Runs() {
  const runs = useQuery({ queryKey: ['runs'], queryFn: api.runs, refetchInterval: 10_000 })

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Körningar</h1>
          <p className="muted">De senaste 100 hämtningarna från bytbil.</p>
        </div>
      </div>
      <section className="card">
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Start</th>
                <th>Modell</th>
                <th>Typ</th>
                <th className="num">Bilar</th>
                <th className="num">Nya</th>
                <th className="num">Prisändringar</th>
                <th className="num">Tid</th>
                <th>Resultat</th>
              </tr>
            </thead>
            <tbody>
              {runs.data?.map(r => (
                <tr key={r.id} className="static">
                  <td>{dateTime(r.startedAt)}</td>
                  <td><Link to={`/models/${r.watchedModelId}`}>{r.make} {r.model}</Link></td>
                  <td>{triggerLabel[r.trigger]}</td>
                  <td className="num">{num(r.carsSeen)}</td>
                  <td className="num">{r.trigger === 'import' ? '–' : num(r.newCars)}</td>
                  <td className="num">{r.trigger === 'import' ? '–' : num(r.priceChanges)}</td>
                  <td className="num">{r.trigger === 'import' ? '–' : duration(r.startedAt, r.finishedAt)}</td>
                  <td>
                    {!r.finishedAt ? <span className="badge running">Pågår</span>
                      : r.error ? <span className="error">✕ {r.error}</span>
                      : <span className="ok">✓ Klar</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {runs.data?.length === 0 && <p className="muted empty">Inga körningar än.</p>}
        </div>
      </section>
    </>
  )
}
