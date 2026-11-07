import type { WatchedModel } from '../api'

export default function StatusBadge({ model }: { model: WatchedModel }) {
  if (model.status === 'running') return <span className="badge running">Hämtar…</span>
  if (model.status === 'queued') return <span className="badge queued">I kö</span>
  if (!model.isActive) return <span className="badge">Pausad</span>
  return null
}
