import { memo, useEffect, useMemo, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import api, { API } from './api'
import { useAuth } from './auth'
import Login from './Login'
import { useTheme } from './theme'
import { useLang } from './lang'
import ResetPassword from './ResetPassword'
import { Logomark } from './icons'
import GateLayout from './Gate'
import Admin from './Admin'
import { fmt } from './format'


type Instrument = {
  id: string
  symbol: string
  name: string
  currentPrice: number
  isActive: boolean
}
type PricePoint = {
  timestamp: string
  price: number
}

type Balance = {
  freeCashBalance: number
  lockedCashBalance: number
  realizedProfitLoss: number
  total: number
  isAdmin: boolean
}

type Order = {
  id: string
  symbol: string
  orderType: string
  direction: string
  quantity: number
  price: number | null
  stopPrice: number | null
  status: string
  createdAt: string
  lockedAmount: number | null
  executedAmount: number | null
}

type Transaction = {
  id: string
  symbol: string
  direction: string
  executedQuantity: number
  executedPrice: number
  totalAmount: number
  transactionDate: string
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

type OrderUpdate = {
  orders: Order[]
  balance: Balance
  portfolio: PortfolioItem[]
}

// "42,5" -> 42.5 ; "" / "42." / "abc" -> NaN
const parseDecimal = (s: string) => parseFloat(s.replace(',', '.'))

const signed = (n: number) => (n >= 0 ? '+' : '−') + fmt(Math.abs(n))

const dirOf = (n: number) => (n > 0 ? 'up' : n < 0 ? 'down' : 'flat')

const fmtDate = (s: string) =>
  new Date(s).toLocaleString('tr-TR', { dateStyle: 'short', timeStyle: 'short' })

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
  const { loggedIn, checking, logout } = useAuth()

  const params = new URLSearchParams(window.location.search)
  const resetEmail = params.get('email')
  const resetToken = params.get('token')

  if (resetEmail && resetToken) {
    return (
      <ResetPassword
        email={resetEmail}
        token={resetToken}
        onDone={() => { window.location.href = '/' }}
      />
    )
  }
  if (checking) {
    return <GateLayout><div /></GateLayout>
  }
  if (!loggedIn) {
    return <Login onSuccess={() => window.location.reload()} />
  }

  return <Terminal onLogout={logout} />
}

function Terminal({ onLogout }: { onLogout: () => void }) {
  const { lang, toggle: toggleLang, t, tServer } = useLang()
  const { theme, toggle: toggleTheme } = useTheme()

  const [indexValue, setIndexValue] = useState(0)
  const [instruments, setInstruments] = useState<Instrument[]>([])
  const [balance, setBalance] = useState<Balance | null>(null)
  const [portfolio, setPortfolio] = useState<Record<string, PortfolioItem>>({})
  const [orders, setOrders] = useState<Order[]>([])
  const [transactions, setTransactions] = useState<Transaction[]>([])
  const [marketMove, setMarketMove] = useState(0)
const TICK_SECONDS = 60
const WINDOW_HOURS = 2
const MAX_POINTS = (WINDOW_HOURS * 3600) / TICK_SECONDS
const [history, setHistory] = useState<Record<string, number[]>>({})
const seeded = useRef<Set<string>>(new Set())
  const [selected, setSelected] = useState<string | null>(null)
  const [mode, setMode] = useState<'market' | 'limit'>('market')
  const [qty, setQty] = useState('1')
  const [limitPrice, setLimitPrice] = useState('')
  const [stopPrice, setStopPrice] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)

  const [ticks, setTicks] = useState<Record<string, Tick>>({})
  const prevPrices = useRef<Record<string, number>>({})
  const [online, setOnline] = useState(true)
  const [showAdmin, setShowAdmin] = useState(false)

  const loadOrders = () =>
    api.get<Order[]>('/api/order').then(r => setOrders(r.data)).catch(console.error)

  const loadTransactions = () =>
    api.get<Transaction[]>('/api/transactions').then(r => setTransactions(r.data)).catch(console.error)

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
    loadTransactions()
    loadBalance()
    loadPortfolio()
  }, [])

  useEffect(() => {
    for (const i of instruments) {
      if (portfolio[i.symbol]) loadHistory(i)
    }
  }, [instruments, portfolio])

  useEffect(() => {
    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`${API}/hubs/prices`)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000, 30000, 60000, 60000])
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
          next[u.symbol] = [...(prev[u.symbol] ?? []), u.currentPrice].slice(-MAX_POINTS)
        }
        return next
      })
    })

    conn.on('OrderUpdate', (p: OrderUpdate) => {
      // Bir emrin durumu yalnızca eşleşme motoru dokunduğunda değişir; gelen
      // satırları id'ye göre birleştir, defterin geri kalanını yerinde bırak.
      setOrders(prev => {
        const byId = new Map(prev.map(o => [o.id, o]))
        for (const o of p.orders) byId.set(o.id, o)
        return [...byId.values()]
          .sort((a, b) => +new Date(b.createdAt) - +new Date(a.createdAt))
      })

      setBalance(p.balance)

      const map: Record<string, PortfolioItem> = {}
      for (const item of p.portfolio) map[item.symbol] = item
      setPortfolio(map)

      // İşlem defteri yalnızca gerçekleşen bir emirle büyür; ret satır üretmez.
      if (p.orders.some(o => o.status === 'Filled')) loadTransactions()
    })

    conn.onreconnecting(() => setOnline(false))
    conn.onreconnected(() => {
      // Kopukken kaçırılan OrderUpdate'ler geri gelmez; bir kez telafi et.
      setOnline(true)
      loadOrders(); loadBalance(); loadPortfolio(); loadTransactions()
    })
    conn.onclose(() => setOnline(false))
    conn.start().then(() => setOnline(true)).catch(() => setOnline(false))
    return () => { conn.stop() }
  }, [])

  const chosen = instruments.find(i => i.id === selected) ?? null
    // Positions only change when an order executes; their value moves with every
  // tick. Recompute locally rather than asking the server sixty times a minute.
  const livePortfolio = useMemo(() => {
    const priceBySymbol: Record<string, number> = {}
    for (const i of instruments) priceBySymbol[i.symbol] = i.currentPrice

    const out: Record<string, PortfolioItem> = {}
    for (const [symbol, p] of Object.entries(portfolio)) {
      const price = priceBySymbol[symbol] ?? p.currentPrice
      out[symbol] = {
        ...p,
        currentPrice: price,
        marketValue: price * p.totalQuantity,
        profitLoss:  (price - p.averageCost) * p.totalQuantity,
      }
    }
    return out
  }, [portfolio, instruments])

  const holdings = Object.values(livePortfolio)
  const holdingsValue = holdings.reduce((s, p) => s + p.marketValue, 0)
  const openPL = holdings.reduce((s, p) => s + p.profitLoss, 0)
  const equity = (balance?.total ?? 0) + holdingsValue

  const submit = async (direction: 'Buy' | 'Sell') => {
    if (!chosen) return
    setNotice('')

    const quantity = parseInt(qty, 10)
    if (!Number.isFinite(quantity) || quantity < 1) {
      setNotice(t('err.minQty'))
      return
    }

    let body: Record<string, unknown> = { instrumentId: chosen.id, direction, quantity }
    let url = '/api/order/market'

    if (mode === 'limit') {
      const price = parseDecimal(limitPrice)
      if (!Number.isFinite(price) || price <= 0) {
        setNotice(t('err.minPrice'))
        return
      }
      body = { ...body, price }
      url = '/api/order/limit'

      // Stop yalnizca satista anlamli: fiyat asagi gecerse panik satis.
      if (direction === 'Sell' && stopPrice !== '') {
        const stop = parseDecimal(stopPrice)
        if (!Number.isFinite(stop) || stop <= 0) {
          setNotice(t('err.minStop'))
          return
        }
        // Sunucu da ayni kontrolu yapiyor; burada tutmak bir gidis-donusten kurtariyor.
        if (stop >= price || stop >= chosen.currentPrice) {
          setNotice(t('err.stopTooHigh'))
          return
        }
        body = { ...body, stopPrice: stop }
      }
    }

    setBusy(true)
    try {
      await api.post(url, body)
      if (mode === 'limit') { setLimitPrice(''); setStopPrice('') }
    } catch (e: any) {
      setNotice(e.response ? tServer(e.response.data) : t('err.orderFailed'))
    } finally {
      setBusy(false)
    }
    loadPortfolio()
    loadBalance()
    loadOrders()
    loadTransactions()
  }

  const cancelOrder = async (id: string) => {
    setNotice('')
    try {
      await api.post(`/api/order/${id}/cancel`)
    } catch (e: any) {
      setNotice(e.response ? tServer(e.response.data) : t('err.cancelFailed'))
    }
    loadBalance(); loadPortfolio(); loadOrders(); loadTransactions()
  }

  const pick = (i: Instrument) => {
  if (!i.isActive) return
  setSelected(prev => (prev === i.id ? null : i.id))
  setLimitPrice(prev => (prev === '' ? i.currentPrice.toFixed(2).replace('.', ',') : prev))
  loadHistory(i)
}

const loadHistory = (i: Instrument) => {
  if (seeded.current.has(i.symbol)) return
  seeded.current.add(i.symbol)

  api.get<PricePoint[]>(`/api/instruments/${i.id}/history`)
    .then(r => {
      const prices = r.data.map(p => p.price)
      setHistory(prev => ({
        ...prev,
        // canlı tick'ler bu arada gelmiş olabilir; geçmişi öne ekle
        [i.symbol]: [...prices, ...(prev[i.symbol] ?? [])].slice(-MAX_POINTS)
      }))
    })
    .catch(() => seeded.current.delete(i.symbol))   // hata olursa tekrar denenebilsin
}

  const tapeRow = instruments.map(i => {
    const h = history[i.symbol] ?? []
    const base = h.length > 1 ? h[0] : i.currentPrice
    const pct = base ? ((i.currentPrice - base) / base) * 100 : 0
    return { ...i, pct }
  })

  const portfolioInstruments = instruments.filter(i => portfolio[i.symbol])
  const otherInstruments = instruments.filter(i => !portfolio[i.symbol])


  return (
    <div className="shell">
      <header className="rail">
        <div className="wrap rail-in">
          <Logomark size={26} />
          <span className="mark">Fin<em>Sim</em></span>
          <span className="mark-sub">{t('app.tagline')}</span>
          <span className="rail-spacer" />
          <span style={{ fontFamily: 'var(--mono)', fontSize: 13 }}>
            <span style={{ color: 'var(--faint)', letterSpacing: '0.08em' }}>{t('app.market')} </span>
            {indexValue ? fmt(indexValue) : '—'}
            <span className={dirOf(marketMove)}>
              {' '}{marketMove >= 0 ? '▲' : '▼'} {(Math.abs(marketMove) * 100).toFixed(2)}%
            </span>
          </span>

          {!online && (
            <span style={{ fontFamily: 'var(--mono)', fontSize: 11, color: 'var(--fall)' }}>
              ● {t('app.offline')}
            </span>
          )}

          <button
            className="ghost-btn"
            onClick={toggleTheme}
            aria-label={theme === 'night' ? t('app.toDay') : t('app.toNight')}
          >
            {theme === 'night' ? '☀' : '☾'}
          </button>
          <button className="ghost-btn" onClick={toggleLang} aria-label="Language">
            {lang === 'tr' ? 'EN' : 'TR'}
          </button>
          {balance?.isAdmin && (
            <button
              className="ghost-btn"
              aria-pressed={showAdmin}
              onClick={() => setShowAdmin(v => !v)}
            >
              {t('admin.panelButton')}
            </button>
          )}
          <button className="ghost-btn" onClick={onLogout}>{t('app.logout')}</button>
        </div>
      </header>

      <Tape items={tapeRow} />

      <section className="strip">
        <div className="cell">
          <div className="cell-label">{t('strip.equity')}</div>
          <div className="cell-value">₺<Money value={equity} /></div>
          <div className={`cell-delta ${dirOf(openPL)}`}>
            {t('strip.openPL')} {signed(openPL)} ₺
          </div>
        </div>
        <div className="cell">
          <div className="cell-label">{t('strip.free')}</div>
          <div className="cell-value sm">{balance ? fmt(balance.freeCashBalance) : '—'}</div>
        </div>
        <div className="cell">
          <div className="cell-label">{t('strip.position')}</div>
          <div className="cell-value sm">{fmt(holdingsValue)}</div>
        </div>
        <div className="cell">
          <div className="cell-label">{t('strip.realized')}</div>
          <div className="cell-value sm">{balance ? `${signed(balance.realizedProfitLoss)} ₺` : '—'}</div>
        </div>
      </section>

      <main className="wrap">
        {showAdmin ? <Admin onClose={() => setShowAdmin(false)} /> : (
        <>
        {notice && (
          <div className="notice">
            <span style={{ flex: 1 }}>{notice}</span>
            <button onClick={() => setNotice('')} aria-label={t('app.close')}>×</button>
          </div>
        )}

        {portfolioInstruments.length > 0 && (
          <>
            <div className="section-head">
              <h2>{t('board.portfolioTitle')}</h2>
              <span className="section-note">{t('board.portfolioNote', { n: portfolioInstruments.length })}</span>
            </div>

            <div className="tiles">
              {portfolioInstruments.map(i => {
                const pos = livePortfolio[i.symbol]
                return (
                  <button
                    key={i.id}
                    className="tile"
                    data-selected={selected === i.id}
                    data-inactive={!i.isActive}
                    onClick={() => pick(i)}
                    disabled={!i.isActive}
                  >
                    <AreaSpark data={history[i.symbol] ?? []} className="tile-spark" />
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
                          <span>{t('board.lots', { n: pos.totalQuantity })}</span>
                          {pos.lockedQuantity > 0 && (
                            <span className="locked">{t('board.locked', { n: pos.lockedQuantity })}</span>
                          )}
                          <span className="avg-cost">{t('board.avgCost', { n: fmt(pos.averageCost) })}</span>
                          <span className={dirOf(pos.profitLoss)}>{signed(pos.profitLoss)}</span>
                        </>
                      ) : (
                        <span className="empty">{i.isActive ? t('board.noPosition') : t('board.closed')}</span>
                      )}
                    </div>
                  </button>
                )
              })}
            </div>
          </>
        )}

        <div className="section-head">
          <h2>{t('board.otherTitle')}</h2>
          <span className="section-note">{t('board.otherNote', { n: otherInstruments.length })}</span>
        </div>

        <div className="board">
          {otherInstruments.map(i => {
            const open = selected === i.id
            return (
              <div className="row" key={i.id} data-open={open}>
                <button
                  type="button"
                  className="row-head"
                  data-selected={open}
                  data-inactive={!i.isActive}
                  aria-expanded={open}
                  onClick={() => pick(i)}
                  disabled={!i.isActive}
                >
                  <span className="row-sym">{i.symbol}</span>
                  <span className="row-name">{i.name}</span>
                  <span className="row-px" data-tick={ticks[i.symbol]} key={i.currentPrice}>
                    {fmt(i.currentPrice)}
                  </span>
                  <div className="row-pos">
                    <span className="empty">{i.isActive ? t('board.noPosition') : t('board.closed')}</span>
                  </div>
                </button>
                <div className="row-body">
                  <div className="row-body-in">
                    {open && <AreaSpark data={history[i.symbol] ?? []} className="row-spark" />}
                  </div>
                </div>
              </div>
            )
          })}
        </div>

        <div className="panels">
          <div className="panel">
            <div className="section-head">
              <h2>{t('ledger.title')}</h2>
              <span className="section-note">{t('ledger.note')}</span>
            </div>

            {orders.length === 0 ? (
              <div className="empty-state">
                {t('ledger.empty')}
              </div>
            ) : (
              <table className="ledger">
                <thead>
                  <tr>
                    <th>{t('ledger.symbol')}</th>
                    <th className="hide-sm">{t('ledger.type')}</th>
                    <th>{t('ledger.side')}</th>
                    <th className="num">{t('ledger.qty')}</th>
                    <th className="num">{t('ledger.price')}</th>
                    <th>{t('ledger.status')}</th>
                    <th className="num">{t('ledger.locked')}</th>
                    <th className="num">{t('ledger.spent')}</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {orders.map(o => (
                    <tr key={o.id}>
                      <td className="sym">{o.symbol}</td>
                      <td className="hide-sm">{o.orderType === 'Market' ? t('order.market') : t('order.limit')}</td>
                      <td className={o.direction === 'Buy' ? 'up' : 'down'}>
                        {o.direction === 'Buy' ? t('order.buy') : t('order.sell')}
                      </td>
                      <td className="num">{o.quantity}</td>
                      <td className="num">
                        {o.price != null ? fmt(o.price) : '—'}
                        {o.stopPrice != null && (
                          <span className="down" style={{ fontSize: 11, marginLeft: 4 }}>
                            ↓{fmt(o.stopPrice)}
                          </span>
                        )}
                      </td>
                      <td>
                        <span className={`pill ${o.status.toLowerCase()}`}>
                          {o.status === 'Pending' ? t('status.pending')
                        : o.status === 'Filled' ? t('status.filled')
                        : o.status === 'Rejected' ? t('status.rejected')
                        : t('status.cancelled')}
                        </span>
                      </td>
                      <td className="num">{o.lockedAmount != null ? fmt(o.lockedAmount) : '—'}</td>
                      <td className="num">{o.executedAmount != null ? fmt(o.executedAmount) : '—'}</td>
                      <td className="num">
                        {o.status === 'Pending' && (
                          <button className="link-btn" onClick={() => cancelOrder(o.id)}>
                            {t('ledger.cancel')}
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>

          <div className="panel">
            <div className="section-head">
              <h2>{t('tx.title')}</h2>
              <span className="section-note">{t('tx.note')}</span>
            </div>

            {transactions.length === 0 ? (
              <div className="empty-state">
                {t('tx.empty')}
              </div>
            ) : (
              <table className="ledger">
                <thead>
                  <tr>
                    <th>{t('tx.symbol')}</th>
                    <th>{t('tx.side')}</th>
                    <th className="num">{t('tx.qty')}</th>
                    <th className="num">{t('tx.price')}</th>
                    <th className="num">{t('tx.total')}</th>
                    <th>{t('tx.date')}</th>
                  </tr>
                </thead>
                <tbody>
                  {transactions.map(tx => (
                    <tr key={tx.id}>
                      <td className="sym">{tx.symbol}</td>
                      <td className={tx.direction === 'Buy' ? 'up' : 'down'}>
                        {tx.direction === 'Buy' ? t('order.buy') : t('order.sell')}
                      </td>
                      <td className="num">{tx.executedQuantity}</td>
                      <td className="num">{fmt(tx.executedPrice)}</td>
                      <td className="num">{fmt(tx.totalAmount)}</td>
                      <td>{fmtDate(tx.transactionDate)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>
        </>
        )}
      </main>

      {!showAdmin && (
      <div className="ticket">
        <div className="wrap ticket-in">
          <div className="ticket-slot grow">
            <span className="field-label">{t('ticket.instrument')}</span>
            {chosen ? (
              <div className="field-static">
                {chosen.symbol}{' '}
                <span style={{ fontFamily: 'var(--mono)', fontSize: 13, color: 'var(--mute)' }}>
                  {fmt(chosen.currentPrice)}
                </span>
              </div>
            ) : (
              <div className="field-static none">{t('ticket.pick')}</div>
            )}
          </div>

          <div className="ticket-slot" style={{ minWidth: 'auto' }}>
            <span className="field-label">{t('ticket.orderType')}</span>
            <div className="seg">
              <button aria-pressed={mode === 'market'} onClick={() => setMode('market')}>
                {t('order.market')}
              </button>
              <button aria-pressed={mode === 'limit'} onClick={() => setMode('limit')}>
                {t('order.limit')}
              </button>
            </div>
          </div>

          <div className="ticket-slot" style={{ minWidth: 96 }}>
            <label className="field-label" htmlFor="qty">{t('ticket.qty')}</label>
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
            <label className="field-label" htmlFor="lmt">{t('ticket.limitPrice')}</label>
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

          <div className="ticket-slot" style={{ minWidth: 120 }}>
            <label className="field-label" htmlFor="stp">{t('ticket.stopPrice')}</label>
            <input
              id="stp"
              className="field-input"
              type="text"
              inputMode="decimal"
              placeholder={mode === 'limit' ? t('ticket.stopHint') : '—'}
              disabled={mode !== 'limit'}
              value={mode === 'limit' ? stopPrice : ''}
              onChange={e => {
                const v = e.target.value
                if (v === '' || /^\d*[.,]?\d*$/.test(v)) setStopPrice(v)
              }}
              onKeyDown={e => {
                if (e.key === 'Enter') { e.preventDefault(); submit('Sell') }
              }}
            />
          </div>

          <button className="trade buy" disabled={!chosen || busy} onClick={() => submit('Buy')}>
            {t('ticket.buy')}
          </button>
          <button className="trade sell" disabled={!chosen || busy} onClick={() => submit('Sell')}>
            {t('ticket.sell')}
          </button>
        </div>
      </div>
      )}
    </div>
  )
}

/** Filled area chart — always-on inside a portfolio tile, or shown inside an expanded board row. */
function AreaSpark({ data, className }: { data: number[]; className: string }) {
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
    <svg className={className} viewBox="0 0 100 30" preserveAspectRatio="none" aria-hidden="true">
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


const Tape = memo(function Tape({ items }: { items: { id: string; symbol: string; currentPrice: number; pct: number }[] }) {
  const tintOf = (pct: number) => {
    if (Math.abs(pct) < 0.001) return 'var(--mute)'
    const w = Math.min(100, 14 + Math.abs(pct) * 43)
    const base = pct > 0 ? 'var(--rise)' : 'var(--fall)'
    return `color-mix(in srgb, ${base} ${w.toFixed(0)}%, var(--mute))`
  }

  return (
    <div className="tape">
      <div className="tape-run">
        {[0, 1].map(copy => (
          <div key={copy} style={{ display: 'flex' }}>
            {items.map(i => (
              <div
                className="tape-item"
                key={`${copy}-${i.id}`}
                style={{ ['--tint' as string]: tintOf(i.pct) }}
              >
                <span className="tape-sym">{i.symbol}</span>
                <span className="tape-px">{fmt(i.currentPrice)}</span>
                <span className="tape-dt">
                  {i.pct >= 0 ? '▲' : '▼'} {Math.abs(i.pct).toFixed(2)}%
                </span>
              </div>
            ))}
          </div>
        ))}
      </div>
    </div>
  )
})