import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type Department } from '../api'

export const useDepartments = defineStore('departments', () => {
  const items = ref<Department[]>([])
  let loading: Promise<void> | null = null

  function load() {
    loading ??= api.departments().then((d) => {
      items.value = d
    })
    return loading
  }

  const byId = computed(() => new Map(items.value.map((d) => [d.id, d])))

  return { items, byId, load }
})

/** Department name in the current UI language. */
export function useDepartmentName() {
  const store = useDepartments()
  const { locale } = useI18n()
  return (id: number | null | undefined) => {
    if (id == null) return '—'
    const d = store.byId.get(id)
    if (!d) return `#${id}`
    return locale.value === 'ar' ? d.nameAr : d.nameEn
  }
}
