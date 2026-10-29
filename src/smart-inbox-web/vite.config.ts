import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const api = env.VITE_API_PROXY ?? 'http://localhost:5080'

  return {
    plugins: [vue()],
    build: {
      rollupOptions: {
        output: {
          // Libraries change less often than app code: separate files that browsers can cache across deploys.
          manualChunks(id) {
            if (!id.includes('node_modules')) return
            if (id.includes('@primeuix')) return 'primevue-theme'
            if (id.includes('primevue')) return 'primevue'
            if (id.includes('@microsoft/signalr')) return 'signalr'
            if (/node_modules\/(@vue|vue|vue-router|vue-i18n|@intlify|pinia)\//.test(id.replaceAll('\\', '/'))) return 'vue'
          },
        },
      },
    },
    server: {
      proxy: {
        '/api': api,
        '/hubs': { target: api, ws: true },
      },
    },
  }
})
