import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const api = env.VITE_API_PROXY ?? 'http://localhost:5080'

  return {
    plugins: [vue()],
    server: {
      proxy: {
        '/api': api,
        '/hubs': { target: api, ws: true },
      },
    },
  }
})
