import { memo, useEffect, useMemo, useRef, useState } from 'react'
import { usePath, navigate, replacePath } from './router'
import PageNav from './PageNav'
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
import { sectorLabel } from './lang'

export const PAGE_SIZE = 5
const MARKET_PAGE_SIZE = 20

type Instrument = {
  id: string
  symbol: string
  name: string
  basePrice: number
  currentPrice: number
  isActive: boolean
  type: 'Stock' | 'Fund'
  sector: string | null
  industry: string | null
  description: string | null
  employees: number | null
  website: string | null
  city: string | null
  sharesOutstanding: number | null
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

export type Paged<T> = { items: T[]; page: number; pageSize: number; totalCount: number }

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

/**
 * Generic offset-paged list: goTo(n) fetches an arbitrary page, next/prev step
 * by one, reload() re-fetches whichever page is currently shown. `params` is
 * read fresh on every call (not captured once) so callers can pass an object
 * literal that changes across renders (sort, q, ...).
 */
export function usePagedList<T>(url: string, params: Record<string, unknown>, pageSize: number) {
  const [items, setItems] = useState<T[]>([])
  const [page, setPage] = useState(1)
  const [totalCount, setTotalCount] = useState(0)

  const fetchPage = (n: number) =>
    api.get<Paged<T>>(url, { params: { ...params, page: n, limit: pageSize } })
      .then(r => {
        setItems(r.data.items)
        setPage(r.data.page)
        setTotalCount(r.data.totalCount)
      })
      .catch(console.error)

  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const goTo = (n: number) => fetchPage(n)
  const next = () => goTo(page + 1)
  const prev = () => goTo(page - 1)
  const reload = () => fetchPage(page)
  const hasNext = page < totalPages
  const hasPrevious = page > 1

  return { items, page, totalPages, hasNext, hasPrevious, goTo, next, prev, reload }
}

function useOrderPage(open: boolean) {
  return usePagedList<Order>('/api/order', { open }, PAGE_SIZE)
}

function useBoardPage(sort: string, q: string) {
  return usePagedList<Instrument>('/api/instruments/board', { sort, q }, MARKET_PAGE_SIZE)
}

function usePortfolioBoardPage(sort: string, q: string) {
  return usePagedList<Instrument>('/api/users/portfolio/board', { sort, q }, PAGE_SIZE)
}

function useFavoritesBoardPage(sort: string) {
  return usePagedList<Instrument>('/api/favorites/board', { sort }, PAGE_SIZE)
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
  i, tick, pos, onClick, isFavorite, onToggleFavorite,
}: {
  i: Instrument
  tick: Tick | undefined
  pos: PortfolioItem | undefined
  onClick: () => void
  isFavorite: boolean
  onToggleFavorite: () => void
}) {
  const { t } = useLang()
  return (
    <div className="row">
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
          data-inactive={!i.isActive}
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
    </div>
  )
})

// URL <-> primary-board page/sort sync, so a page is linkable and survives a
// refresh. Only one board (market or portfolio) is visible at a time, so the
// two views share the same `page`/`sort` keys — whichever view is active reads
// and writes them.
function urlSort(): string | null {
  return new URLSearchParams(window.location.search).get('sort')
}

function urlPage(): number {
  const n = Number(new URLSearchParams(window.location.search).get('page'))
  return Number.isFinite(n) && n >= 1 ? n : 1
}

function setUrlParams(page: number, sort: string) {
  const params = new URLSearchParams(window.location.search)
  params.set('page', String(page))
  params.set('sort', sort)
  const qs = params.toString()
  window.history.replaceState(null, '', window.location.pathname + (qs ? `?${qs}` : ''))
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

  if (window.location.pathname === '/') {
    replacePath('/home')
  }

  return <Terminal onLogout={logout} />
}

function Terminal({ onLogout }: { onLogout: () => void }) {
  const { lang, toggle: toggleLang, t, tServer } = useLang()
  const { theme, toggle: toggleTheme } = useTheme()

  const [indexValue, setIndexValue] = useState(0)
  const [instruments, setInstruments] = useState<Instrument[]>([])
  const [instrumentsLoaded, setInstrumentsLoaded] = useState(false)
  const [balance, setBalance] = useState<Balance | null>(null)
  const [portfolio, setPortfolio] = useState<Record<string, PortfolioItem>>({})
  const openOrders = useOrderPage(true)
  const closedOrders = useOrderPage(false)
  const transactionsList = usePagedList<Transaction>('/api/transactions', {}, PAGE_SIZE)
  const [marketMove, setMarketMove] = useState(0)
  const [favorites, setFavorites] = useState<Set<string>>(new Set())
  const [showFavorites, setShowFavorites] = useState(false)
// Must match MarketTickWorker.Every in src/FinSim.Api/BackgroundWorker.cs — the
// worker writes one PriceHistory row per instrument at that cadence, and this
// constant is how many seconds of real history each chart point represents.
const TICK_SECONDS = 15
const WINDOW_HOURS = 24
const MAX_POINTS = (WINDOW_HOURS * 3600) / TICK_SECONDS
const [history, setHistory] = useState<Record<string, PricePoint[]>>({})
const seeded = useRef<Set<string>>(new Set())
  const [selected, setSelected] = useState<string | null>(null)
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

  const [ticks, setTicks] = useState<Record<string, Tick>>({})
  const prevPrices = useRef<Record<string, number>>({})
  const [online, setOnline] = useState(true)
  const pathname = usePath()
  // /stocks/:symbol opens as an overlay on top of whatever page was already
  // showing, so the background view tracks the last non-overlay path
  // instead of flipping to 'portfolio' while a stock page is open. A cold
  // load straight into /stocks/:symbol has no prior page to fall back to,
  // so default it to the market board rather than the stock page itself.
  const bgPathRef = useRef(pathname.startsWith('/stocks/') ? '/market' : pathname)
  if (!pathname.startsWith('/stocks/')) bgPathRef.current = pathname
  const view: 'portfolio' | 'market' | 'admin' =
    bgPathRef.current === '/market' ? 'market' : bgPathRef.current === '/admin' ? 'admin' : 'portfolio'
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState(() => urlSort() ?? 'symbol_asc')
  const [marketQuery, setMarketQuery] = useState('')
  const [marketSort, setMarketSort] = useState(() => urlSort() ?? 'symbol_asc')
  const [debouncedMarketQuery, setDebouncedMarketQuery] = useState('')
  const [debouncedQuery, setDebouncedQuery] = useState('')
  // No sort control in the favorites panel (matches the pre-pagination UI) —
  // symbol-asc via the board endpoint replaces the old client-side default sort.
  const favSort = 'symbol_asc'

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedMarketQuery(marketQuery), 300)
    return () => window.clearTimeout(timer)
  }, [marketQuery])

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedQuery(query), 300)
    return () => window.clearTimeout(timer)
  }, [query])

  const board = useBoardPage(marketSort, debouncedMarketQuery)
  const portfolioBoard = usePortfolioBoardPage(sort, debouncedQuery)
  const favoritesBoard = useFavoritesBoardPage(favSort)

  // The market view seeds its initial page from the URL (a linked/refreshed
  // page); every later sort/query change resets to page 1.
  const marketInitial = useRef(true)
  useEffect(() => {
    board.goTo(marketInitial.current && bgPathRef.current === '/market' ? urlPage() : 1)
    marketInitial.current = false
  }, [marketSort, debouncedMarketQuery])

  const portfolioInitial = useRef(true)
  useEffect(() => {
    portfolioBoard.goTo(portfolioInitial.current && bgPathRef.current !== '/market' ? urlPage() : 1)
    portfolioInitial.current = false
  }, [sort, debouncedQuery])

  useEffect(() => {
    favoritesBoard.goTo(1)
  }, [favSort])

  // Keep the URL's page/sort in sync with whichever board is currently active.
  useEffect(() => {
    if (bgPathRef.current !== '/market') return
    setUrlParams(board.page, marketSort)
  }, [board.page, marketSort])

  useEffect(() => {
    if (bgPathRef.current === '/market') return
    setUrlParams(portfolioBoard.page, sort)
  }, [portfolioBoard.page, sort])

  const loadBalance = () =>
    api.get<Balance>('/api/users/balance').then(r => setBalance(r.data)).catch(console.error)

  const loadPortfolio = () =>
    api.get<PortfolioItem[]>('/api/users/portfolio')
      .then(r => {
        const map: Record<string, PortfolioItem> = {}
        for (const p of r.data) map[p.symbol] = p
        setPortfolio(map)
        // A fill/cancel/replace can add or remove a row from the paged
        // portfolio board, so the current page needs an explicit refetch.
        portfolioBoard.reload()
      })
      .catch(console.error)

  useEffect(() => {
    api.get<Instrument[]>('/api/instruments')
      .then(res => {
        setInstruments(res.data)
        for (const i of res.data) prevPrices.current[i.symbol] = i.currentPrice
        // Seed the index from the initial snapshot so it reads correctly
        // right away instead of showing "—" until the first tick arrives
        // over the websocket (up to MarketTickWorker.Every seconds later).
        const stocks = res.data.filter(i => i.type === 'Stock' && i.basePrice > 0)
        if (stocks.length > 0) {
          const idx = Math.round(
            (stocks.reduce((sum, i) => sum + i.currentPrice / i.basePrice, 0) / stocks.length) * 10_000 * 100
          ) / 100
          setIndexValue(idx)
          setMarketMove(idx / 10_000 - 1)
        }
      })
      .catch(console.error)
      .finally(() => setInstrumentsLoaded(true))
    api.get<string[]>('/api/favorites')
      .then(res => setFavorites(new Set(res.data)))
      .catch(console.error)
    openOrders.goTo(1)
    closedOrders.goTo(1)
    transactionsList.goTo(1)
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
    req
      // The favorites board is paged server-side, so the toggle's membership
      // change won't show up until the current page is re-fetched.
      .then(() => favoritesBoard.reload())
      .catch(() => {
        setFavorites(prev => {
          const next = new Set(prev)
          if (isFav) next.add(i.id); else next.delete(i.id)
          return next
        })
      })
  }

  useEffect(() => {
    for (const i of instruments) {
      if (portfolio[i.symbol] || favorites.has(i.id)) loadHistory(i)
    }
  }, [instruments, portfolio, favorites])

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
        transactionsList.reload()
      }

      setBalance(prev => ({ ...p.balance, isAdmin: prev?.isAdmin ?? false }))

      const map: Record<string, PortfolioItem> = {}
      for (const item of p.portfolio) map[item.symbol] = item
      setPortfolio(map)
      // Same reasoning as loadPortfolio(): a fill pushed over SignalR can
      // change portfolio membership, so the current board page is stale.
      portfolioBoard.reload()

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
      openOrders.goTo(1); closedOrders.goTo(1); loadBalance(); loadPortfolio(); transactionsList.goTo(1)
    })
    conn.onclose(() => setOnline(false))
    conn.start().then(() => setOnline(true)).catch(() => setOnline(false))
    return () => { conn.stop() }
  }, [])

  const chosen = instruments.find(i => i.id === selected) ?? null
  const stockSymbol = pathname.startsWith('/stocks/') ? pathname.slice('/stocks/'.length).toLowerCase() : null
  const fullscreenInstrument = stockSymbol
    ? instruments.find(i => i.symbol.toLowerCase() === stockSymbol) ?? null
    : null

  useEffect(() => {
    document.title = stockSymbol
      ? (fullscreenInstrument?.symbol ?? stockSymbol.toUpperCase())
      : view === 'admin'
      ? t('admin.panelButton')
      : view === 'market'
      ? t('nav.market')
      : t('nav.portfolio')
  }, [stockSymbol, fullscreenInstrument, view, t])

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
    openOrders.goTo(1); closedOrders.goTo(1)
    transactionsList.goTo(1)
  }

  const cancelOrder = async (id: string) => {
    setNotice('')
    try {
      await api.post(`/api/order/${id}/cancel`)
    } catch (e: any) {
      setNotice(e.response ? tServer(e.response.data) : t('err.cancelFailed'))
    }
    loadBalance(); loadPortfolio(); openOrders.goTo(1); closedOrders.goTo(1); transactionsList.goTo(1)
  }

  const replaceOrder = async (id: string) => {
    setNotice('')
    setReplacing(prev => new Set(prev).add(id))
    try {
      await api.post(`/api/order/${id}/replace`)
      loadBalance(); loadPortfolio(); openOrders.goTo(1); closedOrders.goTo(1); transactionsList.goTo(1)
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

  // Opening the fullscreen panel always selects that instrument for the
  // trade ticket duplicated inside the panel, so it comes up already chosen.
  const openFullscreen = (i: Instrument) => {
    if (!i.isActive) return
    navigate('/stocks/' + i.symbol.toLowerCase())
    setSelected(i.id)
    setLimitPrice(prev => (prev === '' ? i.currentPrice.toFixed(2).replace('.', ',') : prev))
    loadHistory(i)
  }

  // The board sparkline only ever holds the last 24h (WINDOW_HOURS). The
  // fullscreen chart is meant to show the whole run, so it gets its own
  // fetch — the API clamps any range over 30 days, which in practice is
  // "everything" for an instrument that's only ever run in this sim. This
  // also covers a direct/cold navigation to /stocks/:symbol, where
  // openFullscreen was never called.
  useEffect(() => {
    if (!fullscreenInstrument) { setFullscreenHistory([]); return }
    setFullscreenHistory([])
    api.get<PricePoint[]>(`/api/instruments/${fullscreenInstrument.id}/history`, { params: { from: '2000-01-01T00:00:00Z' } })
      .then(r => setFullscreenHistory(r.data))
      .catch(console.error)
  }, [fullscreenInstrument?.id])

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

  const portfolioInstruments = portfolioBoard.items
  const favoriteInstruments = favoritesBoard.items
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
      <header className="rail">
        <div className="wrap rail-in">
          <span className="rail-brand">
            <Logomark size={26} />
            <span className="rail-brand-text">
              <span className="mark">Fin<em>Sim</em></span>
              <span className="mark-sub">{t('app.tagline')}</span>
            </span>
          </span>

          <nav className="top-nav" aria-label={t('nav.toggle')}>
            {balance?.isAdmin && (
              <button
                className="top-nav-item top-nav-wide"
                aria-pressed={view === 'admin'}
                onClick={() => navigate('/admin')}
              >
                <span className="top-nav-label">{t('admin.panelButton')}</span>
              </button>
            )}
            <button
              className="top-nav-item top-nav-wide"
              aria-pressed={view === 'portfolio'}
              onClick={() => navigate('/home')}
            >
              <span className="top-nav-label">{t('nav.portfolio')}</span>
            </button>
            <button
              className="top-nav-item top-nav-wide"
              aria-pressed={view === 'market'}
              onClick={() => navigate('/market')}
            >
              <span className="top-nav-label">{t('nav.market')}</span>
            </button>

            <span className="top-nav-item index-readout">
              <span className="top-nav-icon" aria-hidden="true">
                {online ? <span className="live-dot" /> : '●'}
              </span>
              <span className="top-nav-label">
                {t('app.market')} {indexValue ? fmt(indexValue) : '—'}
                <span className={dirOf(marketMove)}>
                  {' '}{marketMove >= 0 ? '▲' : '▼'} {(Math.abs(marketMove) * 100).toFixed(2)}%
                </span>
              </span>
            </span>

            <div className="top-nav-utility">
              {(view === 'portfolio' || view === 'market') && (
                <button
                  type="button"
                  className="top-nav-icon-btn"
                  aria-pressed={showFavorites}
                  aria-label={t('nav.favorites')}
                  onClick={() => setShowFavorites(v => !v)}
                >
                  <span aria-hidden="true">♥</span>
                </button>
              )}
              <button
                type="button"
                className="top-nav-icon-btn"
                onClick={toggleTheme}
                aria-label={theme === 'night' ? t('app.toDay') : t('app.toNight')}
              >
                <span aria-hidden="true">{theme === 'night' ? '☀' : '☾'}</span>
              </button>
              <button
                type="button"
                className="top-nav-icon-btn top-nav-lang"
                onClick={toggleLang}
                aria-label={t('nav.toggleLang')}
              >
                {lang === 'tr' ? 'EN' : 'TR'}
              </button>
            </div>

            <button className="top-nav-item top-nav-exit top-nav-wide" onClick={onLogout}>
              <span className="top-nav-icon" aria-hidden="true">⏻</span>
              <span className="top-nav-label">{t('app.logout')}</span>
            </button>
          </nav>

          {!online && (
            <span className="offline-pill">● {t('app.offline')}</span>
          )}
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
        {stockSymbol && !instrumentsLoaded ? null : view === 'admin' ? (
          balance === null ? (
            <div className="market-page">{t('fs.loading')}</div>
          ) : !balance.isAdmin ? (
            <div className="market-page">{t('admin.notAuthorized')}</div>
          ) : (
            <Admin onClose={() => navigate('/home')} />
          )
        ) : view === 'market' ? (
          <div className="market-page">
            <div className="section-head">
              <h2>{t('nav.market')}</h2>
              <span className="section-note">{t('board.otherNote', { n: board.items.length })}</span>
              <PageNav page={board.page} totalPages={board.totalPages} hasNext={board.hasNext} hasPrevious={board.hasPrevious} goTo={board.goTo} />
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
                      tick={ticks[i.symbol]}
                      pos={livePortfolio[i.symbol]}
                      onClick={() => openFullscreen(i)}
                      isFavorite={favorites.has(i.id)}
                      onToggleFavorite={() => toggleFavorite(i)}
                    />
                  ))}
                </div>
                <div className="board">
                  {marketRight.map(i => (
                    <InstrumentRow
                      key={i.id}
                      i={i}
                      tick={ticks[i.symbol]}
                      pos={livePortfolio[i.symbol]}
                      onClick={() => openFullscreen(i)}
                      isFavorite={favorites.has(i.id)}
                      onToggleFavorite={() => toggleFavorite(i)}
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
                <option value="symbol_asc">{t('sort.symbolAsc')}</option>
                <option value="symbol_desc">{t('sort.symbolDesc')}</option>
                <option value="price_desc">{t('sort.priceDesc')}</option>
                <option value="price_asc">{t('sort.priceAsc')}</option>
              </select>
            </div>

            <div className="section-head">
              <h2>{t('board.portfolioTitle')}</h2>
              <span className="section-note">{t('board.portfolioNote', { n: portfolioInstruments.length })}</span>
              <PageNav page={portfolioBoard.page} totalPages={portfolioBoard.totalPages} hasNext={portfolioBoard.hasNext} hasPrevious={portfolioBoard.hasPrevious} goTo={portfolioBoard.goTo} />
            </div>

            {portfolioInstruments.length === 0 ? (
              <div className="empty-state">{query ? t('search.noResults') : t('board.portfolioEmpty')}</div>
            ) : (
              <div className="board">
                {portfolioInstruments.map(i => (
                  <InstrumentRow
                    key={i.id}
                    i={i}
                    tick={ticks[i.symbol]}
                    pos={livePortfolio[i.symbol]}
                    onClick={() => openFullscreen(i)}
                    isFavorite={favorites.has(i.id)}
                    onToggleFavorite={() => toggleFavorite(i)}
                  />
                ))}
                {Array.from({ length: Math.max(0, PAGE_SIZE - portfolioInstruments.length) }).map((_, idx) => (
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
              <PageNav page={openOrders.page} totalPages={openOrders.totalPages} hasNext={openOrders.hasNext} hasPrevious={openOrders.hasPrevious} goTo={openOrders.goTo} />
            </div>

            <div className="panel">
              <div className="section-head">
                <h2>{t('tx.title')}</h2>
                <span className="section-note">{t('tx.note')}</span>
              </div>

              {transactionsList.items.length === 0 ? (
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
                      {transactionsList.items.map(tx => (
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
                      {Array.from({ length: Math.max(0, PAGE_SIZE - transactionsList.items.length) }).map((_, idx) => (
                        <tr key={`filler-${idx}`} className="filler-row" aria-hidden="true">
                          <td colSpan={6}>&nbsp;</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
              <PageNav page={transactionsList.page} totalPages={transactionsList.totalPages} hasNext={transactionsList.hasNext} hasPrevious={transactionsList.hasPrevious} goTo={transactionsList.goTo} />
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
              <PageNav page={closedOrders.page} totalPages={closedOrders.totalPages} hasNext={closedOrders.hasNext} hasPrevious={closedOrders.hasPrevious} goTo={closedOrders.goTo} />
            </div>
          </div>
        </div>
        </>
        )}
      </main>

      <FavoritesDrawer
        open={showFavorites}
        onClose={() => setShowFavorites(false)}
        items={favoriteInstruments}
        ticks={ticks}
        livePortfolio={livePortfolio}
        history={history}
        onOpenStock={openFullscreen}
        onToggleFavorite={toggleFavorite}
        page={favoritesBoard.page}
        totalPages={favoritesBoard.totalPages}
        hasNext={favoritesBoard.hasNext}
        hasPrevious={favoritesBoard.hasPrevious}
        goTo={favoritesBoard.goTo}
      />

      {(view === 'portfolio' || view === 'market') && (
      <div className="ticket">
        <div className="wrap ticket-in">
          {renderTicketFields('ticket')}
        </div>
      </div>
      )}

      {stockSymbol && !instrumentsLoaded && (
        <div className="market-page">{t('fs.loading')}</div>
      )}

      {stockSymbol && instrumentsLoaded && !fullscreenInstrument && (
        <div className="market-page">
          <p>{t('stock.unknownSymbol', { symbol: stockSymbol })}</p>
          <button className="ghost-btn" onClick={() => navigate('/market')}>{t('stock.backToMarket')}</button>
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
          onClose={() => navigate(bgPathRef.current)}
          renderTicketFields={renderTicketFields}
        />
      )}
    </div>
  )
}

function FavoritesDrawer({
  open, onClose, items, ticks, livePortfolio, history, onOpenStock, onToggleFavorite,
  page, totalPages, hasNext, hasPrevious, goTo,
}: {
  open: boolean
  onClose: () => void
  items: Instrument[]
  ticks: Record<string, Tick>
  livePortfolio: Record<string, PortfolioItem>
  history: Record<string, PricePoint[]>
  onOpenStock: (i: Instrument) => void
  onToggleFavorite: (i: Instrument) => void
  page: number
  totalPages: number
  hasNext: boolean
  hasPrevious: boolean
  goTo: (n: number) => void
}) {
  const { t } = useLang()
  return (
    <>
      <div className={`fav-veil${open ? ' open' : ''}`} onClick={onClose} aria-hidden="true" />
      <aside className={`fav-drawer${open ? ' open' : ''}`} aria-hidden={!open} aria-label={t('nav.favorites')}>
        <div className="fav-drawer-head">
          <span className="fav-drawer-icon" aria-hidden="true">♥</span>
          <div className="fav-drawer-title">
            <h2>{t('nav.favorites')}</h2>
            <span className="section-note">{t('board.favoritesNote', { n: items.length })}</span>
          </div>
          <button className="ghost-btn" onClick={onClose} aria-label={t('app.close')}>×</button>
        </div>

        <div className="fav-drawer-body">
          {items.length === 0 ? (
            <div className="fav-empty">
              <span className="fav-empty-icon" aria-hidden="true">♡</span>
              <p>{t('board.favoritesEmpty')}</p>
            </div>
          ) : (
            <div className="fav-grid">
              {items.map(i => {
                const pos = livePortfolio[i.symbol]
                const h = history[i.symbol] ?? []
                return (
                  <div key={i.id} className="fav-card" onClick={() => i.isActive && onOpenStock(i)} data-inactive={!i.isActive}>
                    <button
                      type="button"
                      className="fav-card-unfav"
                      aria-label={t('board.unfavorite')}
                      onClick={e => { e.stopPropagation(); onToggleFavorite(i) }}
                    >
                      ♥
                    </button>
                    <div className="fav-card-head">
                      <span className="fav-card-sym">{i.symbol}</span>
                      {i.type === 'Fund' && <span className="fund-badge">{t('board.fundBadge')}</span>}
                      {pos?.isShort && <span className="short-badge">{t('board.shortBadge')}</span>}
                    </div>
                    <span className="fav-card-name">{i.name}</span>

                    <div className="fav-card-spark">
                      <AreaSpark data={h} className="fav-spark" />
                    </div>

                    <div className="fav-card-foot">
                      <span className="fav-card-px" data-tick={ticks[i.symbol]} key={i.currentPrice}>
                        {fmt(i.currentPrice)}
                      </span>
                      {pos ? (
                        <span className={dirOf(pos.profitLoss)}>{signed(pos.profitLoss)}</span>
                      ) : !i.isActive ? (
                        <span className="empty">{t('board.closed')}</span>
                      ) : null}
                    </div>
                  </div>
                )
              })}
            </div>
          )}
        </div>

        {items.length > 0 && (
          <div className="fav-drawer-foot">
            <PageNav page={page} totalPages={totalPages} hasNext={hasNext} hasPrevious={hasPrevious} goTo={goTo} />
          </div>
        )}
      </aside>
    </>
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
  // Pad above and below the shown range so the line isn't flush against
  // the edges, and don't anchor the floor at zero — that squashes normal
  // price movement into a sliver when the price is far from zero.
  const pad = (dataMax - dataMin) * 0.15 || dataMax * 0.05 || 1
  const padBottom = (dataMax - dataMin) * 0.35 || dataMax * 0.1 || 1
  const min = Math.max(0, dataMin - padBottom)
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


const fmtAxisTime = (iso: string, lang: string) =>
  new Date(iso).toLocaleString(lang === 'tr' ? 'tr-TR' : 'en-US', {
    day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit',
  })

function ChartAxes({ min, max, times, lang }: { min: number; max: number; times: string[]; lang: string }) {
  const yTicks = 4
  const rows = Array.from({ length: yTicks + 1 }, (_, idx) => {
    const frac = idx / yTicks
    return { frac, price: max - frac * (max - min) }
  })
  const xCount = Math.min(times.length, 5)
  const xIdx = Array.from({ length: xCount }, (_, idx) =>
    Math.round((idx / (xCount - 1 || 1)) * (times.length - 1)))

  return (
    <>
      <div className="fs-chart-yaxis">
        {rows.map((r, idx) => (
          <span key={idx} className="fs-axis-label" style={{ top: `${r.frac * 100}%` }}>{fmt(r.price)}</span>
        ))}
      </div>
      <div className="fs-chart-xaxis">
        {xIdx.map((idx, pos) => (
          <span key={pos} className="fs-axis-label"
                style={{ left: `${(idx / (times.length - 1 || 1)) * 100}%` }}>
            {fmtAxisTime(times[idx], lang)}
          </span>
        ))}
      </div>
    </>
  )
}

type Candle = { t: string; open: number; high: number; low: number; close: number }

function buildCandles(data: PricePoint[], count: number): Candle[] {
  if (data.length === 0) return []
  const bucketSize = Math.max(1, Math.ceil(data.length / count))
  const candles: Candle[] = []
  let prevClose: number | null = null
  for (let idx = 0; idx < data.length; idx += bucketSize) {
    const slice = data.slice(idx, idx + bucketSize)
    const prices = slice.map(p => p.price)
    // Chain to the previous candle's close, not this bucket's first tick —
    // with short history buckets are often a single tick, which would make
    // open === close (and the candle flat/gray) on every candle regardless
    // of how the price is actually trending between them.
    const open = prevClose ?? slice[0].price
    candles.push({
      t: slice[0].timestamp,
      open,
      close: slice[slice.length - 1].price,
      high: Math.max(...prices, open),
      low: Math.min(...prices, open),
    })
    prevClose = slice[slice.length - 1].price
  }
  return candles
}

function CandleChart({ data, className }: { data: PricePoint[]; className: string }) {
  const { lang } = useLang()
  const [hover, setHover] = useState<number | null>(null)
  const candles = useMemo(() => buildCandles(data, 40), [data])
  if (candles.length < 2) return null

  const highs = candles.map(c => c.high)
  const lows = candles.map(c => c.low)
  const dataMax = Math.max(...highs)
  const dataMin = Math.min(...lows)
  const pad = (dataMax - dataMin) * 0.1 || dataMax * 0.05 || 1
  const padBottom = (dataMax - dataMin) * 0.3 || dataMax * 0.1 || 1
  const min = Math.max(0, dataMin - padBottom)
  const top = dataMax + pad
  const range = top - min || 1
  const last = candles.length - 1

  const px = (idx: number) => ((idx + 0.5) / candles.length) * 100
  const py = (price: number) => 30 - ((price - min) / range) * 26
  const barW = (100 / candles.length) * 0.6

  const onMove = (e: React.MouseEvent<HTMLDivElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    const frac = (e.clientX - rect.left) / rect.width
    setHover(Math.max(0, Math.min(last, Math.floor(frac * candles.length))))
  }

  return (
    <div className={`spark-wrap ${className}`} onMouseMove={onMove} onMouseLeave={() => setHover(null)}>
      <svg className="spark-svg" viewBox="0 0 100 30" preserveAspectRatio="none" aria-hidden="true">
        {candles.map((c, idx) => {
          const color = c.close > c.open ? 'var(--rise)' : c.close < c.open ? 'var(--fall)' : 'var(--flat)'
          const x = px(idx)
          const bodyTop = py(Math.max(c.open, c.close))
          const bodyBottom = py(Math.min(c.open, c.close))
          return (
            <g key={idx}>
              <line x1={x} x2={x} y1={py(c.high)} y2={py(c.low)}
                    stroke={color} strokeWidth="0.6" vectorEffect="non-scaling-stroke" />
              <rect x={x - barW / 2} width={barW}
                    y={bodyTop} height={Math.max(bodyBottom - bodyTop, 0.4)}
                    fill={color} />
            </g>
          )
        })}
        {hover !== null && (
          <line x1={px(hover)} y1="0" x2={px(hover)} y2="30"
                stroke="var(--edge)" strokeWidth="1" vectorEffect="non-scaling-stroke" />
        )}
      </svg>
      <ChartAxes min={min} max={top} times={candles.map(c => c.t)} lang={lang} />
      {hover !== null && (
        <div className="spark-tip">
          <strong>{fmt(candles[hover].close)}</strong>
          <span>{fmtAxisTime(candles[hover].t, lang)}</span>
          <span>O {fmt(candles[hover].open)} H {fmt(candles[hover].high)} L {fmt(candles[hover].low)}</span>
        </div>
      )}
    </div>
  )
}

function fmtCompactTRY(n: number): string {
  const abs = Math.abs(n)
  const units: [number, string][] = [
    [1_000_000_000_000, 'Tr'],
    [1_000_000_000, 'Mr'],
    [1_000_000, 'Mn'],
    [1_000, 'B'],
  ]
  for (const [threshold, suffix] of units) {
    if (abs >= threshold) {
      const value = (n / threshold).toLocaleString('tr-TR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })
      return `${value} ${suffix} ₺`
    }
  }
  return `${n.toLocaleString('tr-TR', { maximumFractionDigits: 0 })} ₺`
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
  const { t, lang } = useLang()
  const [chartMode, setChartMode] = useState<'area' | 'candle'>('area')

  // The panel is its own scroll surface — the board underneath must not
  // move while it's open, even if a wheel scroll spills past the panel edge.
  useEffect(() => {
    const prev = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => { document.body.style.overflow = prev }
  }, [])

  const open = history[0]?.price ?? i.currentPrice
  const prices = history.length ? history.map(p => p.price) : [i.currentPrice]
  const high = Math.max(...prices, i.currentPrice)
  const low = Math.min(...prices, i.currentPrice)
  const areaPad = (high - low) * 0.15 || high * 0.05 || 1
  const areaPadBottom = (high - low) * 0.35 || high * 0.1 || 1
  const areaTop = high + areaPad
  const areaMin = Math.max(0, low - areaPadBottom)
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
          {i.sector != null && (
            <div className="fs-stat">
              <span className="fs-stat-label">{t('fs.sector')}</span>
              <span className="fs-stat-value">{sectorLabel(i.sector)}</span>
            </div>
          )}
          {i.industry != null && (
            <div className="fs-stat">
              <span className="fs-stat-label">{t('fs.industry')}</span>
              <span className="fs-stat-value">{i.industry}</span>
            </div>
          )}
          {i.employees != null && (
            <div className="fs-stat">
              <span className="fs-stat-label">{t('fs.employees')}</span>
              <span className="fs-stat-value">{i.employees.toLocaleString('tr-TR')}</span>
            </div>
          )}
          {i.sharesOutstanding != null && (
            <div className="fs-stat">
              <span className="fs-stat-label">{t('fs.marketCap')}</span>
              <span className="fs-stat-value">{fmtCompactTRY(i.sharesOutstanding * i.currentPrice)}</span>
            </div>
          )}
          {i.city != null && (
            <div className="fs-stat">
              <span className="fs-stat-label">{t('fs.city')}</span>
              <span className="fs-stat-value">{i.city}</span>
            </div>
          )}
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
          {i.description != null && (
            <div className="fs-description">
              <p>{i.description}</p>
              {i.website != null && (
                <a href={i.website} target="_blank" rel="noopener noreferrer">{i.website}</a>
              )}
            </div>
          )}

          <div className="instrument-fullscreen-chart">
            <div className="fs-chart-toolbar">
              <button type="button" className="ghost-btn" aria-pressed={chartMode === 'area'}
                      onClick={() => setChartMode('area')}>
                {t('fs.areaView')}
              </button>
              <button type="button" className="ghost-btn" aria-pressed={chartMode === 'candle'}
                      onClick={() => setChartMode('candle')}>
                {t('fs.candleView')}
              </button>
            </div>
            {chartMode === 'area' ? (
              <>
                <AreaSpark data={history} className="fullscreen-spark" />
                {history.length >= 2 && (
                  <ChartAxes min={areaMin} max={areaTop} times={history.map(p => p.timestamp)} lang={lang} />
                )}
              </>
            ) : (
              <CandleChart data={history} className="fullscreen-spark" />
            )}
            {history.length < 2 && <div className="fs-chart-empty">{t('fs.loading')}</div>}
          </div>
        </div>

        <div className="ticket-in fullscreen-ticket">
          {renderTicketFields('fullscreen-ticket')}
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