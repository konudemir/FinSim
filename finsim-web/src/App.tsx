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

export const PAGE_SIZE = 5
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

type Paged<T> = { items: T[]; nextCursor: string | null }

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

export const signed = (n: number) => (n >= 0 ? '+' : '−') + fmt(Math.abs(n))

export const dirOf = (n: number) => (n > 0 ? 'up' : n < 0 ? 'down' : 'flat')

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

export function paginate<T>(items: T[], page: number, pageSize = PAGE_SIZE) {
  const totalPages = Math.max(1, Math.ceil(items.length / pageSize))
  const clampedPage = Math.min(Math.max(page, 1), totalPages)
  return { items: items.slice((clampedPage - 1) * pageSize, clampedPage * pageSize), totalPages, page: clampedPage }
}

// Right-aligned page control. With up to 10 pages, listing every page number
// (1 2 3 ... 9 10) is noisy — prev/next plus a "page / total" label scales to
// any page count without it, and collapses to a bare "1" when there's nothing
// to page through.
export function Pager({ page, totalPages, onChange }: { page: number; totalPages: number; onChange: (page: number) => void }) {
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

function useOrderPage(open: boolean) {
  const [items, setItems] = useState<Order[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [pageCursor, setPageCursor] = useState<string | null>(null)
  const [stack, setStack] = useState<(string | null)[]>([])

  const load = () =>
    api.get<Paged<Order>>('/api/order', { params: { limit: PAGE_SIZE, open } })
      .then(r => {
        setItems(r.data.items)
        setNextCursor(r.data.nextCursor)
        setPageCursor(null)
        setStack([])
      })
      .catch(console.error)

  const next = () => {
    if (!nextCursor) return
    const cursor = nextCursor
    api.get<Paged<Order>>('/api/order', { params: { limit: PAGE_SIZE, open, cursor } })
      .then(r => {
        setStack(prev => [...prev, pageCursor])
        setItems(r.data.items)
        setNextCursor(r.data.nextCursor)
        setPageCursor(cursor)
      })
      .catch(console.error)
  }

  const prev = () => {
    if (stack.length === 0) return
    const prevCursor = stack[stack.length - 1]
    api.get<Paged<Order>>('/api/order', { params: { limit: PAGE_SIZE, open, cursor: prevCursor ?? undefined } })
      .then(r => {
        setStack(p => p.slice(0, -1))
        setItems(r.data.items)
        setNextCursor(r.data.nextCursor)
        setPageCursor(prevCursor)
      })
      .catch(console.error)
  }

  const reload = () =>
    api.get<Paged<Order>>('/api/order', { params: { limit: PAGE_SIZE, open, cursor: pageCursor ?? undefined } })
      .then(r => {
        setItems(r.data.items)
        setNextCursor(r.data.nextCursor)
      })
      .catch(console.error)

  return { items, nextCursor, stack, load, next, prev, reload }
}

function useBoardPage(sort: string, q: string) {
  const [items, setItems] = useState<Instrument[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [pageCursor, setPageCursor] = useState<string | null>(null)
  const [stack, setStack] = useState<(string | null)[]>([])

  const load = () =>
    api.get<Paged<Instrument>>('/api/instruments/board', { params: { limit: MARKET_PAGE_SIZE, sort, q } })
      .then(r => {
        setItems(r.data.items)
        setNextCursor(r.data.nextCursor)
        setPageCursor(null)
        setStack([])
      })
      .catch(console.error)

  const next = () => {
    if (!nextCursor) return
    const cursor = nextCursor
    api.get<Paged<Instrument>>('/api/instruments/board', { params: { limit: MARKET_PAGE_SIZE, sort, q, cursor } })
      .then(r => {
        setStack(prev => [...prev, pageCursor])
        setItems(r.data.items)
        setNextCursor(r.data.nextCursor)
        setPageCursor(cursor)
      })
      .catch(console.error)
  }

  const prev = () => {
    if (stack.length === 0) return
    const prevCursor = stack[stack.length - 1]
    api.get<Paged<Instrument>>('/api/instruments/board', { params: { limit: MARKET_PAGE_SIZE, sort, q, cursor: prevCursor ?? undefined } })
      .then(r => {
        setStack(p => p.slice(0, -1))
        setItems(r.data.items)
        setNextCursor(r.data.nextCursor)
        setPageCursor(prevCursor)
      })
      .catch(console.error)
  }

  return { items, nextCursor, stack, load, next, prev }
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


const InstrumentRow = memo(function InstrumentRow({
  i, open, tick, pos, sparkData, onClick, isFavorite, onToggleFavorite, onExpand,
}: {
  i: Instrument
  open: boolean
  tick: Tick | undefined
  pos: PortfolioItem | undefined
  sparkData: PricePoint[]
  onClick: () => void
  isFavorite: boolean
  onToggleFavorite: () => void
  onExpand: () => void
}) {
  const { t } = useLang()
  return (
    <div className="row" data-open={open}>
      <div className="row-line">
        <button
          type="button"
          className="row-fav"
          aria-pressed={isFavorite}
          aria-label={t(isFavorite ? 'board.unfavorite' : 'board.favorite')}
          onClick={e => { e.stopPropagation(); onToggleFavorite() }}
        >
          {isFavorite ? '♥' : '♡'}
        </button>
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
        <span className="row-px" data-tick={tick} key={i.currentPrice}>
          {fmt(i.currentPrice)}
        </span>
        </button>
      </div>
      <div className="row-body">
        <div className="row-body-in">
          {open && (
            <>
              <button
                type="button"
                className="row-spark-expand"
                onClick={e => { e.stopPropagation(); onExpand() }}
                aria-label={t('board.fullscreen')}
              >
                ⛶
              </button>
              <AreaSpark data={sparkData} className="row-spark" />
            </>
          )}
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
  const openOrders = useOrderPage(true)
  const closedOrders = useOrderPage(false)
  const [transactions, setTransactions] = useState<Transaction[]>([])
  const [txCursor, setTxCursor] = useState<string | null>(null)
  const [txPageCursor, setTxPageCursor] = useState<string | null>(null)
  const [txCursorStack, setTxCursorStack] = useState<(string | null)[]>([])
  const [marketMove, setMarketMove] = useState(0)
  const [favorites, setFavorites] = useState<Set<string>>(new Set())
  const [showFavorites, setShowFavorites] = useState(false)
  const [favPage, setFavPage] = useState(1)
// Must match MarketTickWorker.Every in src/FinSim.Api/BackgroundWorker.cs — the
// worker writes one PriceHistory row per instrument at that cadence, and this
// constant is how many seconds of real history each chart point represents.
const TICK_SECONDS = 15
const WINDOW_HOURS = 24
const MAX_POINTS = (WINDOW_HOURS * 3600) / TICK_SECONDS
const [history, setHistory] = useState<Record<string, PricePoint[]>>({})
const seeded = useRef<Set<string>>(new Set())
  const [selected, setSelected] = useState<string | null>(null)
  const [selectedPanel, setSelectedPanel] = useState<string | null>(null)
  const [fullscreenInstrumentId, setFullscreenInstrumentId] = useState<string | null>(null)
  const [fullscreenHistory, setFullscreenHistory] = useState<PricePoint[]>([])
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
  const menuCloseTimer = useRef<number | null>(null)

  const openMenu = () => {
    if (menuCloseTimer.current !== null) {
      window.clearTimeout(menuCloseTimer.current)
      menuCloseTimer.current = null
    }
    setMenuOpen(true)
  }

  const scheduleCloseMenu = () => {
    menuCloseTimer.current = window.setTimeout(() => setMenuOpen(false), 150)
  }

  useEffect(() => () => {
    if (menuCloseTimer.current !== null) window.clearTimeout(menuCloseTimer.current)
  }, [])
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState('symbol-asc')
  const [marketQuery, setMarketQuery] = useState('')
  const [marketSort, setMarketSort] = useState('symbol_asc')
  const [debouncedMarketQuery, setDebouncedMarketQuery] = useState('')

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedMarketQuery(marketQuery), 300)
    return () => window.clearTimeout(timer)
  }, [marketQuery])

  const board = useBoardPage(marketSort, debouncedMarketQuery)

  useEffect(() => {
    board.load()
  }, [marketSort, debouncedMarketQuery])

  const [portfolioPage, setPortfolioPage] = useState(1)
  const loadTransactions = () =>
    api.get<Paged<Transaction>>('/api/transactions', { params: { limit: PAGE_SIZE } })
      .then(r => {
        setTransactions(r.data.items)
        setTxCursor(r.data.nextCursor)
        setTxPageCursor(null)
        setTxCursorStack([])
      })
      .catch(console.error)

  const nextTxPage = () => {
    if (!txCursor) return
    const cursor = txCursor
    api.get<Paged<Transaction>>('/api/transactions', { params: { limit: PAGE_SIZE, cursor } })
      .then(r => {
        setTxCursorStack(prev => [...prev, txPageCursor])
        setTransactions(r.data.items)
        setTxCursor(r.data.nextCursor)
        setTxPageCursor(cursor)
      })
      .catch(console.error)
  }

  const prevTxPage = () => {
    if (txCursorStack.length === 0) return
    const prevCursor = txCursorStack[txCursorStack.length - 1]
    api.get<Paged<Transaction>>('/api/transactions', { params: { limit: PAGE_SIZE, cursor: prevCursor ?? undefined } })
      .then(r => {
        setTxCursorStack(prev => prev.slice(0, -1))
        setTransactions(r.data.items)
        setTxCursor(r.data.nextCursor)
        setTxPageCursor(prevCursor)
      })
      .catch(console.error)
  }

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
    api.get<string[]>('/api/favorites')
      .then(res => setFavorites(new Set(res.data)))
      .catch(console.error)
    openOrders.load()
    closedOrders.load()
    loadTransactions()
    loadBalance()
    loadPortfolio()
  }, [])

  const toggleFavorite = (i: Instrument) => {
    const isFav = favorites.has(i.id)
    setFavorites(prev => {
      const next = new Set(prev)
      if (isFav) next.delete(i.id); else next.add(i.id)
      return next
    })
    const req = isFav
      ? api.delete(`/api/favorites/${i.id}`)
      : api.post(`/api/favorites/${i.id}`)
    req.catch(() => {
      setFavorites(prev => {
        const next = new Set(prev)
        if (isFav) next.add(i.id); else next.delete(i.id)
        return next
      })
    })
  }

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
      openOrders.reload()

      // Dolan ya da iptal edilen bir emir iki panel arasında taşınır; ikisi de
      // yeniden çekilmeli. Sabit boyutlu bir sayfaya satır eklemek onu
      // PAGE_SIZE'ın ötesine büyütür, o yüzden merge değil refetch.
      if (p.orders.some(o => o.status === 'Filled' || o.status === 'Cancelled')) {
        closedOrders.reload()
        loadTransactions()
      }

      setBalance(prev => ({ ...p.balance, isAdmin: prev?.isAdmin ?? false }))

      const map: Record<string, PortfolioItem> = {}
      for (const item of p.portfolio) map[item.symbol] = item
      setPortfolio(map)

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
      openOrders.load(); closedOrders.load(); loadBalance(); loadPortfolio(); loadTransactions()
    })
    conn.onclose(() => setOnline(false))
    conn.start().then(() => setOnline(true)).catch(() => setOnline(false))
    return () => { conn.stop() }
  }, [])

  const chosen = instruments.find(i => i.id === selected) ?? null
  const fullscreenInstrument = instruments.find(i => i.id === fullscreenInstrumentId) ?? null
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

  const fullscreenPos = fullscreenInstrument ? livePortfolio[fullscreenInstrument.symbol] : undefined

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
    openOrders.load(); closedOrders.load()
    loadTransactions()
  }

  const cancelOrder = async (id: string) => {
    setNotice('')
    try {
      await api.post(`/api/order/${id}/cancel`)
    } catch (e: any) {
      setNotice(e.response ? tServer(e.response.data) : t('err.cancelFailed'))
    }
    loadBalance(); loadPortfolio(); openOrders.load(); closedOrders.load(); loadTransactions()
  }

  const replaceOrder = async (id: string) => {
    setNotice('')
    setReplacing(prev => new Set(prev).add(id))
    try {
      await api.post(`/api/order/${id}/replace`)
      loadBalance(); loadPortfolio(); openOrders.load(); closedOrders.load(); loadTransactions()
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

  const pick = (i: Instrument, panel: string) => {
  if (!i.isActive) return
  const isOpen = selected === i.id && selectedPanel === panel
  setSelected(isOpen ? null : i.id)
  setSelectedPanel(isOpen ? null : panel)
  setLimitPrice(prev => (prev === '' ? i.currentPrice.toFixed(2).replace('.', ',') : prev))
  loadHistory(i)
}

  // Opening the fullscreen panel always selects that instrument for the
  // trade ticket (rather than toggling like pick() does), so the ticket
  // duplicated inside the panel comes up with it already chosen.
  const openFullscreen = (i: Instrument) => {
    setFullscreenInstrumentId(i.id)
    setSelected(i.id)
    setSelectedPanel('fullscreen')
    setLimitPrice(prev => (prev === '' ? i.currentPrice.toFixed(2).replace('.', ',') : prev))
    loadHistory(i)

    // The board sparkline only ever holds the last 24h (WINDOW_HOURS). The
    // fullscreen chart is meant to show the whole run, so it gets its own
    // fetch — the API clamps any range over 30 days, which in practice is
    // "everything" for an instrument that's only ever run in this sim.
    setFullscreenHistory([])
    api.get<PricePoint[]>(`/api/instruments/${i.id}/history`, { params: { from: '2000-01-01T00:00:00Z' } })
      .then(r => setFullscreenHistory(r.data))
      .catch(console.error)
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
  const favoriteInstruments = useMemo(
    () => filterSortInstruments(instruments.filter(i => favorites.has(i.id)), '', 'symbol-asc'),
    [instruments, favorites]
  )

  const portfolioPaged = paginate(portfolioInstruments, portfolioPage)
  const favPaged = paginate(favoriteInstruments, favPage)
  const marketLeft = board.items.slice(0, MARKET_PAGE_SIZE / 2)
  const marketRight = board.items.slice(MARKET_PAGE_SIZE / 2)

  // Shared by the page-bottom ticket and the copy duplicated inside the
  // instrument fullscreen panel — both trade whatever "chosen" currently is.
  // idPrefix keeps the two copies' input ids from colliding when both are
  // mounted at once.
  const renderTicketFields = (idPrefix: string) => (
    <>
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
        <label className="field-label" htmlFor={`${idPrefix}-qty`}>{t('ticket.qty')}</label>
        <input
          id={`${idPrefix}-qty`}
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
        <label className="field-label" htmlFor={`${idPrefix}-lmt`}>{t('ticket.limitPrice')}</label>
        <input
          id={`${idPrefix}-lmt`}
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
        <label className="field-label" htmlFor={`${idPrefix}-stp`}>{t('ticket.stopPrice')}</label>
        <input
          id={`${idPrefix}-stp`}
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
    </>
  )

  return (
    <div className="shell">
      {menuOpen && (
        <div className="nav-backdrop" onClick={() => setMenuOpen(false)}>
          <nav
            className="nav-drawer"
            onClick={e => e.stopPropagation()}
            onMouseEnter={openMenu}
            onMouseLeave={scheduleCloseMenu}
          >
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
          onMouseEnter={openMenu}
          onMouseLeave={scheduleCloseMenu}
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
          <button
            className="ghost-btn"
            aria-pressed={showFavorites}
            onClick={() => setShowFavorites(v => !v)}
          >
            ♥ {t('nav.favorites')}
          </button>
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
              <span className="section-note">{t('board.otherNote', { n: board.items.length })}</span>
              <div className="pager">
                <button
                  type="button"
                  className="ghost-btn"
                  disabled={board.stack.length === 0}
                  onClick={board.prev}
                  aria-label={t('pager.prev')}
                >
                  ‹
                </button>
                <button
                  type="button"
                  className="ghost-btn"
                  disabled={board.nextCursor == null}
                  onClick={board.next}
                  aria-label={t('pager.next')}
                >
                  ›
                </button>
              </div>
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
                <option value="symbol_asc">{t('sort.symbolAsc')}</option>
                <option value="symbol_desc">{t('sort.symbolDesc')}</option>
                <option value="price_desc">{t('sort.priceDesc')}</option>
                <option value="price_asc">{t('sort.priceAsc')}</option>
              </select>
            </div>

            {board.items.length === 0 ? (
              <div className="empty-state">{marketQuery ? t('search.noResults') : t('board.otherEmpty')}</div>
            ) : (
              <div className="market-columns">
                <div className="board">
                  {marketLeft.map(i => (
                    <InstrumentRow
                      key={i.id}
                      i={i}
                      open={selected === i.id && selectedPanel === 'market'}
                      tick={ticks[i.symbol]}
                      pos={livePortfolio[i.symbol]}
                      sparkData={history[i.symbol] ?? []}
                      onClick={() => pick(i, 'market')}
                      isFavorite={favorites.has(i.id)}
                      onToggleFavorite={() => toggleFavorite(i)}
                      onExpand={() => openFullscreen(i)}
                    />
                  ))}
                </div>
                <div className="board">
                  {marketRight.map(i => (
                    <InstrumentRow
                      key={i.id}
                      i={i}
                      open={selected === i.id && selectedPanel === 'market'}
                      tick={ticks[i.symbol]}
                      pos={livePortfolio[i.symbol]}
                      sparkData={history[i.symbol] ?? []}
                      onClick={() => pick(i, 'market')}
                      isFavorite={favorites.has(i.id)}
                      onToggleFavorite={() => toggleFavorite(i)}
                      onExpand={() => openFullscreen(i)}
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

        <div className="terminal-layout" data-favs={showFavorites}>
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
                    open={selected === i.id && selectedPanel === 'portfolio'}
                    tick={ticks[i.symbol]}
                    pos={livePortfolio[i.symbol]}
                    sparkData={history[i.symbol] ?? []}
                    onClick={() => pick(i, 'portfolio')}
                    isFavorite={favorites.has(i.id)}
                    onToggleFavorite={() => toggleFavorite(i)}
                    onExpand={() => openFullscreen(i)}
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
              </div>

              {openOrders.items.length === 0 ? (
                <div className="empty-state">{t('pending.empty')}</div>
              ) : (
                <OrderTable
                  orders={openOrders.items}
                  pending
                  now={now}
                  onCancel={cancelOrder}
                  onReplace={replaceOrder}
                  replacing={replacing}
                />
              )}
              <div className="pager">
                <button
                  type="button"
                  className="ghost-btn"
                  disabled={openOrders.stack.length === 0}
                  onClick={openOrders.prev}
                  aria-label={t('pager.prev')}
                >
                  ‹
                </button>
                <button
                  type="button"
                  className="ghost-btn"
                  disabled={openOrders.nextCursor == null}
                  onClick={openOrders.next}
                  aria-label={t('pager.next')}
                >
                  ›
                </button>
              </div>
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
                      {Array.from({ length: Math.max(0, PAGE_SIZE - transactions.length) }).map((_, idx) => (
                        <tr key={`filler-${idx}`} className="filler-row" aria-hidden="true">
                          <td colSpan={6}>&nbsp;</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
              <div className="pager">
                <button
                  type="button"
                  className="ghost-btn"
                  disabled={txCursorStack.length === 0}
                  onClick={prevTxPage}
                  aria-label={t('pager.prev')}
                >
                  ‹
                </button>
                <button
                  type="button"
                  className="ghost-btn"
                  disabled={txCursor == null}
                  onClick={nextTxPage}
                  aria-label={t('pager.next')}
                >
                  ›
                </button>
              </div>
            </div>
          </div>

          <div className="terminal-right">
            <PnlChart live={livePnl} />

            <div className="panel">
              <div className="section-head">
                <h2>{t('ledger.title')}</h2>
                <span className="section-note">{t('ledger.note')}</span>
              </div>

              {closedOrders.items.length === 0 ? (
                <div className="empty-state">{t('ledger.empty')}</div>
              ) : (
                <OrderTable
                  orders={closedOrders.items}
                  pending={false}
                  now={now}
                  onCancel={cancelOrder}
                  onReplace={replaceOrder}
                  replacing={replacing}
                />
              )}
              <div className="pager">
                <button
                  type="button"
                  className="ghost-btn"
                  disabled={closedOrders.stack.length === 0}
                  onClick={closedOrders.prev}
                  aria-label={t('pager.prev')}
                >
                  ‹
                </button>
                <button
                  type="button"
                  className="ghost-btn"
                  disabled={closedOrders.nextCursor == null}
                  onClick={closedOrders.next}
                  aria-label={t('pager.next')}
                >
                  ›
                </button>
              </div>
            </div>
          </div>

          {showFavorites && (
            <div className="terminal-fav">
              <div className="section-head">
                <h2>{t('nav.favorites')}</h2>
                <span className="section-note">{t('board.favoritesNote', { n: favoriteInstruments.length })}</span>
                <button className="ghost-btn" onClick={() => setShowFavorites(false)} aria-label={t('app.close')}>
                  ×
                </button>
              </div>
              {favoriteInstruments.length === 0 ? (
                <div className="empty-state">{t('board.favoritesEmpty')}</div>
              ) : (
                <>
                  <div className="board">
                    {favPaged.items.map(i => (
                      <InstrumentRow
                        key={i.id}
                        i={i}
                        open={selected === i.id && selectedPanel === 'favorites'}
                        tick={ticks[i.symbol]}
                        pos={livePortfolio[i.symbol]}
                        sparkData={history[i.symbol] ?? []}
                        onClick={() => pick(i, 'favorites')}
                        isFavorite
                        onToggleFavorite={() => toggleFavorite(i)}
                        onExpand={() => openFullscreen(i)}
                      />
                    ))}
                  </div>
                  <Pager page={favPaged.page} totalPages={favPaged.totalPages} onChange={setFavPage} />
                </>
              )}
            </div>
          )}
        </div>
        </>
        )}
      </main>

      {(view === 'portfolio' || view === 'market') && (
      <div className="ticket">
        <div className="wrap ticket-in">
          {renderTicketFields('ticket')}
        </div>
      </div>
      )}

      {fullscreenInstrument && (
        <InstrumentFullscreen
          i={fullscreenInstrument}
          pos={fullscreenPos}
          tick={ticks[fullscreenInstrument.symbol]}
          history={fullscreenHistory}
          isFavorite={favorites.has(fullscreenInstrument.id)}
          onToggleFavorite={() => toggleFavorite(fullscreenInstrument)}
          onClose={() => setFullscreenInstrumentId(null)}
          renderTicketFields={renderTicketFields}
        />
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
  const dataMin = Math.min(...prices)
  const dataMax = Math.max(...prices)
  const pad = (dataMax - dataMin) * 0.15 || dataMax * 0.05 || 1
  const min = Math.max(0, dataMin - pad)
  const top = dataMax + pad
  const range = top - min || 1
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


function InstrumentFullscreen({
  i, pos, tick, history, isFavorite, onToggleFavorite, onClose, renderTicketFields,
}: {
  i: Instrument
  pos: PortfolioItem | undefined
  tick: Tick | undefined
  history: PricePoint[]
  isFavorite: boolean
  onToggleFavorite: () => void
  onClose: () => void
  renderTicketFields: (idPrefix: string) => React.ReactNode
}) {
  const { t } = useLang()

  const open = history[0]?.price ?? i.currentPrice
  const prices = history.length ? history.map(p => p.price) : [i.currentPrice]
  const high = Math.max(...prices, i.currentPrice)
  const low = Math.min(...prices, i.currentPrice)
  const volume = history.reduce((sum, p) => sum + p.volume, 0)
  const change = i.currentPrice - open
  const changePct = open ? (change / open) * 100 : 0
  const rising = change >= 0

  return (
    <div className="instrument-fullscreen-backdrop" onClick={onClose}>
      <div className="instrument-fullscreen-panel" data-trend={rising ? 'up' : 'down'} onClick={e => e.stopPropagation()}>
        <div className="fs-glow" aria-hidden="true" />

        <div className="fs-head">
          <button
            type="button"
            className="row-fav fs-fav"
            aria-pressed={isFavorite}
            aria-label={t(isFavorite ? 'board.unfavorite' : 'board.favorite')}
            onClick={onToggleFavorite}
          >
            {isFavorite ? '♥' : '♡'}
          </button>

          <div className="fs-title">
            <div className="fs-sym-row">
              <span className="fs-sym">{i.symbol}</span>
              {i.type === 'Fund' && <span className="fund-badge">{t('board.fundBadge')}</span>}
              {pos?.isShort && <span className="short-badge">{t('board.shortBadge')}</span>}
              {!i.isActive && <span className="empty">{t('board.closed')}</span>}
            </div>
            <span className="fs-name">{i.name}</span>
          </div>

          <div className="fs-price-block" data-tick={tick} key={i.currentPrice}>
            <span className="fs-price">{fmt(i.currentPrice)}</span>
            <span className={`fs-change ${dirOf(change)}`}>
              {rising ? '▲' : '▼'} {signed(change)} ({changePct >= 0 ? '+' : ''}{changePct.toFixed(2)}%)
            </span>
          </div>

          <button type="button" className="ghost-btn fs-close" onClick={onClose} aria-label={t('app.close')}>
            ×
          </button>
        </div>

        <div className="fs-stats">
          <div className="fs-stat">
            <span className="fs-stat-label">{t('fs.open')}</span>
            <span className="fs-stat-value">{fmt(open)}</span>
          </div>
          <div className="fs-stat">
            <span className="fs-stat-label">{t('fs.high')}</span>
            <span className="fs-stat-value rise">{fmt(high)}</span>
          </div>
          <div className="fs-stat">
            <span className="fs-stat-label">{t('fs.low')}</span>
            <span className="fs-stat-value fall">{fmt(low)}</span>
          </div>
          <div className="fs-stat">
            <span className="fs-stat-label">{t('fs.volume')}</span>
            <span className="fs-stat-value">{volume.toLocaleString('tr-TR')}</span>
          </div>
          {pos && (
            <div className="fs-stat">
              <span className="fs-stat-label">
                {pos.isShort
                  ? t('board.shortLots', { n: Math.abs(pos.totalQuantity) })
                  : t('board.lots', { n: pos.totalQuantity })}
              </span>
              <span className={`fs-stat-value ${dirOf(pos.profitLoss)}`}>{signed(pos.profitLoss)}</span>
            </div>
          )}
        </div>

        <div className="instrument-fullscreen-body">
          <div className="instrument-fullscreen-chart">
            <AreaSpark data={history} className="fullscreen-spark" />
            {history.length < 2 && <div className="fs-chart-empty">{t('fs.loading')}</div>}
          </div>
          <div className="ticket-in fullscreen-ticket">
            {renderTicketFields('fullscreen-ticket')}
          </div>
        </div>
      </div>
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