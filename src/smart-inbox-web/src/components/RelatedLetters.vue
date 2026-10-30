<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import { api, type RelatedLetter } from '../api'
import { useDepartmentName } from '../stores/departments'
import { formatDate } from './Badges'

const props = defineProps<{ documentId: string; items: RelatedLetter[] }>()
const emit = defineEmits<{ changed: [] }>()

const { t, locale } = useI18n()
const toast = useToast()
const departmentName = useDepartmentName()
const removing = ref<number | null>(null)

const ICONS: Record<RelatedLetter['kind'], string> = {
  Duplicate: 'pi pi-copy',
  RepliesToOurLetter: 'pi pi-reply',
  EmailThread: 'pi pi-envelope',
  FollowUp: 'pi pi-link',
}

async function remove(item: RelatedLetter) {
  removing.value = item.linkId
  try {
    await api.removeRelated(props.documentId, item.linkId)
    toast.add({ severity: 'success', summary: t('related.removed'), life: 2500 })
    emit('changed')
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    removing.value = null
  }
}
</script>

<template>
  <section class="card card-pad related">
    <h3 class="eyebrow"><i class="pi pi-link" /> {{ t('related.title') }} <span class="faint">· {{ items.length }}</span></h3>
    <ul>
      <li v-for="r in items" :key="r.linkId" :class="`k-${r.kind}`">
        <span class="icon" :title="t(`related.kind.${r.kind}`)"><i :class="ICONS[r.kind]" /></span>
        <div class="grow">
          <div class="line1">
            <strong class="kind">{{ t(`related.kind.${r.kind}`) }}</strong>
            <span v-if="r.documentId" class="faint small">· {{ r.earlier ? t('related.earlier') : t('related.later') }}</span>
          </div>
          <RouterLink v-if="r.documentId" :to="{ name: 'document', params: { id: r.documentId } }" class="target" dir="auto">
            {{ r.sender ?? r.fileName }}
          </RouterLink>
          <div v-if="r.documentId" class="small muted">
            {{ formatDate(r.uploadedAt, locale) }}
            · <template v-if="r.departmentId">{{ departmentName(r.departmentId) }}</template><template v-else>{{ t('related.notRouted') }}</template>
            <template v-if="r.handling"> · {{ t(`handling.${r.handling}`) }}</template>
          </div>
          <RouterLink v-if="r.outgoingNumber" :to="{ name: 'register', query: { q: r.outgoingNumber } }" class="small number ltr">
            <i class="pi pi-hashtag" /> {{ r.outgoingNumber }}
          </RouterLink>
          <div class="small faint why" dir="auto">{{ r.evidence }}</div>
        </div>
        <Button
          icon="pi pi-times"
          size="small"
          text
          severity="secondary"
          :title="t('related.remove')"
          :aria-label="t('related.remove')"
          :loading="removing === r.linkId"
          @click="remove(r)"
        />
      </li>
    </ul>
    <p class="small faint hint">{{ t('related.hint') }}</p>
  </section>
</template>

<style scoped>
ul {
  list-style: none;
  margin: 8px 0 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
li {
  display: flex;
  gap: 10px;
  align-items: flex-start;
}
.icon {
  flex: none;
  width: 28px;
  height: 28px;
  border-radius: 8px;
  display: grid;
  place-items: center;
  background: var(--brand-soft);
  color: var(--brand);
}
.k-Duplicate .icon {
  background: var(--high-soft);
  color: var(--high);
}
.grow {
  flex: 1;
  min-width: 0;
}
.kind {
  font-size: 0.82rem;
}
.target {
  display: block;
  font-weight: 600;
  overflow-wrap: anywhere;
}
.number {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
}
.why {
  margin-top: 2px;
}
.small {
  font-size: 0.8rem;
}
.hint {
  margin: 10px 0 0;
}
</style>
