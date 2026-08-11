import { useEffect, useState } from 'react'
import axios from 'axios'
import * as signalR from '@microsoft/signalr'

const API = 'http://localhost:5209'
const USER = 'f0000000-0000-0000-0000-000000000001'

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

export default function App() {
  const [instruments, setInstruments] = useState<Instrument[]>([])
  const [history, setHistory] = useState<Record<string, number[]>>({})
  const [balance, setBalance] = useState<Balance | null>(null)
  const [portfolio, setPortfolio] = useState<Record<string, PortfolioItem>>({})
  const [qty, setQty] = useState(1)
  const [marketMove, setMarketMove] = useState(0)
  const [limitOpen, setLimitOpen] = useState<string | null>(null)
  const [limitPrice, setLimitPrice] = useState('')

  const loadBalance = () =>
    axios.get<Balance>(`${API}/api/users/${USER}/balance`)
      .then(r => setBalance(r.data))
      .catch(console.error)

  const loadPortfolio = () =>
    axios.get<PortfolioItem[]>(`${API}/api/users/${USER}/portfolio`)
      .then(r => {
        const map: Record<string, PortfolioItem> = {}
        for (const p of r.data) map[p.symbol] = p
        setPortfolio(map)
      })
      .catch(console.error)

  useEffect(() => {
    axios.get<Instrument[]>(`${API}/api/instruments`)
      .then(res => setInstruments(res.data))
      .catch(console.error)
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
    })

    conn.start().catch(console.error)
    return () => { conn.stop() }
  }, [])

  const sendOrder = async (instrumentId: string, direction: 'Buy' | 'Sell') => {
    try {
      await axios.post(`${API}/api/order/market`, {
        userId: USER,
        instrumentId,
        direction,
        quantity: qty,
      })
      loadBalance()
      loadPortfolio()
    } catch (e: any) {
      alert(e.response?.data ?? 'Hata')
    }
  }

  const sendLimit = async (instrumentId: string, direction: 'Buy' | 'Sell') => {
    const price = Number(limitPrice)
    if (!price || price <= 0) {
      alert('Geçerli bir fiyat gir')
      return
    }

    try {
      await axios.post(`${API}/api/order/limit`, {
        userId: USER,
        instrumentId,
        direction,
        quantity: qty,
        price,
      })
      setLimitOpen(null)
      setLimitPrice('')
      loadBalance()
      loadPortfolio()
    } catch (e: any) {
      alert(e.response?.data ?? 'Hata')
    }
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
      </div>

      <div className="flex gap-6 items-center mb-6 text-sm">
        <span>Serbest: <b>{balance?.freeCashBalance.toFixed(2) ?? '—'}</b></span>
        <span>Kilitli: <b>{balance?.lockedCashBalance.toFixed(2) ?? '—'}</b></span>
        <span>Toplam: <b>{balance?.total.toFixed(2) ?? '—'}</b></span>
        <label className="ml-auto">
          Adet:
          <input
            type="number"
            min={1}
            value={qty}
            onChange={e => setQty(Number(e.target.value))}
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
                  onClick={() => setLimitOpen(limitOpen === i.id ? null : i.id)}
                  disabled={!i.isActive}
                  className="px-2 bg-slate-600 hover:bg-slate-500 disabled:bg-slate-700 rounded py-1 text-sm"
                >
                  Limit
                </button>
              </div>

              {limitOpen === i.id && (
                <div className="mt-3 pt-3 border-t border-slate-700">
                  <input
                    type="number"
                    step="0.01"
                    placeholder="Limit fiyatı"
                    value={limitPrice}
                    onChange={e => setLimitPrice(e.target.value)}
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