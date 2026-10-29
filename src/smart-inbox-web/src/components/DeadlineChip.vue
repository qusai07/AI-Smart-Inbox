<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { daysUntil, formatDate } from './Badges'

const props = defineProps<{ date: string | null; done?: boolean }>()
const { t, locale } = useI18n()

const days = computed(() => (props.date ? daysUntil(props.date) : null))
const tone = computed(() => {
  if (days.value === null || props.done) return 'calm'
  if (days.value < 0) return 'overdue'
  if (days.value <= 3) return 'soon'
  return 'calm'
})
const label = computed(() => {
  if (days.value === null) return ''
  if (props.done) return formatDate(props.date, locale.value)
  if (days.value < 0) return t('deadline.overdue', { n: -days.value })
  if (days.value === 0) return t('deadline.today')
  if (days.value === 1) return t('deadline.tomorrow')
  if (days.value <= 14) return t('deadline.inDays', { n: days.value })
  return formatDate(props.date, locale.value)
})
</script>

<template>
  <span v-if="date" class="deadline" :class="tone" :title="formatDate(date, locale)">
    <i class="pi pi-clock" />{{ label }}
  </span>
</template>

<style scoped>
.deadline {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 0.8rem;
  font-weight: 500;
  color: var(--muted);
  white-space: nowrap;
}
.deadline .pi {
  font-size: 0.75rem;
}
.soon {
  color: var(--high);
  font-weight: 600;
}
.overdue {
  color: var(--urgent);
  font-weight: 600;
}
</style>
