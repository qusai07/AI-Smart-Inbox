export type DocumentStatus = 'Uploaded' | 'Processing' | 'Analyzed' | 'Routed' | 'NeedsReview' | 'Failed'
export type Priority = 'Low' | 'Normal' | 'High' | 'Urgent'
export type Category = 'Request' | 'Complaint' | 'Invitation' | 'Circular' | 'Invoice' | 'Legal' | 'Other'

export type HandlingStatus = 'New' | 'Acknowledged' | 'Closed'

/** How a document came in. */
export type DocumentSource = 'Upload' | 'Email' | 'Api'

export const STATUSES: DocumentStatus[] = ['Processing', 'Analyzed', 'NeedsReview', 'Routed', 'Failed']

export interface DocumentEvent {
  type:
    | 'Uploaded' | 'Analyzed' | 'AnalysisFailed' | 'Routed' | 'Acknowledged' | 'Closed' | 'Reopened' | 'EmailSent' | 'EmailFailed' | 'Escalated' | 'Assigned'
    | 'DraftRequested' | 'DraftReady' | 'DraftFailed' | 'DraftEdited' | 'DraftApproved' | 'DraftRejected' | 'OutgoingIssued'
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
  signatoryNameEn: string | null
  signatoryNameAr: string | null
  signatoryTitleEn: string | null
  signatoryTitleAr: string | null
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
  assigneeId: string | null
  assigneeName: string | null
  source: DocumentSource
}

export interface Comment {
  id: number
  authorId: string | null
  authorName: string
  text: string
  createdAt: string
}

export interface Member {
  id: string
  displayName: string
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
  assignee: { id: string; name: string } | null
  comments: Comment[]
  source: DocumentSource
  /** The email it arrived in (sender, subject, covering message), when source is Email. */
  sourceEmail: SourceEmail | null
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
  mine?: boolean | null
  q?: string
  source?: DocumentSource | null
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

export interface ApiKeyRow {
  id: number
  name: string
  prefix: string
  role: Role
  departmentId: number | null
  createdBy: string | null
  createdAt: string
  expiresAt: string | null
  lastUsedAt: string | null
  revokedAt: string | null
}

export type DeliveryStatus = 'Pending' | 'Delivered' | 'Failed'

export interface WebhookEndpoint {
  id: number
  url: string
  description: string | null
  events: string[]
  departmentId: number | null
  enabled: boolean
  consecutiveFailures: number
  disabledReason: string | null
  createdBy: string | null
  createdAt: string
  rotationInProgress: boolean
  lastDelivery: { status: DeliveryStatus; statusCode: number | null; at: string } | null
  deliveries7d: number
  successRate7d: number | null
}

export interface WebhookDelivery {
  id: number
  eventId: string
  eventType: string
  status: DeliveryStatus
  attempts: number
  nextAttemptAt: string | null
  lastStatusCode: number | null
  lastError: string | null
  lastResponseSnippet: string | null
  durationMs: number | null
  createdAt: string
  deliveredAt: string | null
  payload: string
}

export interface WebhookEndpointBody {
  url: string
  description: string | null
  events: string[] | null
  departmentId: number | null
  enabled: boolean
}

export interface SourceEmail {
  id: string
  fromName: string | null
  fromAddress: string | null
  subject: string | null
  sentAt: string | null
  bodyText: string | null
  skippedAttachments: string[]
}

export type InboundEmailStatus = 'Imported' | 'Skipped' | 'Failed'

export interface InboundEmail {
  id: string
  messageId: string
  fromName: string | null
  fromAddress: string | null
  subject: string | null
  sentAt: string | null
  receivedAt: string
  status: InboundEmailStatus
  reason: string | null
  attempts: number
  documentIds: string[]
  skippedAttachments: string[]
}

export interface MailboxRunResult {
  imported: number
  skipped: number
  failed: number
  alreadyKnown: number
}

export interface MailboxStatus {
  enabled: boolean
  configured: boolean
  address: string
  host: string | null
  folder: string
  processedFolder: string | null
  pollSeconds: number
  running: boolean
  lastCheckAt: string | null
  nextCheckAt: string | null
  lastResult: MailboxRunResult | null
  lastError: string | null
}

export type DraftStatus = 'Requested' | 'Generating' | 'Ready' | 'Approved' | 'Rejected' | 'Failed'

export interface ReplyDraft {
  id: string
  documentId: string
  version: number
  isLatest: boolean
  status: DraftStatus
  templateKey: string | null
  engine: string | null
  instructions: string | null
  subjectEn: string | null
  bodyEn: string | null
  subjectAr: string | null
  bodyAr: string | null
  missingInfo: string[]
  commitments: string[]
  placeholders: number
  requestedBy: string | null
  createdAt: string
  editedBy: string | null
  editedAt: string | null
  decidedBy: string | null
  decidedAt: string | null
  error: string | null
}

export interface ReplyTemplate {
  key: string
  nameEn: string
  nameAr: string
  categories: Category[]
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

  members: (departmentId: number) => request<Member[]>(`/api/departments/${departmentId}/members`),
  assign: (id: string, userId: string | null) => request<void>(`/api/documents/${id}/assign`, json('POST', { userId })),
  comment: (id: string, text: string) => request<Comment>(`/api/documents/${id}/comments`, json('POST', { text })),

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
  setDepartmentSignatory: (id: number, body: { nameEn: string | null; nameAr: string | null; titleEn: string | null; titleAr: string | null }) =>
    request<void>(`/api/departments/${id}/signatory`, json('PUT', body)),
  setDepartmentContacts: (id: number, email: string | null, headEmail: string | null) =>
    request<void>(`/api/departments/${id}/contacts`, json('PUT', { email, headEmail })),
  apiKeys: () => request<ApiKeyRow[]>('/api/api-keys'),
  createApiKey: (body: { name: string; role: Role; departmentId: number | null; expiresAt: string | null }) =>
    request<{ key: ApiKeyRow; secret: string }>('/api/api-keys', json('POST', body)),
  revokeApiKey: (id: number) => request<void>(`/api/api-keys/${id}`, { method: 'DELETE' }),
  sourceEmailUrl: (documentId: string) => `/api/documents/${documentId}/source-email`,
  mailboxStatus: () => request<MailboxStatus>('/api/mailbox/status'),
  mailboxTest: () => request<{ ok: boolean; pending: number | null; message: string }>('/api/mailbox/test', { method: 'POST' }),
  mailboxCheck: () => request<MailboxRunResult>('/api/mailbox/check', { method: 'POST' }),
  mailboxImports: (page: number, status: InboundEmailStatus | null) =>
    request<Paged<InboundEmail>>(`/api/mailbox/imports?page=${page}${status ? `&status=${status}` : ''}`),
  mailboxRetry: (id: string) => request<void>(`/api/mailbox/imports/${id}/retry`, { method: 'POST' }),
  replyTemplates: () => request<ReplyTemplate[]>('/api/reply-templates'),
  drafts: (documentId: string) => request<ReplyDraft[]>(`/api/documents/${documentId}/drafts`),
  requestDraft: (documentId: string, templateKey: string | null, instructions: string | null) =>
    request<ReplyDraft>(`/api/documents/${documentId}/drafts`, json('POST', { templateKey, instructions })),
  updateDraft: (id: string, body: { subjectEn: string; bodyEn: string; subjectAr: string; bodyAr: string }) =>
    request<ReplyDraft>(`/api/drafts/${id}`, json('PUT', body)),
  approveDraft: (id: string) => request<ReplyDraft>(`/api/drafts/${id}/approve`, { method: 'POST' }),
  draftDocxUrl: (id: string) => `/api/drafts/${id}/docx`,
  rejectDraft: (id: string, reason: string | null) => request<ReplyDraft>(`/api/drafts/${id}/reject`, json('POST', { reason })),
  webhookEvents: () => request<string[]>('/api/webhooks/events'),
  webhooks: () => request<WebhookEndpoint[]>('/api/webhooks'),
  createWebhook: (body: WebhookEndpointBody) => request<{ endpoint: WebhookEndpoint; secret: string }>('/api/webhooks', json('POST', body)),
  updateWebhook: (id: number, body: WebhookEndpointBody) => request<void>(`/api/webhooks/${id}`, json('PUT', body)),
  deleteWebhook: (id: number) => request<void>(`/api/webhooks/${id}`, { method: 'DELETE' }),
  rotateWebhookSecret: (id: number) => request<{ endpoint: WebhookEndpoint; secret: string }>(`/api/webhooks/${id}/rotate-secret`, { method: 'POST' }),
  pingWebhook: (id: number) => request<WebhookDelivery>(`/api/webhooks/${id}/ping`, { method: 'POST' }),
  webhookDeliveries: (id: number, page: number) => request<Paged<WebhookDelivery>>(`/api/webhooks/${id}/deliveries?page=${page}`),
  redeliver: (deliveryId: number) => request<WebhookDelivery>(`/api/webhooks/deliveries/${deliveryId}/redeliver`, { method: 'POST' }),
}

function json(method: string, body: unknown): RequestInit {
  return { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
}
