<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import Textarea from 'primevue/textarea'
import { api, type HandlingStatus } from '../api'
import { useStats } from '../stores/stats'

const props = withDefaults(defineProps<{ id: string; status: HandlingStatus; size?: 'small' | 'large' }>(), { size: undefined })
const emit = defineEmits<{ changed: [] }>()

const { t } = useI18n()
const toast = useToast()
const stats = useStats()
const busy = ref(false)
const closing = ref(false)
const note = ref('')

async function run(action: () => Promise<void>, message: string) {
  busy.value = true
  try {
    await action()
    toast.add({ severity: 'success', summary: message, life: 2500 })
    stats.refreshSoon()
    emit('changed')
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    busy.value = false
  }
}

const acknowledge = () => run(() => api.acknowledge(props.id), t('handling.acknowledged'))
const reopen = () => run(() => api.reopen(props.id), t('handling.reopened'))
async function close() {
  await run(() => api.close(props.id, note.value.trim() || null), t('handling.closed'))
  closing.value = false
  note.value = ''
}
</script>

<template>
  <span class="actions" @click.stop.prevent>
    <Button v-if="status === 'New'" :label="t('handling.acknowledge')" icon="pi pi-check" :size="size" :loading="busy" @click="acknowledge" />
    <Button
      v-if="status !== 'Closed'"
      :label="t('handling.close')"
      icon="pi pi-lock"
      :severity="status === 'New' ? 'secondary' : undefined"
      :outlined="status === 'New'"
      :size="size"
      :disabled="busy"
      @click="closing = true"
    />
    <Button v-else :label="t('handling.reopen')" icon="pi pi-replay" severity="secondary" outlined :size="size" :loading="busy" @click="reopen" />
  </span>

  <Dialog v-model:visible="closing" modal :header="t('handling.closeTitle')" :style="{ width: 'min(480px, 94vw)' }" :draggable="false">
    <Textarea v-model="note" :placeholder="t('handling.closeNote')" rows="4" fluid auto-resize dir="auto" />
    <template #footer>
      <Button :label="t('actions.cancel')" severity="secondary" text @click="closing = false" />
      <Button :label="t('handling.close')" icon="pi pi-lock" :loading="busy" @click="close" />
    </template>
  </Dialog>
</template>

<style scoped>
.actions {
  display: inline-flex;
  gap: 8px;
  flex-wrap: wrap;
}
</style>
