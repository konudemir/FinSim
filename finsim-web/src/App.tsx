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

const PAGE_SIZE = 5
const MARKET_PAGE_SIZE = 20

type Instrument = {
  id: string
  symbol: string
  name: string
  currentPrice: number
  isActive: boolean
  type: 'Stock' | 'Fund'
}
type PricePoint = {
  timestamp: string
  price: number
  volume: number
}

type PnlPoint = {
  date: string
  portfolioValue: number
  pnl: number
  realizedPnl: number
  isLive: boolean
}

type Balance = {
  freeCashBalance: number
  lockedCashBalance: number
  realizedProfitLoss: number
  total: number
  marginUsed: number
  netDeposits: number
  isAdmin: boolean
}

type Order = {
  id: string
  symbol: string
  orderType: string
  direction: string
  quantity: number
  filledQuantity: number
  avgPrice: number
  price: number | null
  stopPrice: number | null
  status: string
  createdAt: string
  lockedAmount: number | null
  executedAmount: number | null
  expiresAt: string | null
  liquidated: boolean
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
  isShort: boolean
}

type LiquidationAlert = {
  id: string
  symbol: string
  quantity: number
  amount: number
}
type PriceUpdate = {
  marketMove: number
  indexValue: number
  prices: { symbol: string; currentPrice: number; volume: number }[]
}
type Tick = 'up' | 'down'

type OrderUpdate = {
  orders: Order[]
  balance: Balance
  portfolio: PortfolioItem[]
}

// "42,5" -> 42.5 ; "" / "42." / "abc" -> NaN
const parseDecimal = (s: string) => parseFloat(s.replace(',', '.'))

// blank expiry date -> no expiry; otherwise the order is valid through the end of that day.
const expiryPartsFromDate = (dateStr: string): { days: number; hours: number; minutes: number } => {
  if (dateStr === '') return { days: 0, hours: 0, minutes: 0 }
  const target = new Date(`${dateStr}T23:59:59`)
  const ms = Math.max(0, target.getTime() - Date.now())
  const totalMinutes = Math.floor(ms / 60000)
  return {
    days: Math.floor(totalMinutes / 1440),
    hours: Math.floor((totalMinutes % 1440) / 60),
    minutes: totalMinutes % 60,
  }
}

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

// null past/absent deadlines — absence is the common case and shouldn't add noise.
const countdown = (expiresAt: string, now: number): string | null => {
  const ms = new Date(expiresAt).getTime() - now
  if (ms <= 0) return null
  const totalMinutes = Math.floor(ms / 60000)
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60
  return hours > 0 ? `${hours}h ${minutes}m` : `${minutes}m`
}

function filterSortInstruments(list: Instrument[], query: string, sort: string): Instrument[] {
  const q = query.trim().toLocaleLowerCase('tr')
  const filtered = q
    ? list.filter(i =>
        i.symbol.toLocaleLowerCase('tr').includes(q) ||
        i.name.toLocaleLowerCase('tr').includes(q))
    : list
  const cmp: Record<string, (a: Instrument, b: Instrument) => number> = {
    'symbol-asc':  (a, b) => a.symbol.localeCompare(b.symbol, 'tr'),
    'symbol-desc': (a, b) => b.symbol.localeCompare(a.symbol, 'tr'),
    'price-asc':   (a, b) => a.currentPrice - b.currentPrice,
    'price-desc':  (a, b) => b.currentPrice - a.currentPrice,
  }
  return [...filtered].sort(cmp[sort])
}

function paginate<T>(items: T[], page: number, pageSize = PAGE_SIZE) {
  const totalPages = Math.max(1, Math.ceil(items.length / pageSize))
  const clampedPage = Math.min(Math.max(page, 1), totalPages)
  return { items: items.slice((clampedPage - 1) * pageSize, clampedPage * pageSize), totalPages, page: clampedPage }
}

// Right-aligned page control. With up to 10 pages, listing every page number
// (1 2 3 ... 9 10) is noisy — prev/next plus a "page / total" label scales to
// any page count without it, and collapses to a bare "1" when there's nothing
// to page through.
function Pager({ page, totalPages, onChange }: { page: number; totalPages: number; onChange: (page: number) => void }) {
  const { t } = useLang()
  const clamped = Math.min(Math.max(page, 1), totalPages)
  if (totalPages <= 1) return <div className="pager">1</div>
  return (
    <div className="pager">
      <button
        type="button"
        className="ghost-btn"
        disabled={clamped <= 1}
        onClick={() => onChange(clamped - 1)}
        aria-label={t('pager.prev')}
      >
        ‹
      </button>
      <span className="pager-label">{clamped} / {totalPages}</span>
      <button
        type="button"
        className="ghost-btn"
        disabled={clamped >= totalPages}
        onClick={() => onChange(clamped + 1)}
        aria-label={t('pager.next')}
      >
        ›
      </button>
    </div>
  )
}

function OrderTable({ orders, pending, now, onCancel, onReplace, replacing, minRows = PAGE_SIZE }: {
  orders: Order[]; pending: boolean; now: number
  onCancel: (id: string) => void
  onReplace: (id: string) => void
  replacing: Set<string>
  minRows?: number
}) {
  const { t } = useLang()
  return (
    <div className="panel-scroll">
      <table className="ledger">
        <thead>
          <tr>
            <th>{t('ledger.symbol')}</th>
            <th className="hide-sm">{t('ledger.type')}</th>
            <th>{t('ledger.side')}</th>
            <th className="num">{t('ledger.qty')}</th>
            <th className="num">{t('ledger.limit')}</th>
            <th className="num">{t('ledger.avgFill')}</th>
            {pending
              ? <><th className="num">{t('ledger.locked')}</th><th /></>
              : <><th>{t('ledger.status')}</th><th className="num">{t('ledger.spent')}</th></>}
          </tr>
        </thead>
        <tbody>
          {orders.map(o => (
            <tr key={o.id} className={o.status === 'Expired' ? 'expired' : undefined}>
              <td className="sym">{o.symbol}</td>
              <td className="hide-sm">{o.orderType === 'Market' ? t('order.market') : t('order.limit')}</td>
              <td className={o.direction === 'Buy' ? 'up' : 'down'}>
                {o.direction === 'Buy' ? t('order.buy') : t('order.sell')}
              </td>
              <td className="num">
                {o.filledQuantity > 0
                  ? <>{o.filledQuantity}<span style={{ opacity: .5 }}> / {o.quantity}</span></>
                  : o.quantity}
              </td>
              <td className="num">
                {o.orderType === 'Market' ? '—' : (o.price != null ? fmt(o.price) : '—')}
                {o.stopPrice != null && (
                  <span className="down" style={{ fontSize: 11, marginLeft: 4 }}>↓{fmt(o.stopPrice)}</span>
                )}
              </td>
              <td className="num">
                {o.filledQuantity > 0 ? fmt(o.avgPrice) : '—'}
              </td>
              {pending ? (
                <>
                  <td className="num">
                    {o.lockedAmount != null ? fmt(o.lockedAmount) : '—'}
                    {o.expiresAt != null && countdown(o.expiresAt, now) != null && (
                      <div className="expiry-countdown">{t('ledger.expiresIn', { n: countdown(o.expiresAt, now)! })}</div>
                    )}
                  </td>
                  <td className="num">
                    <button className="link-btn" onClick={() => onCancel(o.id)}>{t('ledger.cancel')}</button>
                  </td>
                </>
              ) : (
                <>
                  <td>
                    <span className={`pill ${o.status.toLowerCase()}`}>
                      {o.status === 'Filled' ? t('status.filled')
                      : o.status === 'PartiallyFilled' ? t('status.partiallyFilled')
                      : o.status === 'Rejected' ? t('status.rejected')
                      : o.status === 'Expired' ? t('status.expired')
                      : t('status.cancelled')}
                    </span>
                    {o.status === 'Expired' && (
                      <button
                        className="link-btn"
                        style={{ marginLeft: 8 }}
                        disabled={replacing.has(o.id)}
                        onClick={() => onReplace(o.id)}
                      >
                        {t('ledger.replace')}
                      </button>
                    )}
                  </td>
                  <td className="num">{o.filledQuantity > 0 ? fmt(o.avgPrice * o.filledQuantity) : '—'}</td>
                </>
              )}
            </tr>
          ))}
          {Array.from({ length: Math.max(0, minRows - orders.length) }).map((_, idx) => (
            <tr key={`filler-${idx}`} className="filler-row" aria-hidden="true">
              <td colSpan={8}>&nbsp;</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}


const InstrumentRow = memo(function InstrumentRow({ i, open, tick, pos, sparkData, onClick }: {
  i: Instrument
  open: boolean
  tick: Tick | undefined
  pos: PortfolioItem | undefined
  sparkData: PricePoint[]
  onClick: () => void
}) {
  const { t } = useLang()
  return (
    <div className="row" data-open={open}>
      <button
        type="button"
        className="row-head"
        data-selected={open}
        data-inactive={!i.isActive}
        aria-expanded={open}
        onClick={onClick}
        disabled={!i.isActive}
      >
        <span className="row-sym">{i.symbol}</span>
        {i.type === 'Fund' && <span className="fund-badge">{t('board.fundBadge')}</span>}
        {pos?.isShort && <span className="short-badge">{t('board.shortBadge')}</span>}
        <span className="row-name">{i.name}</span>
        <span className="row-px" data-tick={tick} key={i.currentPrice}>
          {fmt(i.currentPrice)}
        </span>
        <div className="row-pos">
          {pos ? (
            <>
              <span>
                {pos.isShort
                  ? t('board.shortLots', { n: Math.abs(pos.totalQuantity) })
                  : t('board.lots', { n: pos.totalQuantity })}
              </span>
              {pos.lockedQuantity > 0 && (
                <span className="locked">{t('board.locked', { n: pos.lockedQuantity })}</span>
              )}
              <span className="avg-cost">{t('board.avgCost', { n: fmt(pos.averageCost) })}</span>
              <span className={dirOf(pos.profitLoss)}>{signed(pos.profitLoss)}</span>
            </>
          ) : (
            !i.isActive && <span className="empty">{t('board.closed')}</span>
          )}
        </div>
      </button>
      <div className="row-body">
        <div className="row-body-in">
          {open && <AreaSpark data={sparkData} className="row-spark" />}
        </div>
      </div>
    </div>
  )
})

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
// Must match MarketTickWorker.Every in src/FinSim.Api/BackgroundWorker.cs — the
// worker writes one PriceHistory row per instrument at that cadence, and this
// constant is how many seconds of real history each chart point represents.
const TICK_SECONDS = 15
const WINDOW_HOURS = 24
const MAX_POINTS = (WINDOW_HOURS * 3600) / TICK_SECONDS
const [history, setHistory] = useState<Record<string, PricePoint[]>>({})
const seeded = useRef<Set<string>>(new Set())
  const [selected, setSelected] = useState<string | null>(null)
  const [mode, setMode] = useState<'market' | 'limit'>('market')
  const [qty, setQty] = useState('1')
  const [limitPrice, setLimitPrice] = useState('')
  const [stopPrice, setStopPrice] = useState('')
  const [expiryDate, setExpiryDate] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [liquidations, setLiquidations] = useState<LiquidationAlert[]>([])
  const [now, setNow] = useState(() => Date.now())
  const [replacing, setReplacing] = useState<Set<string>>(new Set())

  // The menu button sits at the midpoint between the true left edge of the
  // screen and the logo's left edge. That gap isn't a fixed CSS value — the
  // .wrap column centers itself (max-width + auto margins) on wide viewports,
  // so the logo can sit well past the 28px padding alone. Measure it instead.
  const railRef = useRef<HTMLElement>(null)
  const logoRef = useRef<HTMLSpanElement>(null)
  const [navTogglePos, setNavTogglePos] = useState(14)

  useEffect(() => {
    const recalc = () => {
      if (!railRef.current || !logoRef.current) return
      const railLeft = railRef.current.getBoundingClientRect().left
      const logoLeft = logoRef.current.getBoundingClientRect().left
      setNavTogglePos((logoLeft - railLeft) / 2)
    }
    recalc()
    window.addEventListener('resize', recalc)
    return () => window.removeEventListener('resize', recalc)
  }, [])

  const [ticks, setTicks] = useState<Record<string, Tick>>({})
  const prevPrices = useRef<Record<string, number>>({})
  const [online, setOnline] = useState(true)
  const [view, setView] = useState<'portfolio' | 'market' | 'admin'>('portfolio')
  const [menuOpen, setMenuOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState('symbol-asc')
  const [marketQuery, setMarketQuery] = useState('')
  const [marketSort, setMarketSort] = useState('symbol-asc')

  const [portfolioPage, setPortfolioPage] = useState(1)
  const [marketPage, setMarketPage] = useState(1)
  const [pendingPage, setPendingPage] = useState(1)
  const [pastPage, setPastPage] = useState(1)
  const [txPage, setTxPage] = useState(1)

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
      setNow(Date.now())
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
        const now = new Date().toISOString()
        for (const u of payload.prices) {
          next[u.symbol] = [...(prev[u.symbol] ?? []),
          { timestamp: now, price: u.currentPrice, volume: u.volume }].slice(-MAX_POINTS)
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

      setBalance(prev => ({ ...p.balance, isAdmin: prev?.isAdmin ?? false }))

      const map: Record<string, PortfolioItem> = {}
      for (const item of p.portfolio) map[item.symbol] = item
      setPortfolio(map)

      // İşlem defteri yalnızca gerçekleşen bir emirle büyür; ret satır üretmez.
      if (p.orders.some(o => o.status === 'Filled')) loadTransactions()

      // Marj çağrısıyla zorla kapatılan pozisyonlar aynı push'ta gelir —
      // bu, özelliğin var olma sebebi, kaçırılmamalı.
      const liquidated = p.orders.filter(o => o.liquidated)
      if (liquidated.length > 0) {
        setLiquidations(prev => [
          ...prev,
          ...liquidated.map(o => ({
            id: o.id,
            symbol: o.symbol,
            quantity: o.quantity,
            amount: o.executedAmount ?? 0,
          })),
        ])
      }
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
  const pendingOrders = useMemo(() => orders.filter(o => o.status === 'Pending' || o.status === 'PartiallyFilled'), [orders])
  const pastOrders = useMemo(() => orders.filter(o => o.status !== 'Pending'), [orders])
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

  const livePnl = useMemo(() => {
    if (!balance) return null

    // price × quantity across the board — a short's quantity is negative, so the
    // shares owed subtract themselves. Matches PortfolioValueCalculator exactly.
    let positionValue = 0
    for (const p of Object.values(livePortfolio))
      positionValue += p.currentPrice * p.totalQuantity

    const portfolioValue =
      balance.freeCashBalance + balance.lockedCashBalance + positionValue

    return {
      portfolioValue,
      pnl: portfolioValue - balance.netDeposits,
      realizedPnl: balance.realizedProfitLoss,
    }
  }, [balance, livePortfolio])

  const holdings = Object.values(livePortfolio)
  const holdingsValue = holdings.reduce((s, p) => s + p.marketValue, 0)
  const openPL = holdings.reduce((s, p) => s + p.profitLoss, 0)
  // FreeCash + LockedCash + Σ(long qty x price) - Σ(|short qty| x price) — holdingsValue
  // already nets the two since a short's marketValue (price x a negative quantity) is negative.
  const equity = (balance?.total ?? 0) + holdingsValue

  // mirrors MarginCalculator.MaintenanceMarginRate on the backend
  const MAINTENANCE_MARGIN_RATE = 0.3
  const shortExposure = holdings
    .filter(p => p.isShort)
    .reduce((s, p) => s - p.marketValue, 0)   // marketValue is negative for a short; flip it positive
  const maintenanceRequirement = MAINTENANCE_MARGIN_RATE * shortExposure
  // warn a healthy buffer before the server's own liquidation trigger (equity < maintenanceRequirement)
  const marginCallActive = shortExposure > 0 && equity < maintenanceRequirement * 1.5

  // a Sell opens or adds to a short whenever there's no long position left to reduce —
  // mirrors PortfolioFillExecutor.Classify on the backend
  const sellWouldShort = !!chosen && (livePortfolio[chosen.symbol]?.totalQuantity ?? 0) <= 0
  const previewQty = parseInt(qty, 10)
  const previewPrice = mode === 'limit' ? parseDecimal(limitPrice) : chosen?.currentPrice ?? NaN
  const marginPreview =
    sellWouldShort && Number.isFinite(previewQty) && previewQty >= 1 &&
    Number.isFinite(previewPrice) && previewPrice > 0
      ? 0.5 * previewQty * previewPrice
      : null

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
      const expiryParts = expiryPartsFromDate(expiryDate)
      body = {
        ...body,
        price,
        expiryDays: expiryParts.days,
        expiryHours: expiryParts.hours,
        expiryMinutes: expiryParts.minutes,
      }
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
      if (mode === 'limit') {
        setLimitPrice(''); setStopPrice('')
        setExpiryDate('')
      }
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

  const replaceOrder = async (id: string) => {
    setNotice('')
    setReplacing(prev => new Set(prev).add(id))
    try {
      await api.post(`/api/order/${id}/replace`)
      loadBalance(); loadPortfolio(); loadOrders(); loadTransactions()
    } catch (e: any) {
      setNotice(e.response ? tServer(e.response.data) : t('err.orderFailed'))
    } finally {
      setReplacing(prev => {
        const next = new Set(prev)
        next.delete(id)
        return next
      })
    }
  }

  const dismissLiquidation = (id: string) =>
    setLiquidations(prev => prev.filter(l => l.id !== id))

  const pick = (i: Instrument) => {
  if (!i.isActive) return
  setSelected(prev => (prev === i.id ? null : i.id))
  setLimitPrice(prev => (prev === '' ? i.currentPrice.toFixed(2).replace('.', ',') : prev))
  loadHistory(i)
}

const loadHistory = (i: Instrument) => {
  if (seeded.current.has(i.symbol)) return
  seeded.current.add(i.symbol)

  const from = new Date(Date.now() - WINDOW_HOURS * 3600 * 1000).toISOString()
  api.get<PricePoint[]>(`/api/instruments/${i.id}/history`, { params: { from } })
    .then(r => {
      setHistory(prev => ({
        ...prev,
        [i.symbol]: [...r.data, ...(prev[i.symbol] ?? [])].slice(-MAX_POINTS)
      }))
    })
    .catch(() => seeded.current.delete(i.symbol))   // hata olursa tekrar denenebilsin
}

  const tapeRow = instruments.map(i => {
    const h = history[i.symbol] ?? []
    const base = h.length > 1 ? h[0].price : i.currentPrice
    const pct = base ? ((i.currentPrice - base) / base) * 100 : 0
    return { ...i, pct }
  })

  const portfolioInstruments = useMemo(
    () => filterSortInstruments(instruments.filter(i => portfolio[i.symbol]), query, sort),
    [instruments, portfolio, query, sort]
  )
  const marketStocks = useMemo(
    () => filterSortInstruments(instruments.filter(i => i.type === 'Stock'), marketQuery, marketSort),
    [instruments, marketQuery, marketSort]
  )

  const portfolioPaged = paginate(portfolioInstruments, portfolioPage)
  const marketPaged = paginate(marketStocks, marketPage, MARKET_PAGE_SIZE)
  const marketLeft = marketPaged.items.slice(0, MARKET_PAGE_SIZE / 2)
  const marketRight = marketPaged.items.slice(MARKET_PAGE_SIZE / 2)
  const pendingPaged = paginate(pendingOrders, pendingPage)
  const pastPaged = paginate(pastOrders, pastPage)
  const txPaged = paginate(transactions, txPage)


  return (
    <div className="shell">
      {menuOpen && (
        <div className="nav-backdrop" onClick={() => setMenuOpen(false)}>
          <nav className="nav-drawer" onClick={e => e.stopPropagation()}>
            <div className="nav-drawer-head">
              <span className="mark">Fin<em>Sim</em></span>
              <button className="ghost-btn" onClick={() => setMenuOpen(false)} aria-label={t('app.close')}>×</button>
            </div>
            <button
              className="nav-item"
              aria-pressed={view === 'portfolio'}
              onClick={() => { setView('portfolio'); setMenuOpen(false) }}
            >
              {t('nav.portfolio')}
            </button>
            <button
              className="nav-item"
              aria-pressed={view === 'market'}
              onClick={() => { setView('market'); setMenuOpen(false) }}
            >
              {t('nav.market')}
            </button>
            {balance?.isAdmin && (
              <button
                className="nav-item"
                aria-pressed={view === 'admin'}
                onClick={() => { setView('admin'); setMenuOpen(false) }}
              >
                {t('admin.panelButton')}
              </button>
            )}
          </nav>
        </div>
      )}

      <header className="rail" ref={railRef}>
        <button
          className="nav-toggle"
          style={{ left: navTogglePos }}
          onClick={() => setMenuOpen(true)}
          aria-label={t('nav.toggle')}
        >
          <span />
          <span />
          <span />
        </button>
        <div className="wrap rail-in">
          <span ref={logoRef}>
            <Logomark size={26} />
          </span>
          <span className="mark">Fin<em>Sim</em></span>
          <span className="mark-sub">{t('app.tagline')}</span>
          <span className="rail-spacer" />
          <span style={{ fontFamily: 'var(--mono)', fontSize: 13, display: 'inline-flex', alignItems: 'baseline', gap: 6 }}>
            {online && <span className="live-dot" aria-hidden="true" />}
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
          {balance && balance.lockedCashBalance !== 0 && (
            <div className="cell-delta flat">{t('strip.locked', { n: fmt(balance.lockedCashBalance) })}</div>
          )}
          {balance && balance.marginUsed > 0 && (
            <div className="cell-delta amber">{t('strip.margin', { n: fmt(balance.marginUsed) })}</div>
          )}
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

      {liquidations.length > 0 && (
        <div className="liq-stack" role="alert">
          {liquidations.map(l => (
            <div className="liq-toast" key={l.id}>
              <span className="liq-icon">⚠</span>
              <div className="liq-body">
                <strong>{t('alert.liquidatedTitle')}</strong>
                <span>{t('alert.liquidatedBody', {
                  symbol: l.symbol, qty: l.quantity, amount: `₺${fmt(l.amount)}`,
                })}</span>
              </div>
              <button onClick={() => dismissLiquidation(l.id)} aria-label={t('app.close')}>×</button>
            </div>
          ))}
        </div>
      )}

      <main className="wrap">
        {view === 'admin' ? <Admin onClose={() => setView('portfolio')} /> : view === 'market' ? (
          <div className="market-page">
            <div className="section-head">
              <h2>{t('nav.market')}</h2>
              <span className="section-note">{t('board.otherNote', { n: marketStocks.length })}</span>
              <Pager page={marketPaged.page} totalPages={marketPaged.totalPages} onChange={setMarketPage} />
            </div>

            <div className="board-controls">
              <div className="search-input">
                <input
                  className="field-input"
                  type="text"
                  value={marketQuery}
                  onChange={e => setMarketQuery(e.target.value)}
                  placeholder={t('search.placeholder')}
                />
                {marketQuery && (
                  <button
                    className="ghost-btn"
                    onClick={() => setMarketQuery('')}
                    aria-label={t('app.close')}
                  >
                    ×
                  </button>
                )}
              </div>
              <select className="field-input" value={marketSort} onChange={e => setMarketSort(e.target.value)}>
                <option value="symbol-asc">{t('sort.symbolAsc')}</option>
                <option value="symbol-desc">{t('sort.symbolDesc')}</option>
                <option value="price-desc">{t('sort.priceDesc')}</option>
                <option value="price-asc">{t('sort.priceAsc')}</option>
              </select>
            </div>

            {marketStocks.length === 0 ? (
              <div className="empty-state">{marketQuery ? t('search.noResults') : t('board.otherEmpty')}</div>
            ) : (
              <div className="market-columns">
                <div className="board">
                  {marketLeft.map(i => (
                    <InstrumentRow
                      key={i.id}
                      i={i}
                      open={selected === i.id}
                      tick={ticks[i.symbol]}
                      pos={livePortfolio[i.symbol]}
                      sparkData={history[i.symbol] ?? []}
                      onClick={() => pick(i)}
                    />
                  ))}
                </div>
                <div className="board">
                  {marketRight.map(i => (
                    <InstrumentRow
                      key={i.id}
                      i={i}
                      open={selected === i.id}
                      tick={ticks[i.symbol]}
                      pos={livePortfolio[i.symbol]}
                      sparkData={history[i.symbol] ?? []}
                      onClick={() => pick(i)}
                    />
                  ))}
                </div>
              </div>
            )}
          </div>
        ) : (
        <>
        {marginCallActive && (
          <div className="notice warn">
            <span style={{ flex: 1 }}>{t('alert.marginCall')}</span>
          </div>
        )}

          {notice && (
          <div className="notice">
            <span style={{ flex: 1 }}>{notice}</span>
            <button onClick={() => setNotice('')} aria-label={t('app.close')}>×</button>
          </div>
        )}

        <div className="terminal-layout">
          <div className="terminal-left">
            <div className="board-controls">
              <div className="search-input">
                <input
                  className="field-input"
                  type="text"
                  value={query}
                  onChange={e => setQuery(e.target.value)}
                  placeholder={t('search.placeholder')}
                />
                {query && (
                  <button
                    className="ghost-btn"
                    onClick={() => setQuery('')}
                    aria-label={t('app.close')}
                  >
                    ×
                  </button>
                )}
              </div>
              <select className="field-input" value={sort} onChange={e => setSort(e.target.value)}>
                <option value="symbol-asc">{t('sort.symbolAsc')}</option>
                <option value="symbol-desc">{t('sort.symbolDesc')}</option>
                <option value="price-desc">{t('sort.priceDesc')}</option>
                <option value="price-asc">{t('sort.priceAsc')}</option>
              </select>
            </div>

            <div className="section-head">
              <h2>{t('board.portfolioTitle')}</h2>
              <span className="section-note">{t('board.portfolioNote', { n: portfolioInstruments.length })}</span>
              <Pager page={portfolioPaged.page} totalPages={portfolioPaged.totalPages} onChange={setPortfolioPage} />
            </div>

            {portfolioInstruments.length === 0 ? (
              <div className="empty-state">{query ? t('search.noResults') : t('board.portfolioEmpty')}</div>
            ) : (
              <div className="board">
                {portfolioPaged.items.map(i => (
                  <InstrumentRow
                    key={i.id}
                    i={i}
                    open={selected === i.id}
                    tick={ticks[i.symbol]}
                    pos={livePortfolio[i.symbol]}
                    sparkData={history[i.symbol] ?? []}
                    onClick={() => pick(i)}
                  />
                ))}
                {Array.from({ length: Math.max(0, PAGE_SIZE - portfolioPaged.items.length) }).map((_, idx) => (
                  <div className="row" key={`filler-${idx}`} aria-hidden="true">
                    <div className="row-head" style={{ visibility: 'hidden' }}>
                      <span className="row-sym">&nbsp;</span>
                    </div>
                  </div>
                ))}
              </div>
            )}

            <div className="panel">
              <div className="section-head">
                <h2>{t('pending.title')}</h2>
                <span className="section-note">{t('pending.note')}</span>
                <Pager page={pendingPaged.page} totalPages={pendingPaged.totalPages} onChange={setPendingPage} />
              </div>

              {pendingOrders.length === 0 ? (
                <div className="empty-state">{t('pending.empty')}</div>
              ) : (
                <OrderTable
                  orders={pendingPaged.items}
                  pending
                  now={now}
                  onCancel={cancelOrder}
                  onReplace={replaceOrder}
                  replacing={replacing}
                />
              )}
            </div>

            <div className="panel">
              <div className="section-head">
                <h2>{t('tx.title')}</h2>
                <span className="section-note">{t('tx.note')}</span>
                <Pager page={txPaged.page} totalPages={txPaged.totalPages} onChange={setTxPage} />
              </div>

              {transactions.length === 0 ? (
                <div className="empty-state">
                  {t('tx.empty')}
                </div>
              ) : (
                <div className="panel-scroll">
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
                      {txPaged.items.map(tx => (
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
                      {Array.from({ length: Math.max(0, PAGE_SIZE - txPaged.items.length) }).map((_, idx) => (
                        <tr key={`filler-${idx}`} className="filler-row" aria-hidden="true">
                          <td colSpan={6}>&nbsp;</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          </div>

          <div className="terminal-right">
            <PnlChart live={livePnl} />

            <div className="panel">
              <div className="section-head">
                <h2>{t('ledger.title')}</h2>
                <span className="section-note">{t('ledger.note')}</span>
                <Pager page={pastPaged.page} totalPages={pastPaged.totalPages} onChange={setPastPage} />
              </div>

              {pastOrders.length === 0 ? (
                <div className="empty-state">{t('ledger.empty')}</div>
              ) : (
                <OrderTable
                  orders={pastPaged.items}
                  pending={false}
                  now={now}
                  onCancel={cancelOrder}
                  onReplace={replaceOrder}
                  replacing={replacing}
                />
              )}
            </div>
          </div>
        </div>
        </>
        )}
      </main>

      {(view === 'portfolio' || view === 'market') && (
      <div className="ticket">
        <div className="wrap ticket-in">
          <div className="ticket-slot" style={{ minWidth: 172 }}>
            <span className="field-label">{t('ticket.expiry')}</span>
            <div className="expiry-fields">
              <input
                className="field-input"
                type="date"
                min={new Date().toISOString().slice(0, 10)}
                aria-label={t('ticket.expiryDate')}
                disabled={mode !== 'limit'}
                value={mode === 'limit' ? expiryDate : ''}
                onChange={e => setExpiryDate(e.target.value)}
              />
            </div>
          </div>

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

          {marginPreview !== null && (
            <span className="margin-preview">{t('ticket.marginPreview', { n: fmt(marginPreview) })}</span>
          )}

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

const ago = (iso: string, lang: string) => {
  const mins = Math.round((Date.now() - new Date(iso).getTime()) / 60000)
  const rtf = new Intl.RelativeTimeFormat(lang, { numeric: 'auto' })
  if (mins < 60) return rtf.format(-mins, 'minute')
  return rtf.format(-Math.round(mins / 60), 'hour')
}

const PNL_RANGES = [30, 90, 365] as const

function PnlChart({ live }: {
  live: { portfolioValue: number; pnl: number; realizedPnl: number } | null
}) {
  const { t, lang } = useLang()
  const [days, setDays] = useState<number>(30)
  const [data, setData] = useState<PnlPoint[]>([])
  const [hover, setHover] = useState<number | null>(null)

  useEffect(() => {
    let alive = true
    const load = () =>
      api.get<PnlPoint[]>(`/api/users/pnl-history?days=${days}`)
        .then(r => { if (alive) setData(r.data) })
        .catch(() => { /* interceptor handles 401 */ })

    load()
    // only the last point moves; a minute is plenty and keeps this off the tick path
    const id = setInterval(load, 60_000)
    return () => { alive = false; clearInterval(id) }
  }, [days])

  // The fetch supplies history; the tail is overwritten from the current tick so
  // the chart always ends at P&L *now*, not P&L as of the last poll.
  const series = useMemo(() => {
    if (!live || data.length === 0) return data
    const out = data.slice()
    const i = out.length - 1
    if (out[i].isLive) out[i] = { ...out[i], ...live }
    return out
  }, [data, live])

  const day = (iso: string) =>
    new Date(iso).toLocaleDateString(lang === 'tr' ? 'tr-TR' : 'en-US',
      { day: '2-digit', month: 'short' })

  const latest = series.length ? series[series.length - 1] : null
  const stroke = (latest?.pnl ?? 0) >= 0 ? 'var(--rise)' : 'var(--fall)'

  const ranges = (
    <div className="pnl-ranges">
      {PNL_RANGES.map(r => (
        <button key={r} className="ghost-btn" aria-pressed={days === r}
                onClick={() => setDays(r)}>
          {t(`pnl.range.${r}` as 'pnl.range.30')}
        </button>
      ))}
    </div>
  )

  // one point means the account has no history yet — a line needs two
  if (series.length < 2) {
    return (
      <div className="panel pnl-panel">
        <div className="section-head">
          <h2>{t('pnl.title')}</h2>
          {ranges}
        </div>
        <div className="empty-state">{t('pnl.empty')}</div>
      </div>
    )
  }

  const last = series.length - 1
  const values = series.map(p => p.pnl)
  // zero is always in frame: a chart of pure profit that never shows the
  // break-even line is just a squiggle with no reference point
  const lo = Math.min(0, ...values)
  const hi = Math.max(0, ...values)
  const pad = (hi - lo) * 0.12 || 1
  const min = lo - pad
  const span = (hi + pad) - min

  const px = (idx: number) => (idx / last) * 100
  const py = (value: number) => 40 - ((value - min) / span) * 40
  const zero = py(0)

  const pts = series.map((p, idx) => `${px(idx).toFixed(2)},${py(p.pnl).toFixed(2)}`)
  const id = (latest?.pnl ?? 0) >= 0 ? 'pnlu' : 'pnld'

  const onMove = (e: React.MouseEvent<HTMLDivElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    const frac = (e.clientX - rect.left) / rect.width
    setHover(Math.max(0, Math.min(last, Math.round(frac * last))))
  }

  const shown = series[Math.min(hover ?? last, last)]

  return (
    <div className="panel pnl-panel">
      <div className="section-head">
        <h2>{t('pnl.title')}</h2>
        {ranges}
      </div>

      <div className="pnl-head">
        <span className="pnl-value" style={{ color: stroke }}>
          {signed(shown.pnl)} ₺
        </span>
        <span className="pnl-date">
          {day(shown.date)}{shown.isLive ? ` · ${t('pnl.live')}` : ''}
        </span>
      </div>

      <div className="pnl-plot" onMouseMove={onMove} onMouseLeave={() => setHover(null)}>
        <svg className="spark-svg" viewBox="0 0 100 40" preserveAspectRatio="none" aria-hidden="true">
          <defs>
            <linearGradient id={id} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor={stroke} stopOpacity="0.30" />
              <stop offset="100%" stopColor={stroke} stopOpacity="0" />
            </linearGradient>
          </defs>

          <polygon points={`0,${zero} ${pts.join(' ')} 100,${zero}`} fill={`url(#${id})`} />

          <line x1="0" y1={zero} x2="100" y2={zero}
                stroke="var(--faint)" strokeWidth="1" strokeDasharray="3 3"
                vectorEffect="non-scaling-stroke" />

          <polyline points={pts.join(' ')} fill="none" stroke={stroke}
                    strokeWidth="1.5" vectorEffect="non-scaling-stroke" />

          {hover !== null && (
            <line x1={px(hover)} y1="0" x2={px(hover)} y2="40"
                  stroke="var(--edge)" strokeWidth="1" vectorEffect="non-scaling-stroke" />
          )}
        </svg>

        <i className="spark-dot"
           style={{
             left: `${px(hover ?? last)}%`,
             top: `${(py(shown.pnl) / 40) * 100}%`,
             background: stroke,
           }} />
      </div>

      <div className="pnl-foot">
        <span>{t('pnl.portfolioValue')} <strong>{fmt(shown.portfolioValue)}</strong></span>
        <span>{t('pnl.realized')} <strong>{signed(shown.realizedPnl)}</strong></span>
      </div>
    </div>
  )
}

function AreaSpark({ data, className }: { data: PricePoint[]; className: string }) {
  const [hover, setHover] = useState<number | null>(null)
  if (data.length < 2) return null
  const { lang } = useLang()
  const prices = data.map(p => p.price)
  const min = Math.min(...prices)
  const max = Math.max(...prices)
  const range = max - min || 1
  const last = data.length - 1

  const px = (idx: number) => (idx / last) * 100
  const py = (idx: number) => 30 - ((data[idx].price - min) / range) * 26

  const pts = data.map((_, idx) => `${px(idx).toFixed(2)},${py(idx).toFixed(2)}`)
  const maxVol = Math.max(...data.map(p => p.volume), 1)
  const barW = 100 / data.length

  const rising = data[last].price >= data[0].price
  const stroke = rising ? 'var(--rise)' : 'var(--fall)'
  const id = `g${rising ? 'u' : 'd'}`

  const onMove = (e: React.MouseEvent<HTMLDivElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    const frac = (e.clientX - rect.left) / rect.width
    setHover(Math.max(0, Math.min(last, Math.round(frac * last))))
  }

  return (
    <div className={`spark-wrap ${className}`} onMouseMove={onMove} onMouseLeave={() => setHover(null)}>
      <svg className="spark-svg" viewBox="0 0 100 30" preserveAspectRatio="none" aria-hidden="true">
        <defs>
          <linearGradient id={id} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={stroke} stopOpacity="0.34" />
            <stop offset="100%" stopColor={stroke} stopOpacity="0" />
          </linearGradient>
        </defs>
        {data.map((p, idx) => p.volume > 0 && (
        <rect key={idx}
              x={px(idx) - barW / 2} width={barW * 0.8}
              y={30 - (p.volume / maxVol) * 7} height={(p.volume / maxVol) * 7}
              fill={stroke} opacity="0.25" />
      ))}
        <polygon points={`0,30 ${pts.join(' ')} 100,30`} fill={`url(#${id})`} />
        <polyline points={pts.join(' ')} fill="none" stroke={stroke}
                  strokeWidth="1.5" vectorEffect="non-scaling-stroke" />
        {hover !== null && (
          <>
            <line x1={px(hover)} y1="0" x2={px(hover)} y2="30"
                stroke="var(--edge)" strokeWidth="1" vectorEffect="non-scaling-stroke" />
          </>
        )}
      </svg>
      {hover !== null && (
        <i className="spark-dot"
           style={{ left: `${px(hover)}%`, top: `${(py(hover) / 30) * 100}%`, background: stroke }} />
      )}
      {hover !== null && (
        <div className="spark-tip">
          <strong>{fmt(data[hover].price)}</strong>
          <span>{ago(data[hover].timestamp, lang)}</span>
          <span>{data[hover].volume > 0 ? `${data[hover].volume} adet` : '—'}</span>
        </div>
      )}
    </div>
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