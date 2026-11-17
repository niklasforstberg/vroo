const TOKEN_KEY = 'vroo.token'

export const getToken = () => localStorage.getItem(TOKEN_KEY)
export const setToken = (t: string | null) =>
  t ? localStorage.setItem(TOKEN_KEY, t) : localStorage.removeItem(TOKEN_KEY)

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  const token = getToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (init.body) headers.set('Content-Type', 'application/json')

  const res = await fetch(path, { ...init, headers })
  if (res.status === 401 && !path.startsWith('/api/auth/')) {
    setToken(null)
    window.location.assign('/login')
  }
  if (!res.ok) {
    let message = res.statusText
    try {
      const body = await res.json()
      message = body.error ?? body.title ?? message
    } catch { /* not JSON */ }
    throw new ApiError(res.status, message)
  }
  if (res.status === 202 || res.status === 204) return undefined as T
  return res.json()
}

export type RunStatus = 'idle' | 'queued' | 'running'

export interface ScrapeRun {
  id: number
  watchedModelId: number
  make: string
  model: string
  trigger: 'schedule' | 'manual' | 'import'
  startedAt: string
  finishedAt: string | null
  carsSeen: number
  newCars: number
  priceChanges: number
  error: string | null
}

export interface WatchedModel {
  id: number
  make: string
  model: string
  isActive: boolean
  hasImage: boolean
  status: RunStatus
  listings: number | null
  medianPrice: number | null
  statDate: string | null
  medianPrice30DaysAgo: number | null
  lastRun: ScrapeRun | null
}

export interface Me {
  email: string
  isAdmin: boolean
}

export interface AdminUser {
  id: number
  email: string
  isAdmin: boolean
  createdAt: string
  lastSeenAt: string | null
  verified: boolean
  models: number
}

export interface AdminModel {
  id: number
  make: string
  model: string
  followers: number
  activeFollowers: number
  cars: number
}

export interface DailyStat {
  date: string
  listings: number
  medianPrice: number
  medianMileageMil: number | null
}

export interface Car {
  id: number
  title: string
  url: string
  imageUrl: string | null
  registrationNumber: string | null
  year: number | null
  mileageMil: number | null
  location: string | null
  gearbox: string | null
  driveType: string | null
  price: number
  firstPrice: number
  priceChanges: number
  firstSeenAt: string
  lastSeenAt: string
  isListed: boolean
  plausiblePrice: boolean
}

export interface CarDetail {
  car: Car
  color: string | null
  bodyType: string | null
  prices: { price: number; observedAt: string }[]
}

export const api = {
  login: (email: string, password: string) =>
    apiFetch<{ token: string }>('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }),
  register: (email: string, password: string) =>
    apiFetch<void>('/api/auth/register', { method: 'POST', body: JSON.stringify({ email, password }) }),
  verifyEmail: (email: string, token: string) =>
    apiFetch<{ alreadyVerified?: boolean } | undefined>('/api/auth/verify-email', { method: 'POST', body: JSON.stringify({ email, token }) }),
  resendVerification: (email: string) =>
    apiFetch<void>('/api/auth/resend-verification', { method: 'POST', body: JSON.stringify({ email }) }),
  forgotPassword: (email: string) =>
    apiFetch<void>('/api/auth/forgot-password', { method: 'POST', body: JSON.stringify({ email }) }),
  resetPassword: (email: string, token: string, newPassword: string) =>
    apiFetch<{ token: string }>('/api/auth/reset-password', { method: 'POST', body: JSON.stringify({ email, token, newPassword }) }),
  me: () => apiFetch<Me>('/api/me'),
  catalog: () => apiFetch<{ make: string; model: string }[]>('/api/catalog'),
  models: () => apiFetch<WatchedModel[]>('/api/models'),
  model: (id: number) => apiFetch<{ model: WatchedModel; stats: DailyStat[] }>(`/api/models/${id}`),
  cars: (id: number) => apiFetch<Car[]>(`/api/models/${id}/cars`),
  car: (id: number) => apiFetch<CarDetail>(`/api/cars/${id}`),
  runs: () => apiFetch<ScrapeRun[]>('/api/runs'),
  addModel: (make: string, model: string) =>
    apiFetch<{ id: number }>('/api/models', { method: 'POST', body: JSON.stringify({ make, model }) }),
  setActive: (id: number, isActive: boolean) =>
    apiFetch<void>(`/api/models/${id}`, { method: 'PATCH', body: JSON.stringify({ isActive }) }),
  unfollow: (id: number) => apiFetch<void>(`/api/models/${id}`, { method: 'DELETE' }),
  runModel: (id: number) => apiFetch<void>(`/api/models/${id}/run`, { method: 'POST' }),
  runAll: () => apiFetch<void>('/api/models/run-all', { method: 'POST' }),
  adminUsers: () => apiFetch<AdminUser[]>('/api/admin/users'),
  deleteUser: (id: number) => apiFetch<void>(`/api/admin/users/${id}`, { method: 'DELETE' }),
  adminModels: () => apiFetch<AdminModel[]>('/api/admin/models'),
  deleteModel: (id: number) => apiFetch<void>(`/api/admin/models/${id}`, { method: 'DELETE' }),
}
