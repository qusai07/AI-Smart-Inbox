import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api, type Me } from '../api'
import { startRealtime, stopRealtime } from '../realtime'

export const useAuth = defineStore('auth', () => {
  const me = ref<Me | null>(null)
  const checked = ref(false)
  const providers = ref({ local: true, entra: false })

  const role = computed(() => me.value?.role ?? null)
  const isDesk = computed(() => role.value === 'Admin' || role.value === 'Clerk')
  const isAdmin = computed(() => role.value === 'Admin')
  const isDepartment = computed(() => role.value === 'Department')
  /** Signed in, but an admin hasn't given this account a role yet (typical for a first Microsoft sign-in). */
  const isPending = computed(() => !!me.value && !role.value)

  /** Where a user lands after sign-in, based on role. */
  const home = computed(() =>
    isDepartment.value && me.value?.departmentId ? `/departments/${me.value.departmentId}` : isPending.value ? '/pending' : '/',
  )

  async function load() {
    try {
      me.value = await api.me()
      startRealtime()
    } catch {
      me.value = null
    } finally {
      checked.value = true
    }
    api.providers().then((p) => (providers.value = p)).catch(() => {})
  }

  async function login(email: string, password: string) {
    me.value = await api.login(email, password)
    startRealtime()
  }

  async function logout() {
    try {
      await api.logout()
    } finally {
      me.value = null
      stopRealtime()
    }
  }

  function expired() {
    me.value = null
    stopRealtime()
  }

  return { me, checked, providers, role, isDesk, isAdmin, isDepartment, isPending, home, load, login, logout, expired }
})
