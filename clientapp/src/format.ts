const sek = new Intl.NumberFormat('sv-SE', { maximumFractionDigits: 0 })

export const kr = (n: number | null | undefined) => (n == null ? '–' : `${sek.format(n)} kr`)
export const num = (n: number | null | undefined) => (n == null ? '–' : sek.format(n))
export const kkr = (n: number) => `${sek.format(Math.round(n / 1000))} tkr`

export const date = (iso: string | null | undefined) =>
  iso ? new Date(iso).toLocaleDateString('sv-SE') : '–'

export const dateTime = (iso: string | null | undefined) =>
  iso ? new Date(iso).toLocaleString('sv-SE', { dateStyle: 'short', timeStyle: 'short' }) : '–'

export function ago(iso: string | null | undefined) {
  if (!iso) return '–'
  const mins = Math.round((Date.now() - new Date(iso).getTime()) / 60000)
  if (mins < 1) return 'nyss'
  if (mins < 60) return `${mins} min sedan`
  const h = Math.round(mins / 60)
  if (h < 24) return `${h} h sedan`
  const d = Math.round(h / 24)
  return d === 1 ? 'i går' : `${d} dagar sedan`
}

export function pct(from: number | null | undefined, to: number | null | undefined) {
  if (!from || to == null) return null
  return ((to - from) / from) * 100
}

export const signedPct = (p: number) => `${p > 0 ? '+' : ''}${p.toFixed(1).replace('.', ',')} %`

export function duration(fromIso: string, toIso: string | null) {
  if (!toIso) return '–'
  const s = Math.round((new Date(toIso).getTime() - new Date(fromIso).getTime()) / 1000)
  if (s < 60) return `${s} s`
  const m = Math.floor(s / 60)
  return m < 60 ? `${m} min` : `${Math.floor(m / 60)} h ${m % 60} min`
}
