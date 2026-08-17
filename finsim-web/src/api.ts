import axios from 'axios'

export const API = import.meta.env.VITE_API_URL ?? ''

const api = axios.create({ baseURL: API, withCredentials: true })

// session expired anywhere -> boot back to login
api.interceptors.response.use(
  (res) => res,
  (err) => {
    const isAuthCall = err.config?.url?.startsWith('/api/auth')
    if (err.response?.status === 401 && !isAuthCall) {
      window.location.reload()
    }
    return Promise.reject(err)
  }
)

export default api