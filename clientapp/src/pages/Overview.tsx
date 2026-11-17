import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, type WatchedModel } from '../api'
import { ago, kr, num, pct, signedPct } from '../format'
import StatusBadge from '../components/StatusBadge'

export default function Overview() {
  const qc = useQueryClient()
  const models = useQuery({
    queryKey: ['models'],
    queryFn: api.models,
    // Poll while something is being fetched so the status and numbers update by themselves.
    refetchInterval: q => (q.state.data?.some(m => m.status !== 'idle') ? 4000 : false),
  })
  const invalidate = () => qc.invalidateQueries({ queryKey: ['models'] })
  const me = useQuery({ queryKey: ['me'], queryFn: api.me, staleTime: Infinity })
  const runAll = useMutation({ mutationFn: api.runAll, onSuccess: invalidate })
  const anyActive = models.data?.some(m => m.isActive)

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Bevakningar</h1>
          <p className="muted">Aktiva modeller hämtas från bytbil varje natt kl 03.</p>
        </div>
        {me.data?.isAdmin && (
          <button onClick={() => runAll.mutate()} disabled={!anyActive || runAll.isPending}>Hämta alla nu</button>
        )}
      </div>

      <AddModelForm onAdded={invalidate} />

      {models.isLoading && <p className="muted">Laddar…</p>}
      {models.error && <p className="error">{(models.error as Error).message}</p>}
      {models.data?.length === 0 && (
        <div className="card empty">Inga bevakningar än. Lägg till ett märke och en modell ovan.</div>
      )}

      <div className="model-grid">
        {models.data?.map(m => <ModelCard key={m.id} m={m} onChange={invalidate} />)}
      </div>
    </>
  )
}

function AddModelForm({ onAdded }: { onAdded: () => void }) {
  const [make, setMake] = useState('')
  const [model, setModel] = useState('')
  // Models others already watch, as suggestions; picking one gives the full history right away.
  const catalog = useQuery({ queryKey: ['catalog'], queryFn: api.catalog })
  const makes = [...new Set(catalog.data?.map(c => c.make))]
  const models = catalog.data?.filter(c => c.make.toLowerCase() === make.trim().toLowerCase()).map(c => c.model) ?? []
  const add = useMutation({
    mutationFn: () => api.addModel(make, model),
    onSuccess: () => { setMake(''); setModel(''); onAdded(); catalog.refetch() },
  })

  return (
    <form className="card add-form" onSubmit={e => { e.preventDefault(); add.mutate() }}>
      <label>
        Märke
        <input value={make} onChange={e => setMake(e.target.value)} placeholder="Tesla" list="catalog-makes" />
        <datalist id="catalog-makes">{makes.map(m => <option key={m} value={m} />)}</datalist>
      </label>
      <label>
        Modell
        <input value={model} onChange={e => setModel(e.target.value)} placeholder="Model Y" list="catalog-models" />
        <datalist id="catalog-models">{models.map(m => <option key={m} value={m} />)}</datalist>
      </label>
      <button className="primary" disabled={!make.trim() || !model.trim() || add.isPending}>Lägg till</button>
      <p className="hint">Stava som i bytbils filter, t.ex. <i>Mercedes-Benz</i> / <i>S-Klass</i>. Modeller som någon redan bevakar föreslås och kommer med hela sin historik; nya hämtas direkt.</p>
      {add.error && <p className="error">{add.error instanceof ApiError ? add.error.message : 'Kunde inte lägga till.'}</p>}
    </form>
  )
}

function ModelCard({ m, onChange }: { m: WatchedModel; onChange: () => void }) {
  const run = useMutation({ mutationFn: () => api.runModel(m.id), onSuccess: onChange })
  const toggle = useMutation({ mutationFn: () => api.setActive(m.id, !m.isActive), onSuccess: onChange })
  const remove = useMutation({ mutationFn: () => api.unfollow(m.id), onSuccess: onChange })
  const change = pct(m.medianPrice30DaysAgo, m.medianPrice)

  return (
    <div className={`card model-card${m.isActive ? '' : ' paused'}`}>
      <div className="model-card-head">
        <Link to={`/models/${m.id}`} className="model-name">
          {m.hasImage ? <img src={`/api/models/${m.id}/image`} alt="" /> : <span className="noimg" />}
          <span><span className="make">{m.make}</span> {m.model}</span>
        </Link>
        <StatusBadge model={m} />
      </div>

      <Link to={`/models/${m.id}`} className="stats">
        <div>
          <div className="stat-label">Medianpris</div>
          <div className="stat-value">{kr(m.medianPrice)}</div>
          {change != null && (
            <div className={`delta ${change < 0 ? 'down' : change > 0 ? 'up' : ''}`}>{signedPct(change)} på 30 dagar</div>
          )}
        </div>
        <div>
          <div className="stat-label">Till salu</div>
          <div className="stat-value">{num(m.listings)}</div>
        </div>
      </Link>

      <div className="model-card-foot">
        <span className="muted small">
          {m.lastRun
            ? m.lastRun.error
              ? <span className="error">Senaste körning misslyckades: {m.lastRun.error}</span>
              : <>Hämtad {ago(m.lastRun.finishedAt ?? m.lastRun.startedAt)}
                  {m.lastRun.trigger !== 'import' && <> · {m.lastRun.newCars} nya · {m.lastRun.priceChanges} prisändringar</>}</>
            : 'Aldrig hämtad'}
        </span>
        <span className="actions">
          <button className="link" onClick={() => run.mutate()} disabled={m.status !== 'idle'}>Hämta nu</button>
          <button className="link" onClick={() => toggle.mutate()}>{m.isActive ? 'Pausa' : 'Aktivera'}</button>
          <button
            className="link danger"
            onClick={() => window.confirm(`Sluta bevaka ${m.make} ${m.model}?`) && remove.mutate()}
          >
            Sluta bevaka
          </button>
        </span>
      </div>
      {remove.error && <p className="error small">{(remove.error as Error).message}</p>}
    </div>
  )
}
