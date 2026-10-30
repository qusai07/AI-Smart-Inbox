<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import { api, type Department } from '../api'

/** Everything about one department in one place: notifications, reply signature, outgoing-number code. */
const props = defineProps<{ department: Department | null }>()
const emit = defineEmits<{ close: []; saved: [] }>()

const { t, locale } = useI18n()
const toast = useToast()
const saving = ref(false)
const failed = ref<string | null>(null)

const form = reactive({ email: '', headEmail: '', titleEn: '', titleAr: '', nameEn: '', nameAr: '', code: '' })

watch(
  () => props.department,
  (d) => {
    if (!d) return
    Object.assign(form, {
      email: d.email ?? '', headEmail: d.headEmail ?? '',
      titleEn: d.signatoryTitleEn ?? '', titleAr: d.signatoryTitleAr ?? '', nameEn: d.signatoryNameEn ?? '', nameAr: d.signatoryNameAr ?? '',
      code: d.code ?? '',
    })
    failed.value = null
  },
  { immediate: true },
)

const clean = (s: string) => s.trim() || null
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
const errors = computed(() => ({
  email: !!form.email.trim() && !EMAIL.test(form.email.trim()),
  headEmail: !!form.headEmail.trim() && !EMAIL.test(form.headEmail.trim()),
  code: !/^[A-Za-z0-9]{0,10}$/.test(form.code.trim()),
}))
const valid = computed(() => !errors.value.email && !errors.value.headEmail && !errors.value.code)

const changes = computed(() => {
  const d = props.department
  if (!d) return { contacts: false, signature: false, code: false }
  return {
    contacts: clean(form.email) !== d.email || clean(form.headEmail) !== d.headEmail,
    signature: clean(form.titleEn) !== d.signatoryTitleEn || clean(form.titleAr) !== d.signatoryTitleAr
      || clean(form.nameEn) !== d.signatoryNameEn || clean(form.nameAr) !== d.signatoryNameAr,
    code: (clean(form.code)?.toUpperCase() ?? null) !== d.code,
  }
})
const dirty = computed(() => changes.value.contacts || changes.value.signature || changes.value.code)

/** The signature block as it is printed under replies; [●] where a person will have to fill it in. */
const preview = computed(() => ({
  en: [form.nameEn.trim(), form.titleEn.trim() || '[●]', props.department?.nameEn ?? ''].filter(Boolean),
  ar: [form.nameAr.trim(), form.titleAr.trim() || '[●]', props.department?.nameAr ?? ''].filter(Boolean),
}))

const name = computed(() => (props.department ? (locale.value === 'ar' ? props.department.nameAr : props.department.nameEn) : ''))

/** Saves only what changed. If one part fails, the others are kept and the dialog says which part didn't save. */
async function save() {
  const d = props.department
  if (!d || !valid.value) return
  saving.value = true
  failed.value = null
  const problems: string[] = []
  const c = changes.value
  if (c.contacts) {
    try {
      await api.setDepartmentContacts(d.id, clean(form.email), clean(form.headEmail))
      Object.assign(d, { email: clean(form.email), headEmail: clean(form.headEmail) })
    } catch (e) {
      problems.push(`${t('admin.parts.contacts')}: ${(e as Error).message}`)
    }
  }
  if (c.signature) {
    const body = { nameEn: clean(form.nameEn), nameAr: clean(form.nameAr), titleEn: clean(form.titleEn), titleAr: clean(form.titleAr) }
    try {
      await api.setDepartmentSignatory(d.id, body)
      Object.assign(d, { signatoryNameEn: body.nameEn, signatoryNameAr: body.nameAr, signatoryTitleEn: body.titleEn, signatoryTitleAr: body.titleAr })
    } catch (e) {
      problems.push(`${t('admin.parts.signature')}: ${(e as Error).message}`)
    }
  }
  if (c.code) {
    const code = clean(form.code)?.toUpperCase() ?? null
    try {
      await api.setDepartmentCode(d.id, code)
      d.code = code
    } catch (e) {
      problems.push(`${t('admin.parts.code')}: ${(e as Error).message}`)
    }
  }
  saving.value = false
  if (problems.length) {
    failed.value = problems.join(' · ')
    emit('saved') // what did save shows straight away
    return
  }
  toast.add({ severity: 'success', summary: t('admin.saved'), life: 2000 })
  emit('saved')
  emit('close')
}
</script>

<template>
  <Dialog
    :visible="!!department"
    modal
    :header="t('admin.editDepartment', { name })"
    :style="{ width: 'min(640px, 96vw)' }"
    :draggable="false"
    @update:visible="(v: boolean) => { if (!v) emit('close') }"
  >
    <form class="form" @submit.prevent="save">
      <fieldset>
        <legend><i class="pi pi-envelope" /> {{ t('admin.notifications') }}</legend>
        <label>
          <span>{{ t('admin.deptEmail') }} <small class="faint">· {{ t('admin.deptEmailHint') }}</small></span>
          <InputText v-model="form.email" type="email" dir="ltr" fluid :invalid="errors.email" :placeholder="t('admin.emailPlaceholder')" />
          <small v-if="errors.email" class="error">{{ t('admin.invalidEmail') }}</small>
        </label>
        <label>
          <span>{{ t('admin.headEmail') }} <small class="faint">· {{ t('admin.headEmailHint') }}</small></span>
          <InputText v-model="form.headEmail" type="email" dir="ltr" fluid :invalid="errors.headEmail" :placeholder="t('admin.headPlaceholder')" />
          <small v-if="errors.headEmail" class="error">{{ t('admin.invalidEmail') }}</small>
        </label>
      </fieldset>

      <fieldset>
        <legend><i class="pi pi-id-card" /> {{ t('admin.signature') }}</legend>
        <p class="small muted">{{ t('admin.signaturesHint') }}</p>
        <div class="pair">
          <label><span>{{ t('admin.titleEn') }}</span><InputText v-model="form.titleEn" dir="ltr" maxlength="150" fluid /></label>
          <label><span>{{ t('admin.titleAr') }}</span><InputText v-model="form.titleAr" dir="rtl" maxlength="150" fluid /></label>
          <label><span>{{ t('admin.nameEn') }} <small class="faint">· {{ t('admin.optional') }}</small></span><InputText v-model="form.nameEn" dir="ltr" maxlength="150" fluid /></label>
          <label><span>{{ t('admin.nameAr') }} <small class="faint">· {{ t('admin.optional') }}</small></span><InputText v-model="form.nameAr" dir="rtl" maxlength="150" fluid /></label>
        </div>
        <div class="preview">
          <span class="faint small">{{ t('admin.preview') }}</span>
          <div class="sig">
            <div dir="ltr"><div v-for="line in preview.en" :key="line">{{ line }}</div></div>
            <div dir="rtl" lang="ar"><div v-for="line in preview.ar" :key="line">{{ line }}</div></div>
          </div>
        </div>
      </fieldset>

      <fieldset>
        <legend><i class="pi pi-hashtag" /> {{ t('admin.code') }}</legend>
        <p class="small muted">{{ t('admin.codesHint') }}</p>
        <InputText v-model="form.code" dir="ltr" maxlength="10" class="code" placeholder="LEG" :invalid="errors.code" />
        <small v-if="errors.code" class="error">{{ t('admin.invalidCode') }}</small>
      </fieldset>

      <Message v-if="failed" severity="error" :closable="false">{{ t('admin.partialSave', { parts: failed }) }}</Message>
    </form>
    <template #footer>
      <Button :label="t('actions.cancel')" severity="secondary" text @click="emit('close')" />
      <Button :label="t('admin.save')" icon="pi pi-check" :loading="saving" :disabled="!dirty || !valid" @click="save" />
    </template>
  </Dialog>
</template>

<style scoped>
.form {
  display: flex;
  flex-direction: column;
  gap: 18px;
}
fieldset {
  border: 0;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
legend {
  font-weight: 600;
  font-size: 0.92rem;
  margin-bottom: 6px;
  display: flex;
  align-items: center;
  gap: 8px;
}
legend .pi {
  color: var(--brand);
}
label {
  display: flex;
  flex-direction: column;
  gap: 5px;
  font-size: 0.85rem;
  font-weight: 500;
}
.pair {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px 14px;
}
@media (max-width: 560px) {
  .pair {
    grid-template-columns: 1fr;
  }
}
.preview {
  border: 1px dashed var(--border);
  border-radius: 10px;
  padding: 10px 14px;
}
.sig {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  margin-top: 6px;
  font-size: 0.88rem;
}
.code {
  width: 120px;
  text-transform: uppercase;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
}
.small {
  font-size: 0.8rem;
  margin: 0;
}
.error {
  color: var(--urgent);
}
</style>
