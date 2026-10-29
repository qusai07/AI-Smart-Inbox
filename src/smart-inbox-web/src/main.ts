import { createApp } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import Aura from '@primeuix/themes/aura'
import { definePreset } from '@primeuix/themes'
import App from './App.vue'
import { router } from './router'
import { applyLocale, i18n, type Locale } from './i18n'
import { applyTheme, theme } from './theme'
import 'primeicons/primeicons.css'
import './style.css'

const Preset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '{teal.50}', 100: '{teal.100}', 200: '{teal.200}', 300: '{teal.300}', 400: '{teal.400}',
      500: '{teal.500}', 600: '{teal.600}', 700: '{teal.700}', 800: '{teal.800}', 900: '{teal.900}', 950: '{teal.950}',
    },
    colorScheme: {
      light: {
        primary: { color: '{teal.700}', hoverColor: '{teal.800}', activeColor: '{teal.900}', contrastColor: '#ffffff' },
      },
    },
  },
})

applyLocale(i18n.global.locale.value as Locale)
applyTheme(theme.value)

createApp(App)
  .use(createPinia())
  .use(router)
  .use(i18n)
  .use(PrimeVue, { theme: { preset: Preset, options: { darkModeSelector: '.app-dark', cssLayer: false } } })
  .use(ToastService)
  .mount('#app')
