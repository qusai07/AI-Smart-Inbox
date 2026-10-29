export type DocumentStatus = 'Uploaded' | 'Processing' | 'Analyzed' | 'Routed' | 'NeedsReview' | 'Failed'
export type Priority = 'Low' | 'Normal' | 'High' | 'Urgent'
export type Category = 'Request' | 'Complaint' | 'Invitation' | 'Circular' | 'Invoice' | 'Legal' | 'Other'

export type HandlingStatus = 'New' | 'Acknowledged' | 'Closed'

export const STATUSES: DocumentStatus[] = ['Processing', 'Analyzed', 'NeedsReview', 'Routed', 'Failed']

export interface DocumentEvent {
  type: 'Uploaded' | 'Analyzed' | 'AnalysisFailed' | 'Routed' | 'Acknowledged' | 'Closed' | 'Reopened' | 'EmailSent' | 'EmailFailed' | 'Escalated'
  actor: string | null
  departmentId: number | null
  note: string | null
  at: string
}

export interface DepartmentWorkload {
  departmentId: number
  new: number
  acknowledged: number
  closed: number
  overdue: number
}
export const PRIORITIES: Priority[] = ['Urgent', 'High', 'Normal', 'Low']

export interface Department {
  id: number
  nameEn: string
  nameAr: string
  description: string | null
  keywords: string | null
  email: string | null
  headEmail: string | null
}

export interface DocumentListItem {
  id: string
  fileName: string
  status: DocumentStatus
  uploadedAt: string
  summaryEn: string | null
  summaryAr: string | null
  category: Category | null
  priority: Priority | null
  sender: string | null
  deadline: string | null
  suggestedDepartmentId: number | null
  finalDepartmentId: number | null
  confidence: number | null
  handling: HandlingStatus | null
}

export interface Analysis {
  summaryEn: string | null
  summaryAr: string | null
  category: Category
  priority: Priority
  language: string | null
  sender: string | null
  recipient: string | null
  referenceNumber: string | null
  documentDate: string | null
  deadline: string | null
  suggestedDepartmentId: number | null
  confidence: number
  reasoning: string | null
  modelUsed: string | null
  tokensUsed: number
}

export interface DocumentDetail {
  id: string
  fileName: string
  contentType: string
  status: DocumentStatus
  uploadedAt: string
  extractedText: string | null
  errorMessage: string | null
  analysis: Analysis | null
  actionItems: { id: number; description: string; dueDate: string | null }[]
  routing: {
    suggestedDepartmentId: number | null
    finalDepartmentId: number
    wasOverridden: boolean
    note: string | null
    decidedBy: string | null
    decidedAt: string
  } | null
  handling: {
    departmentId: number
    status: HandlingStatus
    updatedAt: string | null
    note: string | null
    by: string | null
  } | null
  notifications: {
    departmentId: number
    kind: 'Routed' | 'Closed' | 'Digest' | 'Escalation'
    to: string
    status: 'Pending' | 'Sent' | 'Failed'
    attempts: number
    error: string | null
    createdAt: string
    sentAt: string | null
  }[]
  timeline: DocumentEvent[]
}

export interface Paged<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface CountByKey {
  key: string
  count: number
}

export interface Stats {
  totalDocuments: number
  needsReview: number
  routed: number
  failed: number
  decisions: number
  acceptedSuggestions: number
  routingAccuracy: number | null
  averageConfidence: number | null
  totalTokens: number
  byStatus: CountByKey[]
  byPriority: CountByKey[]
  byCategory: CountByKey[]
  byDepartment: CountByKey[]
}

export interface DocumentFilter {
  status?: DocumentStatus | null
  priority?: Priority | null
  department?: number | null
  assigned?: number | null
  handling?: HandlingStatus | null
  q?: string
  page?: number
  pageSize?: number
}

export type Role = 'Admin' | 'Clerk' | 'Department'

export interface Me {
  id: string
  displayName: string
  email: string | null
  role: Role | null
  departmentId: number | null
  hasPassword: boolean
  logins: string[]
}

export interface UserRow {
  id: string
  displayName: string
  email: string | null
  role: Role | null
  departmentId: number | null
  hasPassword: boolean
  logins: string[]
  disabled: boolean
  createdAt: string
  lastSignInAt: string | null
}

export class ApiError extends Error {
  status: number

  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, { credentials: 'same-origin', ...init })
  // Session expired or signed out elsewhere: let the app send the user to the login page.
  if (res.status === 401 && !url.startsWith('/api/auth/')) window.dispatchEvent(new Event('auth:expired'))
  if (!res.ok) {
    let message = `${res.status} ${res.statusText}`
    try {
      const body = await res.json()
      message = body.detail ?? body.title ?? message
    } catch {
      /* not JSON */
    }
    throw new ApiError(message, res.status)
  }
  return res.status === 204 || res.status === 202 ? (undefined as T) : res.json()
}

export const api = {
  departments: () => request<Department[]>('/api/departments'),

  list(filter: DocumentFilter) {
    const params = new URLSearchParams()
    for (const [key, value] of Object.entries(filter)) {
      if (value !== null && value !== undefined && value !== '') params.set(key, String(value))
    }
    return request<Paged<DocumentListItem>>(`/api/documents?${params}`)
  },

  get: (id: string) => request<DocumentDetail>(`/api/documents/${id}`),

  fileUrl: (id: string) => `/api/documents/${id}/file`,

  uploadFile(file: File) {
    const form = new FormData()
    form.append('file', file)
    return request<{ id: string }>('/api/documents', { method: 'POST', body: form })
  },

  uploadText(text: string) {
    const form = new FormData()
    form.append('text', text)
    return request<{ id: string }>('/api/documents', { method: 'POST', body: form })
  },

  reanalyze: (id: string) => request<void>(`/api/documents/${id}/reanalyze`, { method: 'POST' }),

  route: (id: string, departmentId: number, note: string | null) =>
    request<void>(`/api/documents/${id}/route`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ departmentId, note }),
    }),

  stats: () => request<Stats>('/api/stats'),

  workload: () => request<DepartmentWorkload[]>('/api/departments/workload'),

  acknowledge: (id: string) => request<void>(`/api/documents/${id}/acknowledge`, { method: 'POST' }),

  close: (id: string, note: string | null) =>
    request<void>(`/api/documents/${id}/close`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ note }),
    }),

  reopen: (id: string) => request<void>(`/api/documents/${id}/reopen`, { method: 'POST' }),

  // Auth
  providers: () => request<{ local: boolean; entra: boolean }>('/api/auth/providers'),
  me: () => request<Me>('/api/auth/me'),
  login: (email: string, password: string) => request<Me>('/api/auth/login', json('POST', { email, password })),
  logout: () => request<void>('/api/auth/logout', { method: 'POST' }),
  entraLoginUrl: (returnUrl: string) => `/api/auth/entra?returnUrl=${encodeURIComponent(returnUrl)}`,

  // Admin
  users: () => request<UserRow[]>('/api/users'),
  createUser: (body: { displayName: string; email: string; password: string | null; role: Role; departmentId: number | null }) =>
    request<UserRow>('/api/users', json('POST', body)),
  updateUser: (id: string, body: { displayName: string; role: Role | null; departmentId: number | null; disabled: boolean }) =>
    request<UserRow>(`/api/users/${id}`, json('PUT', body)),
  setPassword: (id: string, password: string) => request<void>(`/api/users/${id}/password`, json('POST', { password })),
  setDepartmentContacts: (id: number, email: string | null, headEmail: string | null) =>
    request<void>(`/api/departments/${id}/contacts`, json('PUT', { email, headEmail })),
}

function json(method: string, body: unknown): RequestInit {
  return { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
}
