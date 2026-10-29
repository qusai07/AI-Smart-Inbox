import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { ref } from 'vue'
import type { DocumentStatus } from './api'

export interface StatusChanged {
  documentId: string
  status: DocumentStatus
}

type Listener = (e: StatusChanged) => void
const listeners = new Set<Listener>()

export const connected = ref(false)

// The hub requires sign-in; the auth cookie is sent automatically (same origin).
const connection = new HubConnectionBuilder()
  .withUrl('/hubs/inbox')
  .withAutomaticReconnect()
  .configureLogging(LogLevel.Warning)
  .build()

connection.on('DocumentStatusChanged', (e: StatusChanged) => listeners.forEach((l) => l(e)))
connection.onreconnecting(() => (connected.value = false))
connection.onreconnected(() => (connected.value = true))
connection.onclose(() => (connected.value = false))

let wanted = false
let retry: ReturnType<typeof setTimeout> | undefined

export async function startRealtime() {
  wanted = true
  clearTimeout(retry)
  if (connection.state !== HubConnectionState.Disconnected) return
  try {
    await connection.start()
    connected.value = true
  } catch {
    if (wanted) retry = setTimeout(startRealtime, 5000)
  }
}

export async function stopRealtime() {
  wanted = false
  clearTimeout(retry)
  await connection.stop()
}

/** Subscribe to status changes; returns an unsubscribe function. */
export function onStatusChanged(listener: Listener) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}
