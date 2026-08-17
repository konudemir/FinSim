import axios from 'axios'

export const API = import.meta.env.VITE_API_URL ?? ''

const api = axios.create({ baseURL: API })

// attach the token to every outgoing request
api.interceptors.request.use((config) => {
  const token = localStorage.getItem('finsim_token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// token expired / invalid anywhere -> boot back to login
api.interceptors.response.use(
  (res) => res,
  (err) => {
    const isAuthCall = err.config?.url?.startsWith('/api/auth')
    const hadSession = !!localStorage.getItem('finsim_token')

    if (err.response?.status === 401 && hadSession && !isAuthCall) {
      localStorage.removeItem('finsim_token')
      localStorage.removeItem('finsim_expiry')
      window.location.reload()
    }
    return Promise.reject(err)
  }
)

export default api