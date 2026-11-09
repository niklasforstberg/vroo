import { useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Car } from '../api'
import { ago, date, kkr, kr, num, pct, signedPct } from '../format'
import { CategoryBarChart, PriceMileageChart, TimeSeriesChart, type BarDatum } from '../components/charts'
import StatusBadge from '../components/StatusBadge'
import CarModal from '../components/CarModal'

const DAY = 86_400_000
const dayOf = (iso: string) => Math.floor(new Date(iso).getTime() / DAY)

const median = (xs: number[]) => {
  const s = [...xs].sort((a, b) => a - b), m = s.length >> 1
  return s.length % 2 ? s[m] : Math.round((s[m - 1] + s[m]) / 2)
}

/** Median asking price per model year, for the plausibly priced cars for sale. */
function byYear(cars: Car[]): BarDatum[] {
  const groups = new Map<number, number[]>()
  for (const c of cars) if (c.year != null) groups.set(c.year, [...groups.get(c.year) ?? [], c.price])
  return [...groups].sort(([a], [b]) => a - b).map(([year, prices]) => ({
    label: String(year), value: median(prices),
    rows: [['Medianpris', kr(median(prices))], ['Bilar', num(prices.length)],
      ['Spann', `${kkr(Math.min(...prices))} – ${kkr(Math.max(...prices))}`]],
  }))
}

// A car's time on market is only known when a run shortly before its first sighting
// didn't see it, and a run shortly after its last sighting didn't either. Cars there on
// the first run, or that vanished during a gap in the history, would give made-up numbers.
const MAX_GAP_DAYS = 3
const BUCKETS: [number, string][] = [[7, '0–7'], [14, '8–14'], [30, '15–30'], [60, '31–60'], [90, '61–90'], [180, '91–180'], [Infinity, '180+']]

function timeOnMarket(cars: Car[], runDates: string[]): { days: number[]; buckets: BarDatum[] } {
  const runs = runDates.map(dayOf).sort((a, b) => a - b)
  const days: number[] = []
  for (const c of cars) {
    if (c.isListed) continue
    const first = dayOf(c.firstSeenAt), last = dayOf(c.lastSeenAt)
    const before = runs.filter(r => r < first).at(-1), after = runs.find(r => r > last)
    if (before == null || after == null || first - before > MAX_GAP_DAYS || after - last > MAX_GAP_DAYS) continue
    days.push(last - first)
  }
  const buckets = BUCKETS.map(([max, label], i) => {
    const n = days.filter(d => d <= max && d > (i ? BUCKETS[i - 1][0] : -1)).length
    return { label, value: n, rows: [['Bilar', num(n)]] as [string, string][] }
  })
  return { days, buckets }
}

type SortKey = 'price' | 'year' | 'mileageMil' | 'firstSeenAt' | 'change'

export default function ModelPage() {
  const id = Number(useParams().id)
  const qc = useQueryClient()
  const detail = useQuery({
    queryKey: ['model', id],
    queryFn: () => api.model(id),
    refetchInterval: q => (q.state.data && q.state.data.model.status !== 'idle' ? 4000 : false),
  })
  const status = detail.data?.model.status
  const cars = useQuery({ queryKey: ['cars', id, status], queryFn: () => api.cars(id) })
  const run = useMutation({
    mutationFn: () => api.runModel(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['model', id] }),
  })

  const [showAll, setShowAll] = useState(false)
  const [search, setSearch] = useState('')
  const [sort, setSort] = useState<{ key: SortKey; desc: boolean }>({ key: 'firstSeenAt', desc: true })
  const [selected, setSelected] = useState<number | null>(null)

  const listed = useMemo(() => cars.data?.filter(c => c.isListed) ?? [], [cars.data])
  // Leasing amounts and placeholders would wreck the price range and the scatter's y-axis.
  const priced = useMemo(() => listed.filter(c => c.plausiblePrice), [listed])
  const rows = useMemo(() => {
    const q = search.trim().toLowerCase()
    const base = (showAll ? cars.data ?? [] : listed).filter(c =>
      !q || [c.title, c.location, c.registrationNumber].some(s => s?.toLowerCase().includes(q)))
    const val = (c: Car) => sort.key === 'change' ? c.price - c.firstPrice
      : sort.key === 'firstSeenAt' ? c.firstSeenAt : c[sort.key] ?? -Infinity
    return [...base].sort((a, b) => {
      const x = val(a), y = val(b)
      return (x < y ? -1 : x > y ? 1 : 0) * (sort.desc ? -1 : 1)
    })
  }, [cars.data, listed, showAll, search, sort])

  if (detail.isLoading) return <p className="muted">Laddar…</p>
  if (!detail.data) return <p className="error">Hittade inte modellen.</p>

  const { model: m, stats } = detail.data
  const change = pct(m.medianPrice30DaysAgo, m.medianPrice)
  const scatter = priced.filter(c => c.mileageMil != null).map(c => ({
    id: c.id, mileage: c.mileageMil!, price: c.price, title: c.title, year: c.year,
  }))
  const years = byYear(priced)
  const market = timeOnMarket(cars.data ?? [], stats.map(s => s.date))
  const th = (key: SortKey, label: string) => (
    <th className="num sortable" onClick={() => setSort(s => ({ key, desc: s.key === key ? !s.desc : true }))}>
      {label}{sort.key === key ? (sort.desc ? ' ↓' : ' ↑') : ''}
    </th>
  )

  return (
    <>
      <p><Link to="/" className="muted">← Bevakningar</Link></p>
      <div className="page-head">
        <div>
          <h1><span className="make">{m.make}</span> {m.model} <StatusBadge model={m} /></h1>
          <p className="muted">
            {m.lastRun ? <>Senast hämtad {ago(m.lastRun.finishedAt ?? m.lastRun.startedAt)}</> : 'Aldrig hämtad'}
            {m.lastRun?.error && <span className="error"> · {m.lastRun.error}</span>}
          </p>
        </div>
        <button onClick={() => run.mutate()} disabled={m.status !== 'idle'}>Hämta nu</button>
      </div>

      <div className="tiles">
        <div className="card tile">
          <div className="stat-label">Medianpris</div>
          <div className="stat-value">{kr(m.medianPrice)}</div>
          {change != null && <div className={`delta ${change < 0 ? 'down' : change > 0 ? 'up' : ''}`}>{signedPct(change)} på 30 dagar</div>}
        </div>
        <div className="card tile">
          <div className="stat-label">Till salu</div>
          <div className="stat-value">{num(m.listings)}</div>
        </div>
        <div className="card tile">
          <div className="stat-label">Lägsta / högsta pris</div>
          <div className="stat-value small-value">
            {priced.length ? `${kkr(Math.min(...priced.map(c => c.price)))} – ${kkr(Math.max(...priced.map(c => c.price)))}` : '–'}
          </div>
        </div>
        <div className="card tile">
          <div className="stat-label">Har sänkt priset</div>
          <div className="stat-value">{num(listed.filter(c => c.price < c.firstPrice).length)}</div>
        </div>
      </div>

      {stats.length > 1 ? (
        <div className="chart-row">
          <section className="card">
            <h2>Medianpris</h2>
            <TimeSeriesChart data={stats} value={s => s.medianPrice} format={kr} axisFormat={kkr} label="Medianpris" />
          </section>
          <section className="card">
            <h2>Antal till salu</h2>
            <TimeSeriesChart data={stats} value={s => s.listings} format={num} axisFormat={num} label="Till salu" />
          </section>
        </div>
      ) : (
        <div className="card empty">Trenddiagrammen visas när det finns data från minst två dagar.</div>
      )}

      {scatter.length > 0 && (
        <section className="card">
          <h2>Pris mot miltal <span className="muted small">· {scatter.length} bilar till salu, klicka för detaljer</span></h2>
          <PriceMileageChart data={scatter} onSelect={setSelected} />
        </section>
      )}

      <div className="chart-row">
        <section className="card">
          <h2>Medianpris per årsmodell <span className="muted small">· bilar till salu</span></h2>
          {years.length
            ? <CategoryBarChart data={years} axisFormat={kkr} title={d => `Årsmodell ${d.label}`} />
            : <p className="muted empty">Inga bilar till salu med årsmodell.</p>}
        </section>
        <section className="card">
          <h2>Liggtid {market.days.length > 0 && <span className="muted small">
            · median {num(median(market.days))} dagar, {num(market.days.length)} borttagna annonser</span>}</h2>
          {market.days.length
            ? <CategoryBarChart data={market.buckets} axisFormat={num} title={d => `${d.label} dagar`} />
            : <p className="muted empty">Visas när annonser har försvunnit under en period med dagliga körningar.</p>}
        </section>
      </div>

      <section className="card">
        <div className="table-head">
          <h2>Bilar</h2>
          <input className="search" placeholder="Sök titel, ort, regnr" value={search} onChange={e => setSearch(e.target.value)} />
          <div className="segmented">
            <button className={showAll ? '' : 'on'} onClick={() => setShowAll(false)}>Till salu ({listed.length})</button>
            <button className={showAll ? 'on' : ''} onClick={() => setShowAll(true)}>Alla ({cars.data?.length ?? 0})</button>
          </div>
        </div>
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Bil</th>
                {th('year', 'År')}
                {th('mileageMil', 'Mil')}
                {th('price', 'Pris')}
                {th('change', 'Ändring')}
                {th('firstSeenAt', 'Först sedd')}
              </tr>
            </thead>
            <tbody>
              {rows.map(c => (
                <tr key={c.id} onClick={() => setSelected(c.id)} className={c.isListed ? '' : 'gone'}>
                  <td className="car-cell">
                    {c.imageUrl ? <img src={c.imageUrl} alt="" loading="lazy" /> : <div className="noimg" />}
                    <div>
                      <div className="car-title">{c.title}</div>
                      <div className="muted small">
                        {[c.location, c.registrationNumber, c.gearbox].filter(Boolean).join(' · ')}
                        {!c.isListed && <> · såld/borta {date(c.lastSeenAt)}</>}
                      </div>
                    </div>
                  </td>
                  <td className="num">{c.year ?? '–'}</td>
                  <td className="num">{num(c.mileageMil)}</td>
                  <td className={`num ${c.plausiblePrice ? '' : 'muted'}`}
                    title={c.plausiblePrice ? undefined : 'Orimligt lågt, troligen ett månadsbelopp. Räknas inte med i statistiken.'}>
                    {kr(c.price)}{!c.plausiblePrice && ' *'}
                  </td>
                  <td className={`num ${c.price < c.firstPrice ? 'down' : c.price > c.firstPrice ? 'up' : 'muted'}`}>
                    {c.price === c.firstPrice ? '–' : signedPct(pct(c.firstPrice, c.price)!)}
                  </td>
                  <td className="num">{date(c.firstSeenAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {cars.isLoading && <p className="muted">Laddar bilar…</p>}
          {!cars.isLoading && rows.length === 0 && <p className="muted empty">Inga bilar.</p>}
        </div>
      </section>

      {selected != null && <CarModal id={selected} onClose={() => setSelected(null)} />}
    </>
  )
}
