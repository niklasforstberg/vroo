import { useEffect, useState } from 'react'
import {
  CartesianGrid, Line, LineChart, ResponsiveContainer, Scatter, ScatterChart, Tooltip, XAxis, YAxis, ZAxis,
} from 'recharts'
import { date, kkr, kr, num } from '../format'

// Recharts writes colors into SVG attributes, where CSS variables don't resolve,
// so read the theme tokens once and again whenever the OS theme flips.
function useChartColors() {
  const read = () => {
    const s = getComputedStyle(document.documentElement)
    return {
      series: s.getPropertyValue('--series').trim(),
      grid: s.getPropertyValue('--grid').trim(),
      axis: s.getPropertyValue('--text-muted').trim(),
      surface: s.getPropertyValue('--surface').trim(),
    }
  }
  const [colors, setColors] = useState(read)
  useEffect(() => {
    const mq = window.matchMedia('(prefers-color-scheme: dark)')
    const update = () => setColors(read())
    mq.addEventListener('change', update)
    return () => mq.removeEventListener('change', update)
  }, [])
  return colors
}

const axisProps = (color: string) => ({
  stroke: color, tick: { fill: color, fontSize: 12 }, tickLine: false, axisLine: false,
})

function TooltipBox({ title, rows }: { title: string; rows: [string, string][] }) {
  return (
    <div className="tooltip">
      <div className="tooltip-title">{title}</div>
      {rows.map(([k, v]) => (
        <div key={k} className="tooltip-row"><span>{k}</span><b>{v}</b></div>
      ))}
    </div>
  )
}

/** One measure over time. Single series, so no legend: the card title names it. */
export function TimeSeriesChart<T extends { date: string }>({
  data, value, format, axisFormat, label, step = false,
}: {
  data: T[]
  value: (d: T) => number
  format: (n: number) => string
  axisFormat: (n: number) => string
  label: string
  step?: boolean
}) {
  const c = useChartColors()
  const points = data.map(d => ({ t: new Date(d.date).getTime(), v: value(d) }))
  return (
    <ResponsiveContainer width="100%" height={220}>
      <LineChart data={points} margin={{ top: 8, right: 12, bottom: 0, left: 0 }}>
        <CartesianGrid stroke={c.grid} vertical={false} />
        <XAxis
          dataKey="t" type="number" scale="time" domain={['dataMin', 'dataMax']}
          tickFormatter={t => new Date(t).toLocaleDateString('sv-SE', { month: 'short', year: '2-digit' })}
          minTickGap={40} {...axisProps(c.axis)}
        />
        <YAxis width={64} tickFormatter={axisFormat} domain={['auto', 'auto']} {...axisProps(c.axis)} />
        <Tooltip
          cursor={{ stroke: c.axis, strokeDasharray: '3 3' }}
          content={({ active, payload }) => active && payload?.length ? (
            <TooltipBox
              title={date(new Date(payload[0].payload.t).toISOString())}
              rows={[[label, format(payload[0].payload.v)]]}
            />
          ) : null}
        />
        <Line
          type={step ? 'stepAfter' : 'linear'} dataKey="v" stroke={c.series} strokeWidth={2}
          dot={points.length < 40 ? { r: 3, fill: c.series, stroke: c.surface, strokeWidth: 2 } : false}
          activeDot={{ r: 5, fill: c.series, stroke: c.surface, strokeWidth: 2 }}
          isAnimationActive={false}
        />
      </LineChart>
    </ResponsiveContainer>
  )
}

export interface ScatterPoint { id: number; mileage: number; price: number; title: string; year: number | null }

/** Asking price against mileage for the cars currently for sale. */
export function PriceMileageChart({ data, onSelect }: { data: ScatterPoint[]; onSelect: (id: number) => void }) {
  const c = useChartColors()
  return (
    <ResponsiveContainer width="100%" height={280}>
      <ScatterChart margin={{ top: 8, right: 12, bottom: 0, left: 0 }}>
        <CartesianGrid stroke={c.grid} />
        <XAxis
          dataKey="mileage" type="number" name="Miltal" tickFormatter={n => num(n)}
          domain={[0, 'auto']} {...axisProps(c.axis)}
        />
        <YAxis dataKey="price" type="number" name="Pris" width={64} tickFormatter={kkr} {...axisProps(c.axis)} />
        <ZAxis range={[64, 64]} />
        <Tooltip
          cursor={false}
          content={({ active, payload }) => {
            const p = active && payload?.[0]?.payload as ScatterPoint | undefined
            return p ? (
              <TooltipBox title={p.title} rows={[
                ['Pris', kr(p.price)], ['Miltal', `${num(p.mileage)} mil`], ['Årsmodell', p.year?.toString() ?? '–'],
              ]} />
            ) : null
          }}
        />
        <Scatter
          data={data} fill={c.series} fillOpacity={0.75} stroke={c.surface} strokeWidth={2}
          onClick={p => onSelect((p as unknown as ScatterPoint).id)} style={{ cursor: 'pointer' }}
          isAnimationActive={false}
        />
      </ScatterChart>
    </ResponsiveContainer>
  )
}
