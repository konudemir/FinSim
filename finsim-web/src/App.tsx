import { useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import api, { API } from './api'
import { useAuth } from './auth'
import Login from './Login'
import { useTheme } from './theme'

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
  indexValue: number
  prices: { symbol: string; currentPrice: number }[]
}

type Tick = 'up' | 'down'

// "42,5" -> 42.5 ; "" / "42." / "abc" -> NaN
const parseDecimal = (s: string) => parseFloat(s.replace(',', '.'))

const fmt = (n: number) =>
  n.toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

const signed = (n: number) => (n >= 0 ? '+' : '−') + fmt(Math.abs(n))

const dirOf = (n: number) => (n > 0 ? 'up' : n < 0 ? 'down' : 'flat')

/** 1.234,56 with the kuruş set smaller — print-ledger habit. */
function Money({ value }: { value: number }) {
  const [lira, kurus] = fmt(value).split(',')
  return (
    <span>
      {lira}
      <span className="kurus">,{kurus}</span>
    </span>
  )
}


export default function App() {
  const { loggedIn, logout } = useAuth()
  const { theme, toggle } = useTheme()

  if (!loggedIn) {
    return <Login onSuccess={() => window.location.reload()} />
  }

  return <Terminal onLogout={logout} theme={theme} onToggleTheme={toggle} />
}

function Terminal({ onLogout, theme, onToggleTheme }: {
  onLogout: () => void
  theme: 'night' | 'day'
  onToggleTheme: () => void
}) {
  const [indexValue, setIndexValue] = useState(0)
  const [instruments, setInstruments] = useState<Instrument[]>([])
  const [history, setHistory] = useState<Record<string, number[]>>({})
  const [balance, setBalance] = useState<Balance | null>(null)
  const [portfolio, setPortfolio] = useState<Record<string, PortfolioItem>>({})
  const [orders, setOrders] = useState<Order[]>([])
  const [marketMove, setMarketMove] = useState(0)

  const [selected, setSelected] = useState<string | null>(null)
  const [mode, setMode] = useState<'market' | 'limit'>('market')
  const [qty, setQty] = useState('1')
  const [limitPrice, setLimitPrice] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)

  const [ticks, setTicks] = useState<Record<string, Tick>>({})
  const prevPrices = useRef<Record<string, number>>({})

  const loadOrders = () =>
    api.get<Order[]>('/api/order').then(r => setOrders(r.data)).catch(console.error)

  const loadBalance = () =>
    api.get<Balance>('/api/users/balance').then(r => setBalance(r.data)).catch(console.error)

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
      .then(res => {
        setInstruments(res.data)
        for (const i of res.data) prevPrices.current[i.symbol] = i.currentPrice
      })
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
      setIndexValue(payload.indexValue)

      const fresh: Record<string, Tick> = {}
      for (const u of payload.prices) {
        const before = prevPrices.current[u.symbol]
        if (before !== undefined && u.currentPrice !== before) {
          fresh[u.symbol] = u.currentPrice > before ? 'up' : 'down'
        }
        prevPrices.current[u.symbol] = u.currentPrice
      }
      setTicks(fresh)

      setInstruments(prev =>
        prev.map(i => {
          const u = payload.prices.find(x => x.symbol === i.symbol)
          return u ? { ...i, currentPrice: u.currentPrice } : i
        })
      )

      setHistory(prev => {
        const next = { ...prev }
        for (const u of payload.prices) {
          next[u.symbol] = [...(prev[u.symbol] ?? []), u.currentPrice].slice(-24)
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

  const chosen = instruments.find(i => i.id === selected) ?? null

  const holdings = Object.values(portfolio)
  const holdingsValue = holdings.reduce((s, p) => s + p.marketValue, 0)
  const openPL = holdings.reduce((s, p) => s + p.profitLoss, 0)
  const equity = (balance?.total ?? 0) + holdingsValue

  const submit = async (direction: 'Buy' | 'Sell') => {
    if (!chosen) return
    setNotice('')

    const quantity = parseInt(qty, 10)
    if (!Number.isFinite(quantity) || quantity < 1) {
      setNotice('Adet en az 1 olmalı.')
      return
    }

    let body: Record<string, unknown> = { instrumentId: chosen.id, direction, quantity }
    let url = '/api/order/market'

    if (mode === 'limit') {
      const price = parseDecimal(limitPrice)
      if (!Number.isFinite(price) || price <= 0) {
        setNotice('Limit fiyatı 0’dan büyük olmalı.')
        return
      }
      body = { ...body, price }
      url = '/api/order/limit'
    }

    setBusy(true)
    try {
      await api.post(url, body)
      if (mode === 'limit') setLimitPrice('')
    } catch (e: any) {
      setNotice(typeof e.response?.data === 'string' ? e.response.data : 'Emir geçmedi.')
    } finally {
      setBusy(false)
    }
    loadPortfolio()
    loadBalance()
    loadOrders()
  }

  const cancelOrder = async (id: string) => {
    setNotice('')
    try {
      await api.post(`/api/order/${id}/cancel`)
    } catch (e: any) {
      setNotice(typeof e.response?.data === 'string' ? e.response.data : 'İptal geçmedi.')
    }
    loadBalance(); loadPortfolio(); loadOrders()
  }

  const pick = (i: Instrument) => {
    if (!i.isActive) return
    setSelected(prev => (prev === i.id ? null : i.id))
    setLimitPrice(prev => (prev === '' ? i.currentPrice.toFixed(2).replace('.', ',') : prev))
  }

  const tapeRow = instruments.map(i => {
    const h = history[i.symbol] ?? []
    const base = h.length > 1 ? h[0] : i.currentPrice
    const pct = base ? ((i.currentPrice - base) / base) * 100 : 0
    return { ...i, pct }
  })

  return (
    <div className="shell">
      <header className="rail">
        <div className="wrap rail-in">
          <span className="mark">Fin<em>Sim</em></span>
          <span className="mark-sub">Borsa Simülasyonu</span>
          <span className="rail-spacer" />
          <span style={{ fontFamily: 'var(--mono)', fontSize: 13 }}>
            <span style={{ color: 'var(--faint)', letterSpacing: '0.08em' }}>Market </span>
            {indexValue ? fmt(indexValue) : '—'}
            <span className={dirOf(marketMove)}>
              {' '}{marketMove >= 0 ? '▲' : '▼'} {(Math.abs(marketMove) * 100).toFixed(2)}%
            </span>
          </span>
          <button
            className="ghost-btn"
            onClick={onToggleTheme}
            aria-label={theme === 'night' ? 'Gündüz moduna geç' : 'Gece moduna geç'}
          >
            {theme === 'night' ? '☀' : '☾'}
          </button>
          <button className="ghost-btn" onClick={onLogout}>Çıkış</button>
        </div>
      </header>

      <div className="tape">
        <div className="tape-run">
          {[0, 1].map(copy => (
            <div key={copy} style={{ display: 'flex' }}>
              {tapeRow.map(i => (
                <div className="tape-item" key={`${copy}-${i.id}`}>
                  <span className="tape-sym">{i.symbol}</span>
                  <span className="tape-px">{fmt(i.currentPrice)}</span>
                  <span className={`tape-dt ${dirOf(i.pct)}`}>
                    {i.pct >= 0 ? '▲' : '▼'} {Math.abs(i.pct).toFixed(2)}%
                  </span>
                </div>
              ))}
            </div>
          ))}
        </div>
      </div>

      <section className="strip">
        <div className="cell">
          <div className="cell-label">Hesap Değeri</div>
          <div className="cell-value">₺<Money value={equity} /></div>
          <div className={`cell-delta ${dirOf(openPL)}`}>
            Açık pozisyon {signed(openPL)} ₺
          </div>
        </div>
        <div className="cell">
          <div className="cell-label">Serbest</div>
          <div className="cell-value sm">{balance ? fmt(balance.freeCashBalance) : '—'}</div>
        </div>
        <div className="cell">
          <div className="cell-label">Kilitli</div>
          <div
            className="cell-value sm"
            style={{ color: (balance?.lockedCashBalance ?? 0) > 0 ? 'var(--amber)' : undefined }}
          >
            {balance ? fmt(balance.lockedCashBalance) : '—'}
          </div>
        </div>
        <div className="cell">
          <div className="cell-label">Pozisyon</div>
          <div className="cell-value sm">{fmt(holdingsValue)}</div>
        </div>
      </section>

      <main className="wrap">
        {notice && (
          <div className="notice">
            <span style={{ flex: 1 }}>{notice}</span>
            <button onClick={() => setNotice('')} aria-label="Kapat">×</button>
          </div>
        )}

        <div className="section-head">
          <h2>Tahta</h2>
          <span className="section-note">{instruments.length} enstrüman · emir için seç</span>
        </div>

        <div className="board">
          {instruments.map(i => {
            const pos = portfolio[i.symbol]
            return (
              <button
                key={i.id}
                className="tile"
                data-selected={selected === i.id}
                data-inactive={!i.isActive}
                onClick={() => pick(i)}
                disabled={!i.isActive}
              >
                <AreaSpark data={history[i.symbol] ?? []} />
                <div className="tile-top">
                  <span className="tile-sym">{i.symbol}</span>
                  <span className="tile-px" data-tick={ticks[i.symbol]} key={i.currentPrice}>
                    {fmt(i.currentPrice)}
                  </span>
                </div>
                <div className="tile-name">{i.name}</div>
                <div className="tile-pos">
                  {pos ? (
                    <>
                      <span>{pos.totalQuantity} lot</span>
                      {pos.lockedQuantity > 0 && (
                        <span className="locked">{pos.lockedQuantity} kilitli</span>
                      )}
                      <span className={dirOf(pos.profitLoss)}>{signed(pos.profitLoss)}</span>
                    </>
                  ) : (
                    <span className="empty">{i.isActive ? 'pozisyon yok' : 'işleme kapalı'}</span>
                  )}
                </div>
              </button>
            )
          })}
        </div>

        <div className="section-head">
          <h2>Emir Defteri</h2>
          <span className="section-note">son 50 kayıt</span>
        </div>

        {orders.length === 0 ? (
          <div className="empty-state">
            Henüz emir yok. Tahtadan bir hisse seç, aşağıdan adet gir.
          </div>
        ) : (
          <table className="ledger">
            <thead>
              <tr>
                <th>Hisse</th>
                <th className="hide-sm">Tip</th>
                <th>Yön</th>
                <th className="num">Adet</th>
                <th className="num">Fiyat</th>
                <th>Durum</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {orders.map(o => (
                <tr key={o.id}>
                  <td className="sym">{o.symbol}</td>
                  <td className="hide-sm">{o.orderType === 'Market' ? 'Piyasa' : 'Limit'}</td>
                  <td className={o.direction === 'Buy' ? 'up' : 'down'}>
                    {o.direction === 'Buy' ? 'Alış' : 'Satış'}
                  </td>
                  <td className="num">{o.quantity}</td>
                  <td className="num">{o.price != null ? fmt(o.price) : '—'}</td>
                  <td>
                    <span className={`pill ${o.status.toLowerCase()}`}>
                      {o.status === 'Pending' ? 'Bekliyor'
                        : o.status === 'Filled' ? 'Gerçekleşti'
                        : 'İptal'}
                    </span>
                  </td>
                  <td className="num">
                    {o.status === 'Pending' && (
                      <button className="link-btn" onClick={() => cancelOrder(o.id)}>
                        iptal et
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </main>

      <div className="ticket">
        <div className="wrap ticket-in">
          <div className="ticket-slot grow">
            <span className="field-label">Enstrüman</span>
            {chosen ? (
              <div className="field-static">
                {chosen.symbol}{' '}
                <span style={{ fontFamily: 'var(--mono)', fontSize: 13, color: 'var(--mute)' }}>
                  {fmt(chosen.currentPrice)}
                </span>
              </div>
            ) : (
              <div className="field-static none">Tahtadan seç</div>
            )}
          </div>

          <div className="ticket-slot" style={{ minWidth: 'auto' }}>
            <span className="field-label">Emir tipi</span>
            <div className="seg">
              <button aria-pressed={mode === 'market'} onClick={() => setMode('market')}>
                Piyasa
              </button>
              <button aria-pressed={mode === 'limit'} onClick={() => setMode('limit')}>
                Limit
              </button>
            </div>
          </div>

          <div className="ticket-slot" style={{ minWidth: 96 }}>
            <label className="field-label" htmlFor="qty">Adet</label>
            <input
              id="qty"
              className="field-input"
              type="text"
              inputMode="numeric"
              value={qty}
              onChange={e => {
                const v = e.target.value
                if (v === '' || /^\d+$/.test(v)) setQty(v)
              }}
            />
          </div>

          <div className="ticket-slot" style={{ minWidth: 120 }}>
            <label className="field-label" htmlFor="lmt">Limit fiyatı</label>
            <input
              id="lmt"
              className="field-input"
              type="text"
              inputMode="decimal"
              placeholder={mode === 'limit' ? '0,00' : '—'}
              disabled={mode !== 'limit'}
              value={mode === 'limit' ? limitPrice : ''}
              onChange={e => {
                const v = e.target.value
                if (v === '' || /^\d*[.,]?\d*$/.test(v)) setLimitPrice(v)
              }}
              onKeyDown={e => {
                if (e.key === 'Enter') { e.preventDefault(); submit('Buy') }
              }}
            />
          </div>

          <button className="trade buy" disabled={!chosen || busy} onClick={() => submit('Buy')}>
            Al
          </button>
          <button className="trade sell" disabled={!chosen || busy} onClick={() => submit('Sell')}>
            Sat
          </button>
        </div>
      </div>
    </div>
  )
}

/** Filled area chart that sits behind the tile content. */
function AreaSpark({ data }: { data: number[] }) {
  if (data.length < 2) return null

  const min = Math.min(...data)
  const max = Math.max(...data)
  const range = max - min || 1
  const last = data.length - 1

  const pts = data.map((v, idx) => {
    const x = (idx / last) * 100
    const y = 30 - ((v - min) / range) * 26
    return `${x.toFixed(2)},${y.toFixed(2)}`
  })

  const rising = data[last] >= data[0]
  const stroke = rising ? 'var(--rise)' : 'var(--fall)'
  const id = `g${rising ? 'u' : 'd'}`

  return (
    <svg className="tile-spark" viewBox="0 0 100 30" preserveAspectRatio="none" aria-hidden="true">
      <defs>
        <linearGradient id={id} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={stroke} stopOpacity="0.34" />
          <stop offset="100%" stopColor={stroke} stopOpacity="0" />
        </linearGradient>
      </defs>
      <polygon points={`0,30 ${pts.join(' ')} 100,30`} fill={`url(#${id})`} />
      <polyline
        points={pts.join(' ')}
        fill="none"
        stroke={stroke}
        strokeWidth="1.5"
        vectorEffect="non-scaling-stroke"
      />
    </svg>
  )
}