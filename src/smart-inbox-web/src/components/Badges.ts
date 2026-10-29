export function formatDate(value: string | null | undefined, locale: string) {
  if (!value) return '—'
  return toDate(value).toLocaleDateString(locale === 'ar' ? 'ar-AE' : 'en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
}

export function formatDateTime(value: string, locale: string) {
  return toDate(value).toLocaleString(locale === 'ar' ? 'ar-AE' : 'en-GB', {
    day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit',
  })
}

/** Whole days from today to a date-only value (negative = overdue). */
export function daysUntil(value: string) {
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  return Math.round((toDate(value).getTime() - today.getTime()) / 86_400_000)
}

/** "2 h ago", "3 days ago" — short relative time for received dates. */
export function timeAgo(value: string, locale: string) {
  const seconds = (toDate(value).getTime() - Date.now()) / 1000
  const rtf = new Intl.RelativeTimeFormat(locale === 'ar' ? 'ar' : 'en', { numeric: 'auto', style: 'short' })
  const units: [Intl.RelativeTimeFormatUnit, number][] = [['day', 86400], ['hour', 3600], ['minute', 60]]
  for (const [unit, size] of units) {
    if (Math.abs(seconds) >= size || unit === 'minute') return rtf.format(Math.round(seconds / size), unit)
  }
  return ''
}

export function toDate(value: string) {
  // Date-only values (YYYY-MM-DD) must not shift across time zones; server timestamps are UTC.
  if (value.length === 10) return new Date(`${value}T00:00:00`)
  return new Date(/[zZ]|[+-]\d\d:\d\d$/.test(value) ? value : `${value}Z`)
}
