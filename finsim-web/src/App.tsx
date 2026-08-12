import { useEffect, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import api, { API } from './api'
import { useAuth } from './auth'
import Login from './Login'

type Instrument = {
  id: string
  symbol: string
  name: string
  currentPrice: number
  isActive: boolean
}

type Balance = {
  freeCashBalance: number
  lockedCashBalance: number
  total: number
}

type Order = {
  id: string
  symbol: string
  orderType: string
  direction: string
  quantity: number
  price: number | null
  status: string
  createdAt: string
}

type PortfolioItem = {
  symbol: string
  name: string
  totalQuantity: number
  lockedQuantity: number
  averageCost: number
  currentPrice: number
  marketValue: number
  profitLoss: number
}

type PriceUpdate = {
  marketMove: number
  prices: { symbol: string; currentPrice: number }[]
}

// "42,5" -> 42.5 ; "" / "42." / "abc" -> NaN
const parseDecimal = (s: string) => parseFloat(s.replace(',', '.'))

export default function App() {
  const { loggedIn, logout } = useAuth()

  if (!loggedIn) {
    return <Login onSuccess={() => window.location.reload()} />
  }

  return <Dashboard onLogout={logout} />
}

function Dashboard({ onLogout }: { onLogout: () => void }) {
  const [instruments, setInstruments] = useState<Instrument[]>([])
  const [history, setHistory] = useState<Record<string, number[]>>({})
  const [balance, setBalance] = useState<Balance | null>(null)
  const [portfolio, setPortfolio] = useState<Record<string, PortfolioItem>>({})
  const [qty, setQty] = useState('1')
  const [marketMove, setMarketMove] = useState(0)
  const [limitOpen, setLimitOpen] = useState<string | null>(null)
  const [limitPrice, setLimitPrice] = useState('')

  const [orders, setOrders] = useState<Order[]>([])

  const loadOrders = () =>
    api.get<Order[]>('/api/order')
      .then(r => setOrders(r.data))
      .catch(console.error)

  const loadBalance = () =>
    api.get<Balance>('/api/users/balance')
      .then(r => setBalance(r.data))
      .catch(console.error)

  const loadPortfolio = () =>
    api.get<PortfolioItem[]>('/api/users/portfolio')
      .then(r => {
        const map: Record<string, PortfolioItem> = {}
        for (const p of r.data) map[p.symbol] = p
        setPortfolio(map)
      })
      .catch(console.error)

  useEffect(() => {
    api.get<Instrument[]>('/api/instruments')
      .then(res => setInstruments(res.data))
      .catch(console.error)
    loadOrders()
    loadBalance()
    loadPortfolio()
  }, [])

  useEffect(() => {
    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`${API}/hubs/prices`)
      .withAutomaticReconnect()
      .build()

    conn.on('PriceUpdate', (payload: PriceUpdate) => {
      setMarketMove(payload.marketMove)

      setInstruments(prev =>
        prev.map(i => {
          const u = payload.prices.find(x => x.symbol === i.symbol)
          return u ? { ...i, currentPrice: u.currentPrice } : i
        })
      )

      setHistory(prev => {
        const next = { ...prev }
        for (const u of payload.prices) {
          next[u.symbol] = [...(prev[u.symbol] ?? []), u.currentPrice].slice(-10)
        }
        return next
      })

      loadPortfolio()
      loadBalance()
      loadOrders()
    })

    conn.start().catch(console.error)
    return () => { conn.stop() }
  }, [])

  const sendOrder = async (instrumentId: string, direction: 'Buy' | 'Sell') => {
    const quantity = parseInt(qty, 10)
    if (!Number.isFinite(quantity) || quantity < 1) {
      alert('Geçerli bir adet gir')
      return
    }

    try {
      await api.post('/api/order/market', {
        instrumentId,
        direction,
        quantity,
      })
      loadBalance()
      loadPortfolio()
    } catch (e: any) {
      alert(e.response?.data ?? 'Hata')
    }
    loadPortfolio()
    loadBalance()
    loadOrders()
  }

  const sendLimit = async (instrumentId: string, direction: 'Buy' | 'Sell') => {
    const quantity = parseInt(qty, 10)
    if (!Number.isFinite(quantity) || quantity < 1) {
      alert('Geçerli bir adet gir')
      return
    }

    const price = parseDecimal(limitPrice)
    if (!Number.isFinite(price) || price <= 0) {
      alert('Geçerli bir fiyat gir')
      return
    }

    try {
      await api.post('/api/order/limit', {
        instrumentId,
        direction,
        quantity,
        price,
      })
      setLimitOpen(null)
      setLimitPrice('')
      loadBalance()
      loadPortfolio()
    } catch (e: any) {
      alert(e.response?.data ?? 'Hata')
    }
    loadPortfolio()
    loadBalance()
    loadOrders()
  }

  const cancelOrder = async (id: string) => {
    try {
      await api.post(`/api/order/${id}/cancel`)
      loadBalance(); loadPortfolio(); loadOrders()
    } catch (e: any) {
      alert(e.response?.data ?? 'Hata')
    }
  }

  const toggleLimit = (instrumentId: string) => {
    setLimitOpen(prev => (prev === instrumentId ? null : instrumentId))
    setLimitPrice('')
  }

  return (
    <div className="p-8 bg-slate-900 min-h-screen text-slate-100">
      <div className="flex items-baseline gap-4 mb-6">
        <h1 className="text-2xl font-bold">Borsa Tahtası</h1>
        <span
          className={`text-sm px-2 py-0.5 rounded ${
            marketMove >= 0 ? 'bg-green-900 text-green-300' : 'bg-red-900 text-red-300'
          }`}
        >
          Piyasa {marketMove >= 0 ? '▲' : '▼'} {(marketMove * 100).toFixed(2)}%
        </span>
        <button
          onClick={onLogout}
          className="ml-auto text-xs bg-slate-700 hover:bg-slate-600 px-3 py-1 rounded"
        >
          Çıkış Yap
        </button>
      </div>

      <div className="flex gap-6 items-center mb-6 text-sm">
        <span>Serbest: <b>{balance?.freeCashBalance.toFixed(2) ?? '—'}</b></span>
        <span>Kilitli: <b>{balance?.lockedCashBalance.toFixed(2) ?? '—'}</b></span>
        <span>Toplam: <b>{balance?.total.toFixed(2) ?? '—'}</b></span>
        <label className="ml-auto">
          Adet:
          <input
            type="text"
            inputMode="numeric"
            value={qty}
            onChange={e => {
              const v = e.target.value
              if (v === '' || /^\d+$/.test(v)) setQty(v)
            }}
            className="ml-2 w-20 bg-slate-700 px-2 py-1 rounded"
          />
        </label>
      </div>

      <div className="grid grid-cols-2 lg:grid-cols-3 gap-4">
        {instruments.map(i => {
          const pos = portfolio[i.symbol]
          return (
            <div
              key={i.id}
              className={`bg-slate-800 rounded p-4 ${!i.isActive ? 'opacity-40' : ''}`}
            >
              <div className="flex justify-between items-baseline mb-1">
                <span className="font-bold">{i.symbol}</span>
                <span>{i.currentPrice.toFixed(2)}</span>
              </div>

              {pos ? (
                <div className="text-xs text-slate-400 mb-2">
                  {pos.totalQuantity} lot
                  {pos.lockedQuantity > 0 && ` (${pos.lockedQuantity} kilitli)`}
                  {' · ort '}{pos.averageCost.toFixed(2)}
                  <span className={pos.profitLoss >= 0 ? ' text-green-400' : ' text-red-400'}>
                    {' '}{pos.profitLoss >= 0 ? '+' : ''}{pos.profitLoss.toFixed(2)}
                  </span>
                </div>
              ) : (
                <div className="text-xs text-slate-600 mb-2">pozisyon yok</div>
              )}

              <Sparkline data={history[i.symbol] ?? []} />

              <div className="flex gap-2 mt-3">
                <button
                  onClick={() => sendOrder(i.id, 'Buy')}
                  disabled={!i.isActive}
                  className="flex-1 bg-green-600 hover:bg-green-500 disabled:bg-slate-700 rounded py-1 text-sm"
                >
                  Al
                </button>
                <button
                  onClick={() => sendOrder(i.id, 'Sell')}
                  disabled={!i.isActive}
                  className="flex-1 bg-red-600 hover:bg-red-500 disabled:bg-slate-700 rounded py-1 text-sm"
                >
                  Sat
                </button>
                <button
                  onClick={() => toggleLimit(i.id)}
                  disabled={!i.isActive}
                  className="px-2 bg-slate-600 hover:bg-slate-500 disabled:bg-slate-700 rounded py-1 text-sm"
                >
                  Limit
                </button>
              </div>

              {limitOpen === i.id && (
                <div className="mt-3 pt-3 border-t border-slate-700">
                  <input
                    type="text"
                    inputMode="decimal"
                    autoFocus
                    placeholder="Limit fiyatı"
                    value={limitPrice}
                    onChange={e => {
                      const v = e.target.value
                      if (v === '' || /^\d*[.,]?\d*$/.test(v)) setLimitPrice(v)
                    }}
                    onKeyDown={e => {
                      if (e.key === 'Enter') sendLimit(i.id, 'Buy')
                      if (e.key === 'Escape') toggleLimit(i.id)
                    }}
                    className="w-full bg-slate-700 px-2 py-1 rounded text-sm mb-2"
                  />
                  <div className="flex gap-2">
                    <button
                      onClick={() => sendLimit(i.id, 'Buy')}
                      className="flex-1 bg-green-700 hover:bg-green-600 rounded py-1 text-xs"
                    >
                      Limit Al
                    </button>
                    <button
                      onClick={() => sendLimit(i.id, 'Sell')}
                      className="flex-1 bg-red-700 hover:bg-red-600 rounded py-1 text-xs"
                    >
                      Limit Sat
                    </button>
                  </div>
                </div>
              )}
            </div>
          )
        })}
      </div>
        <h2 className="text-xl font-bold mt-10 mb-3">Emirler</h2>
        <table className="w-full text-sm">
          <thead className="text-slate-400 text-left">
            <tr>
              <th className="py-1">Hisse</th>
              <th>Tip</th>
              <th>Yön</th>
              <th className="text-right">Adet</th>
              <th className="text-right">Fiyat</th>
              <th>Durum</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {orders.map(o => (
              <tr key={o.id} className="border-t border-slate-800">
                <td className="py-1 font-medium">{o.symbol}</td>
                <td>{o.orderType}</td>
                <td className={o.direction === 'Buy' ? 'text-green-400' : 'text-red-400'}>
                  {o.direction}
                </td>
                <td className="text-right">{o.quantity}</td>
                <td className="text-right">{o.price?.toFixed(2) ?? '—'}</td>
                <td>
                  <span className={
                    o.status === 'Pending' ? 'text-yellow-400'
                    : o.status === 'Filled' ? 'text-green-400'
                    : 'text-slate-500'
                  }>
                    {o.status}
                  </span>
                </td>
                <td className="text-right">
                  {o.status === 'Pending' && (
                    <button
                      onClick={() => cancelOrder(o.id)}
                      className="text-xs bg-slate-700 hover:bg-slate-600 px-2 py-0.5 rounded"
                    >
                      İptal
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
    </div>
  )
}

function Sparkline({ data }: { data: number[] }) {
  if (data.length < 2)
    return <div className="h-10 text-xs text-slate-500 flex items-center">veri bekleniyor…</div>

  const min = Math.min(...data)
  const max = Math.max(...data)
  const range = max - min || 1

  const points = data
    .map((v, idx) => `${(idx / (data.length - 1)) * 100},${40 - ((v - min) / range) * 40}`)
    .join(' ')

  const rising = data[data.length - 1] >= data[0]

  return (
    <svg viewBox="0 0 100 40" preserveAspectRatio="none" className="w-full h-10">
      <polyline
        points={points}
        fill="none"
        strokeWidth="2"
        vectorEffect="non-scaling-stroke"
        className={rising ? 'stroke-green-400' : 'stroke-red-400'}
      />
    </svg>
  )
}