import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api, type DepartmentWorkload, type DocumentStatus, type Stats } from '../api'
import { onStatusChanged } from '../realtime'

/** Shared stats for sidebar counts, inbox tabs, department pages and the dashboard. Refreshes on live updates. */
export const useStats = defineStore('stats', () => {
  const stats = ref<Stats | null>(null)
  const workload = ref<DepartmentWorkload[]>([])
  let timer: ReturnType<typeof setTimeout> | undefined

  async function load() {
    try {
      ;[stats.value, workload.value] = await Promise.all([api.stats(), api.workload()])
    } catch {
      /* keep last known numbers */
    }
  }

  function refreshSoon() {
    clearTimeout(timer)
    timer = setTimeout(load, 400)
  }

  onStatusChanged(refreshSoon)

  const countByStatus = computed(() => {
    const map: Partial<Record<DocumentStatus, number>> = {}
    for (const s of stats.value?.byStatus ?? []) map[s.key as DocumentStatus] = s.count
    return map
  })

  const workloadFor = (departmentId: number): DepartmentWorkload =>
    workload.value.find((w) => w.departmentId === departmentId) ?? { departmentId, new: 0, acknowledged: 0, closed: 0, overdue: 0 }

  return { stats, workload, load, refreshSoon, countByStatus, workloadFor }
})
