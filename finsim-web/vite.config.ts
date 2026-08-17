import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      '/api':  { target: 'http://localhost:5209', changeOrigin: true },
      '/hubs': { target: 'http://localhost:5209', ws: true },
    },
  }
})