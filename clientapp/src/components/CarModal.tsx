import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api'
import { date, kkr, kr, num } from '../format'
import { TimeSeriesChart } from './charts'

export default function CarModal({ id, onClose }: { id: number; onClose: () => void }) {
  const q = useQuery({ queryKey: ['car', id], queryFn: () => api.car(id) })

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const d = q.data
  // Extend the step line to the last sighting so a long-unchanged price reads as a plateau.
  const series = d ? [
    ...d.prices.map(p => ({ date: p.observedAt, price: p.price })),
    { date: d.car.lastSeenAt, price: d.car.price },
  ] : []

  return (
    <div className="overlay" onClick={onClose}>
      <div className="card modal" onClick={e => e.stopPropagation()} role="dialog" aria-modal="true">
        <button className="link close" onClick={onClose} aria-label="Stäng">✕</button>
        {!d ? <p className="muted">Laddar…</p> : (
          <>
            <div className="modal-head">
              {d.car.imageUrl && <img src={d.car.imageUrl} alt="" />}
              <div>
                <h2>{d.car.title}</h2>
                <div className="stat-value">{kr(d.car.price)}</div>
                {d.car.price !== d.car.firstPrice && <div className="muted small">Först {kr(d.car.firstPrice)}</div>}
                <p>
                  <a href={d.car.url} target="_blank" rel="noreferrer">
                    {d.car.isListed ? 'Öppna på bytbil ↗' : 'Annonsen (troligen borttagen) ↗'}
                  </a>
                </p>
              </div>
            </div>

            <dl className="facts">
              <dt>Årsmodell</dt><dd>{d.car.year ?? '–'}</dd>
              <dt>Miltal</dt><dd>{d.car.mileageMil != null ? `${num(d.car.mileageMil)} mil` : '–'}</dd>
              <dt>Regnr</dt><dd>{d.car.registrationNumber ?? '–'}</dd>
              <dt>Ort</dt><dd>{d.car.location ?? '–'}</dd>
              <dt>Växellåda</dt><dd>{d.car.gearbox ?? '–'}</dd>
              <dt>Drivning</dt><dd>{d.car.driveType ?? '–'}</dd>
              <dt>Färg</dt><dd>{d.color ?? '–'}</dd>
              <dt>Kaross</dt><dd>{d.bodyType ?? '–'}</dd>
              <dt>Först sedd</dt><dd>{date(d.car.firstSeenAt)}</dd>
              <dt>Senast sedd</dt><dd>{date(d.car.lastSeenAt)}</dd>
            </dl>

            <h3>Prishistorik</h3>
            {d.prices.length > 1 ? (
              <>
                <TimeSeriesChart data={series} value={s => s.price} format={kr} axisFormat={kkr} label="Pris" step />
                <table className="compact">
                  <tbody>
                    {d.prices.map((p, i) => (
                      <tr key={i}>
                        <td>{date(p.observedAt)}</td>
                        <td className="num">{kr(p.price)}</td>
                        <td className={`num ${i && p.price < d.prices[i - 1].price ? 'down' : i ? 'up' : 'muted'}`}>
                          {i ? `${p.price > d.prices[i - 1].price ? '+' : ''}${num(p.price - d.prices[i - 1].price)} kr` : 'första pris'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </>
            ) : <p className="muted">Priset har inte ändrats sedan {date(d.car.firstSeenAt)}.</p>}
          </>
        )}
      </div>
    </div>
  )
}
