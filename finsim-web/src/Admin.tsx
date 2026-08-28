import { useEffect, useState } from 'react'
import api from './api'
import { useLang, type LangKey } from './lang'
import { fmt } from './format'
import { paginate, Pager, signed, dirOf, useCursorPage, PAGE_SIZE } from './App'

type Instrument = {
  id: string
  symbol: string
  name: string
  basePrice: number
  currentPrice: number
  isActive: boolean
}

type LiquidationPreview = {
  affectedUsers: number
  totalShares: number
  price: number
}

type Holding = {
  symbol: string
  name: string
  totalQuantity: number
  lockedQuantity: number
  averageCost: number
  currentPrice: number
  marketValue: number
  profitLoss: number
}

type AdminUser = {
  id: string
  username: string
  email: string
  freeCashBalance: number
  lockedCashBalance: number
  realizedProfitLoss: number
  netDeposits: number
  holdings: Holding[]
}

type BookLevel = { price: number; quantity: number; orderCount: number }
type OrderBook = {
  instrumentId: string; symbol: string; currentPrice: number
  bids: BookLevel[]; asks: BookLevel[]
}

// Prev/next controls for a useCursorPage board — mirrors the market/portfolio
// board pager in App.tsx (a running "page number" doesn't exist with cursors).
function CursorPager({ board }: { board: { stack: unknown[]; nextCursor: string | null; prev: () => void; next: () => void } }) {
  const { t } = useLang()
  return (
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
  )
}

export default function Admin({ onClose }: { onClose: () => void }) {
  const { t, tServer } = useLang()

  const [instruments, setInstruments] = useState<Instrument[]>([])
  const [users, setUsers] = useState<AdminUser[]>([])
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)

  const [bookSymbol, setBookSymbol] = useState<string>('')
  const [book, setBook] = useState<OrderBook | null>(null)

  const [pendingDeactivate, setPendingDeactivate] =
    useState<{ instrument: Instrument; preview: LiquidationPreview } | null>(null)

  const [instrumentQuery, setInstrumentQuery] = useState('')
  const [instrumentSort, setInstrumentSort] = useState('symbol_asc')
  const [debouncedInstrumentQuery, setDebouncedInstrumentQuery] = useState('')
  const [userQuery, setUserQuery] = useState('')
  const [userSort, setUserSort] = useState('name_asc')
  const [debouncedUserQuery, setDebouncedUserQuery] = useState('')
  const [botView, setBotView] = useState(false)
  const [exposurePage, setExposurePage] = useState(1)
  const [netWorthPage, setNetWorthPage] = useState(1)
  const [askPage, setAskPage] = useState(1)
  const [bidPage, setBidPage] = useState(1)

  const [cashDelta, setCashDelta] = useState<Record<string, string>>({})
  const [cashReason, setCashReason] = useState<Record<string, string>>({})
  const [shareInstrument, setShareInstrument] = useState<Record<string, string>>({})
  const [shareQty, setShareQty] = useState<Record<string, string>>({})

  // Full lists — still needed for the order-book/share-grant dropdowns (active
  // instruments only) and the bot-view aggregates (net worth, exposure, cash
  // utilization, leaderboards), which need every bot at once and can't be
  // computed from a single cursor page.
  const loadInstruments = () =>
    api.get<Instrument[]>('/api/instruments').then(r => setInstruments(r.data)).catch(console.error)

  const loadUsers = () =>
    api.get<AdminUser[]>('/api/admin/users').then(r => setUsers(r.data)).catch(console.error)

  // Cursor-paged instrument/user tables — mirrors the market/portfolio boards
  // in App.tsx. Unlike loadInstruments above, this includes inactive instruments.
  const instrumentBoard = useCursorPage<Instrument>('/api/instruments/admin-board', {
    limit: PAGE_SIZE, sort: instrumentSort, q: debouncedInstrumentQuery,
  })
  const humanBoard = useCursorPage<AdminUser>('/api/admin/users/board', {
    bots: false, limit: PAGE_SIZE, sort: userSort, q: debouncedUserQuery,
  })
  const botBoard = useCursorPage<AdminUser>('/api/admin/users/board', {
    bots: true, limit: PAGE_SIZE, sort: userSort, q: debouncedUserQuery,
  })

  const reloadPrice = async (i: Instrument) => {
    setNotice(''); setBusy(true)
    try {
      const r = await api.post(`/api/admin/instruments/${i.id}/reload-price`)
      setNotice(`${i.symbol}: ${r.data.outcome} ${fmt(r.data.oldPrice)} → ${fmt(r.data.newPrice)}`)
      loadInstruments()
      instrumentBoard.reload()
    } catch (e: any) { fail(e, 'err.orderFailed') } finally { setBusy(false) }
  }


  useEffect(() => { loadInstruments(); loadUsers() }, [])

  useEffect(() => {
    if (!bookSymbol) return
    const load = () => api.get<OrderBook>(`/api/admin/book/${bookSymbol}`)
      .then(r => setBook(r.data)).catch(console.error)
    load()
    const h = setInterval(load, 10000)
    return () => clearInterval(h)
  }, [bookSymbol])

  useEffect(() => { setAskPage(1); setBidPage(1) }, [bookSymbol])

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedInstrumentQuery(instrumentQuery), 300)
    return () => window.clearTimeout(timer)
  }, [instrumentQuery])

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedUserQuery(userQuery), 300)
    return () => window.clearTimeout(timer)
  }, [userQuery])

  // Sort/query changes reset paging to page 1 — a cursor minted under the old
  // sort or query wouldn't decode (or would decode against the wrong rows)
  // under the new one anyway.
  useEffect(() => {
    instrumentBoard.load()
  }, [instrumentSort, debouncedInstrumentQuery])

  useEffect(() => {
    humanBoard.load()
    botBoard.load()
  }, [userSort, debouncedUserQuery])

  const fail = (e: any, fallback: LangKey) =>
    setNotice(e.response ? tServer(e.response.data) : t(fallback))

  const reactivate = async (i: Instrument) => {
    setNotice('')
    try {
      await api.put(`/api/instruments/${i.id}/active`, { isActive: true })
      setNotice(t('admin.reactivated'))
      loadInstruments()
      instrumentBoard.reload()
    } catch (e: any) {
      fail(e, 'err.orderFailed')
    }
  }

  const requestDeactivate = async (i: Instrument) => {
    setNotice('')
    try {
      const r = await api.get<LiquidationPreview>(`/api/instruments/${i.id}/liquidation-preview`)
      setPendingDeactivate({ instrument: i, preview: r.data })
    } catch (e: any) {
      fail(e, 'err.orderFailed')
    }
  }

  const confirmDeactivate = async () => {
    if (!pendingDeactivate) return
    const { instrument } = pendingDeactivate
    setPendingDeactivate(null)
    setNotice('')
    try {
      await api.put(`/api/instruments/${instrument.id}/active`, { isActive: false })
      setNotice(t('admin.deactivated'))
      loadInstruments()
      loadUsers()
      instrumentBoard.reload()
    } catch (e: any) {
      fail(e, 'err.orderFailed')
    }
  }

  const applyCash = async (userId: string) => {
    setNotice('')
    const delta = parseFloat((cashDelta[userId] ?? '').replace(',', '.'))
    if (!Number.isFinite(delta) || delta === 0) {
      setNotice(t('err.minPrice'))
      return
    }
    try {
      await api.post(`/api/admin/users/${userId}/cash`, { delta, reason: cashReason[userId] ?? '' })
      setNotice(t('admin.cashApplied'))
      setCashDelta(prev => ({ ...prev, [userId]: '' }))
      setCashReason(prev => ({ ...prev, [userId]: '' }))
      loadUsers()
      humanBoard.reload()
      botBoard.reload()
    } catch (e: any) {
      fail(e, 'err.orderFailed')
    }
  }

  const applyShares = async (userId: string) => {
    setNotice('')
    const instrumentId = shareInstrument[userId]
    const qty = parseInt(shareQty[userId] ?? '', 10)
    if (!instrumentId || !Number.isFinite(qty) || qty === 0) {
      setNotice(t('err.minQty'))
      return
    }
    try {
      await api.post(`/api/admin/users/${userId}/shares`, { instrumentId, quantityDelta: qty })
      setNotice(t('admin.sharesApplied'))
      setShareQty(prev => ({ ...prev, [userId]: '' }))
      loadUsers()
      humanBoard.reload()
      botBoard.reload()
    } catch (e: any) {
      fail(e, 'err.orderFailed')
    }
  }

  const isBotUser = (u: AdminUser) =>
    u.email.toLocaleLowerCase('tr').endsWith('@bots.finsim.local')

  const botsAll = users.filter(isBotUser)

  const holdingsValue = (u: AdminUser) => u.holdings.reduce((s, h) => s + h.marketValue, 0)
  const accountValue = (u: AdminUser) => u.freeCashBalance + u.lockedCashBalance + holdingsValue(u)
  const netPnl = (u: AdminUser) => accountValue(u) - u.netDeposits

  const botStats = (() => {
    const n = botsAll.length
    const totalFree = botsAll.reduce((s, u) => s + u.freeCashBalance, 0)
    const totalLocked = botsAll.reduce((s, u) => s + u.lockedCashBalance, 0)
    const totalRealized = botsAll.reduce((s, u) => s + u.realizedProfitLoss, 0)
    const totalDeposits = botsAll.reduce((s, u) => s + u.netDeposits, 0)
    const totalAccountValue = botsAll.reduce((s, u) => s + accountValue(u), 0)
    const totalNetPnl = totalAccountValue - totalDeposits
    const winners = botsAll.filter(u => netPnl(u) > 0).length
    const losers = botsAll.filter(u => netPnl(u) < 0).length
    return {
      n, totalFree, totalLocked, totalRealized,
      totalDeposits, totalAccountValue, totalNetPnl,
      avgNetPnl: n ? totalNetPnl / n : 0,
      winners, losers,
    }
  })()

  const exposure = (() => {
    const map = new Map<string, {
      symbol: string; name: string; netQty: number; lockedQty: number
      marketValue: number; botCount: number
    }>()
    for (const u of botsAll) {
      for (const h of u.holdings) {
        const cur = map.get(h.symbol) ?? {
          symbol: h.symbol, name: h.name, netQty: 0, lockedQty: 0, marketValue: 0, botCount: 0,
        }
        cur.netQty += h.totalQuantity
        cur.lockedQty += h.lockedQuantity
        cur.marketValue += h.marketValue
        cur.botCount += 1
        map.set(h.symbol, cur)
      }
    }
    return [...map.values()].sort((a, b) => Math.abs(b.marketValue) - Math.abs(a.marketValue))
  })()

  const exposurePaged = paginate(exposure, exposurePage)

  const leaderboard = [...botsAll].sort((a, b) => netPnl(b) - netPnl(a))
  const topGainers = leaderboard.slice(0, 5).filter(u => netPnl(u) > 0)
  const topLosers = leaderboard.slice(-5).reverse().filter(u => netPnl(u) < 0)
  const netWorthPaged = paginate(leaderboard, netWorthPage)

  const cashUtil = [...botsAll]
    .map(u => ({
      ...u,
      utilPct: u.freeCashBalance + u.lockedCashBalance > 0
        ? (u.lockedCashBalance / (u.freeCashBalance + u.lockedCashBalance)) * 100
        : 0,
    }))
    .sort((a, b) => b.utilPct - a.utilPct)
    .slice(0, 8)
  const askPaged = paginate(book ? [...book.asks].reverse() : [], askPage)
  const bidPaged = paginate(book ? book.bids : [], bidPage)

  return (
    <div className="admin">
      <div className="section-head">
        <h2>{t('admin.title')}</h2>
        <span className="rail-spacer" />
        <button className="ghost-btn" onClick={onClose}>{t('app.close')}</button>
      </div>

      {notice && (
        <div className="notice">
          <span style={{ flex: 1 }}>{notice}</span>
          <button onClick={() => setNotice('')} aria-label={t('app.close')}>×</button>
        </div>
      )}

      {pendingDeactivate && (
        <div className="modal-backdrop" onClick={() => setPendingDeactivate(null)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <h3>{t('admin.confirmDeactivateTitle')}</h3>
            <p>
              {pendingDeactivate.preview.affectedUsers > 0
                ? t('admin.confirmDeactivateBody', {
                    symbol: pendingDeactivate.instrument.symbol,
                    users: pendingDeactivate.preview.affectedUsers,
                    shares: pendingDeactivate.preview.totalShares,
                    price: fmt(pendingDeactivate.preview.price)
                  })
                : t('admin.confirmDeactivateNoHoldings', { symbol: pendingDeactivate.instrument.symbol })}
            </p>
            <div className="modal-actions">
              <button className="ghost-btn" onClick={() => setPendingDeactivate(null)}>
                {t('admin.cancel')}
              </button>
              <button className="trade sell" onClick={confirmDeactivate}>{t('admin.confirm')}</button>
            </div>
          </div>
        </div>
      )}

      <div className="panel">
    <div className="section-head">
      <h3>{t('admin.instrumentsTitle')}</h3>
    </div>

    <div className="board-controls" style={{ marginBottom: 16 }}>
      <div className="search-input">
        <input
          className="field-input"
          type="text"
          value={instrumentQuery}
          onChange={e => setInstrumentQuery(e.target.value)}
          placeholder={t('search.placeholder')}
        />
        {instrumentQuery && (
          <button
            className="ghost-btn"
            onClick={() => setInstrumentQuery('')}
            aria-label={t('app.close')}
          >
            ×
          </button>
        )}
      </div>
      <select className="field-input" value={instrumentSort} onChange={e => setInstrumentSort(e.target.value)}>
        <option value="symbol_asc">{t('sort.symbolAsc')}</option>
        <option value="symbol_desc">{t('sort.symbolDesc')}</option>
        <option value="price_desc">{t('sort.priceDesc')}</option>
        <option value="price_asc">{t('sort.priceAsc')}</option>
      </select>
    </div>

    <table className="ledger">
      <thead>
        <tr>
          <th>{t('admin.symbol')}</th>
          <th>{t('admin.name')}</th>
          <th className="num">{t('ledger.price')}</th>
          <th>{t('admin.active')}</th>
          <th />
          <th />
        </tr>
      </thead>
      <tbody>
        {instrumentBoard.items.map(i => (
          <tr key={i.id}>
            <td className="sym">{i.symbol}</td>
            <td>{i.name}</td>
            <td className="num">{fmt(i.currentPrice)}</td>
            <td>{i.isActive ? t('admin.active') : t('admin.inactive')}</td>
            <td className="num">
              {i.isActive && (
                <button className="link-btn" disabled={busy} onClick={() => reloadPrice(i)}>
                  {t('admin.reloadPrice')}
                </button>
              )}
            </td>
            <td className="num">
              {i.isActive ? (
                <button className="link-btn" onClick={() => requestDeactivate(i)}>
                  {t('admin.deactivate')}
                </button>
              ) : (
                <button className="link-btn" onClick={() => reactivate(i)}>
                  {t('admin.reactivate')}
                </button>
              )}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
    <CursorPager board={instrumentBoard} />
  </div>

      <div className="panel">
        <div className="section-head">
          <h3>Order Book</h3>
          <select className="field-input" value={bookSymbol} onChange={e => setBookSymbol(e.target.value)}>
            <option value="">—</option>
            {instruments.map(i => (
              <option key={i.id} value={i.id}>{i.symbol}</option>
            ))}
          </select>
        </div>
        {book && (
          <>
            <div className="section-head">
              <span className="section-note">Sell</span>
            </div>
            <table className="ledger">
              <thead>
                <tr>
                  <th>Side</th>
                  <th className="num">Price</th>
                  <th className="num">Qty</th>
                  <th className="num">Orders</th>
                </tr>
              </thead>
              <tbody>
                {askPaged.items.map(l => (
                  <tr key={`a${l.price}`}>
                    <td className="down">Sell</td>
                    <td className="num down">{fmt(l.price)}</td>
                    <td className="num">{l.quantity}</td>
                    <td className="num">{l.orderCount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <Pager page={askPaged.page} totalPages={askPaged.totalPages} onChange={setAskPage} />

            <table className="ledger">
              <tbody>
                <tr>
                  <td colSpan={2}>Last</td>
                  <td className="num" colSpan={2}>{fmt(book.currentPrice)}</td>
                </tr>
              </tbody>
            </table>

            <div className="section-head">
              <span className="section-note">Buy</span>
            </div>
            <table className="ledger">
              <thead>
                <tr>
                  <th>Side</th>
                  <th className="num">Price</th>
                  <th className="num">Qty</th>
                  <th className="num">Orders</th>
                </tr>
              </thead>
              <tbody>
                {bidPaged.items.map(l => (
                  <tr key={`b${l.price}`}>
                    <td className="up">Buy</td>
                    <td className="num up">{fmt(l.price)}</td>
                    <td className="num">{l.quantity}</td>
                    <td className="num">{l.orderCount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <Pager page={bidPaged.page} totalPages={bidPaged.totalPages} onChange={setBidPage} />
          </>
        )}
      </div>

      <div className="panel">
        <div className="section-head">
          <h3>{t('admin.usersTitle')}</h3>
        </div>

        <div className="board-controls">
          <div className="search-input">
            <input
              className="field-input"
              type="text"
              value={userQuery}
              onChange={e => setUserQuery(e.target.value)}
              placeholder={t('admin.userSearchPlaceholder')}
            />
            {userQuery && (
              <button
                className="ghost-btn"
                onClick={() => setUserQuery('')}
                aria-label={t('app.close')}
              >
                ×
              </button>
            )}
          </div>
          <select className="field-input" value={userSort} onChange={e => setUserSort(e.target.value)}>
            <option value="name_asc">{t('sort.nameAsc')}</option>
            <option value="name_desc">{t('sort.nameDesc')}</option>
          </select>
        </div>

        {humanBoard.items.map(u => renderUserCard(u))}
        <CursorPager board={humanBoard} />
      </div>

      <div className="panel">
        <div className="section-head">
          <h3>{t('admin.botUsersTitle')}</h3>
          <span className="rail-spacer" />
          <button className="ghost-btn" onClick={() => setBotView(v => !v)}>
            {botView ? t('admin.closeBotView') : t('admin.botView')}
          </button>
        </div>

        {botView ? (
          <div className="bot-view">
            <div className="stat-grid">
              <div className="stat-tile">
                <span className="stat-label">{t('admin.botCount')}</span>
                <span className="stat-value">{botStats.n}</span>
              </div>
              <div className="stat-tile">
                <span className="stat-label">{t('admin.totalFreeCash')}</span>
                <span className="stat-value">{fmt(botStats.totalFree)}</span>
              </div>
              <div className="stat-tile">
                <span className="stat-label">{t('admin.totalLockedCash')}</span>
                <span className="stat-value">{fmt(botStats.totalLocked)}</span>
              </div>
              <div className="stat-tile">
                <span className="stat-label">{t('admin.totalDeposits')}</span>
                <span className="stat-value">{fmt(botStats.totalDeposits)}</span>
              </div>
              <div className="stat-tile">
                <span className="stat-label">{t('admin.totalAccountValue')}</span>
                <span className="stat-value">{fmt(botStats.totalAccountValue)}</span>
              </div>
              <div className="stat-tile">
                <span className="stat-label">{t('admin.totalNetPnl')}</span>
                <span className={`stat-value ${dirOf(botStats.totalNetPnl)}`}>{signed(botStats.totalNetPnl)}</span>
              </div>
              <div className="stat-tile">
                <span className="stat-label">{t('admin.avgNetPnl')}</span>
                <span className={`stat-value ${dirOf(botStats.avgNetPnl)}`}>{signed(botStats.avgNetPnl)}</span>
              </div>
              <div className="stat-tile">
                <span className="stat-label">{t('admin.totalRealized')}</span>
                <span className={`stat-value ${dirOf(botStats.totalRealized)}`}>{signed(botStats.totalRealized)}</span>
              </div>
              <div className="stat-tile">
                <span className="stat-label">{t('admin.winLoss')}</span>
                <span className="stat-value">
                  <span className="up">{botStats.winners}</span> / <span className="down">{botStats.losers}</span>
                </span>
              </div>
            </div>

            <div className="section-head">
              <h4>{t('admin.netWorthTitle')}</h4>
            </div>
            <table className="ledger">
              <thead>
                <tr>
                  <th>{t('admin.name')}</th>
                  <th className="num">{t('admin.initialBudget')}</th>
                  <th className="num">{t('admin.holdingsValue')}</th>
                  <th className="num">{t('admin.totalAccountValue')}</th>
                  <th className="num">{t('admin.netPnl')}</th>
                </tr>
              </thead>
              <tbody>
                {netWorthPaged.items.map(u => (
                  <tr key={u.id}>
                    <td>{u.username}</td>
                    <td className="num">{fmt(u.netDeposits)}</td>
                    <td className="num">{fmt(holdingsValue(u))}</td>
                    <td className="num">{fmt(accountValue(u))}</td>
                    <td className={`num ${dirOf(netPnl(u))}`}>{signed(netPnl(u))}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <Pager page={netWorthPaged.page} totalPages={netWorthPaged.totalPages} onChange={setNetWorthPage} />

            <div className="section-head">
              <h4>{t('admin.exposureTitle')}</h4>
            </div>
            {exposure.length === 0 ? (
              <span className="empty">{t('admin.noExposure')}</span>
            ) : (
              <>
                <table className="ledger">
                  <thead>
                    <tr>
                      <th>{t('admin.symbol')}</th>
                      <th className="num">{t('admin.netQty')}</th>
                      <th className="num">{t('admin.lockedQty')}</th>
                      <th className="num">{t('admin.marketValue')}</th>
                      <th className="num">{t('admin.botsHolding')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {exposurePaged.items.map(e => (
                      <tr key={e.symbol}>
                        <td className="sym">{e.symbol}</td>
                        <td className="num">{e.netQty}</td>
                        <td className="num">{e.lockedQty}</td>
                        <td className="num">{fmt(e.marketValue)}</td>
                        <td className="num">{e.botCount}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                <Pager page={exposurePaged.page} totalPages={exposurePaged.totalPages} onChange={setExposurePage} />
              </>
            )}

            <div className="bot-view-cols">
              <div>
                <div className="section-head"><h4>{t('admin.topGainers')}</h4></div>
                <table className="ledger">
                  <tbody>
                    {topGainers.length === 0 ? (
                      <tr><td className="empty">—</td></tr>
                    ) : topGainers.map(u => (
                      <tr key={u.id}>
                        <td>{u.username}</td>
                        <td className="num up">{signed(netPnl(u))}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div>
                <div className="section-head"><h4>{t('admin.topLosers')}</h4></div>
                <table className="ledger">
                  <tbody>
                    {topLosers.length === 0 ? (
                      <tr><td className="empty">—</td></tr>
                    ) : topLosers.map(u => (
                      <tr key={u.id}>
                        <td>{u.username}</td>
                        <td className="num down">{signed(netPnl(u))}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>

            <div className="section-head">
              <h4>{t('admin.cashUtilTitle')}</h4>
            </div>
            <table className="ledger">
              <thead>
                <tr>
                  <th>{t('admin.name')}</th>
                  <th className="num">{t('admin.free')}</th>
                  <th className="num">{t('admin.locked')}</th>
                  <th className="num">{t('admin.utilPct')}</th>
                </tr>
              </thead>
              <tbody>
                {cashUtil.map(u => (
                  <tr key={u.id}>
                    <td>{u.username}</td>
                    <td className="num">{fmt(u.freeCashBalance)}</td>
                    <td className="num">{fmt(u.lockedCashBalance)}</td>
                    <td className="num">{u.utilPct.toFixed(0)}%</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <>
            {botBoard.items.map(u => renderUserCard(u))}
            <CursorPager board={botBoard} />
          </>
        )}
      </div>
    </div>
  )

  function renderUserCard(u: AdminUser) {
    return (
      <div key={u.id} className="admin-user">
        <div className="admin-user-head">
          <strong>{u.username}</strong>
          <span className="section-note">{u.email}</span>
          <span>{t('admin.free')}: {fmt(u.freeCashBalance)}</span>
          <span>{t('admin.locked')}: {fmt(u.lockedCashBalance)}</span>
          <span>{t('admin.realized')}: {fmt(u.realizedProfitLoss)}</span>
        </div>

        <div className="admin-user-holdings">
          {u.holdings.length === 0 ? (
            <span className="empty">{t('admin.noHoldings')}</span>
          ) : (
            u.holdings.map(h => (
              <span key={h.symbol}>
                {h.symbol}: {h.totalQuantity}
                {h.lockedQuantity > 0 ? ` (${t('board.locked', { n: h.lockedQuantity })})` : ''}
              </span>
            ))
          )}
        </div>

        <div className="admin-form">
          <input className="field-input" placeholder={t('admin.cashDelta')}
            value={cashDelta[u.id] ?? ''}
            onChange={e => setCashDelta(prev => ({ ...prev, [u.id]: e.target.value }))} />
          <input className="field-input" placeholder={t('admin.reason')}
            value={cashReason[u.id] ?? ''}
            onChange={e => setCashReason(prev => ({ ...prev, [u.id]: e.target.value }))} />
          <button className="ghost-btn" onClick={() => applyCash(u.id)}>
            {t('admin.applyCash')}
          </button>
        </div>

        <div className="admin-form">
          <select className="field-input" value={shareInstrument[u.id] ?? ''}
            onChange={e => setShareInstrument(prev => ({ ...prev, [u.id]: e.target.value }))}>
            <option value="">{t('admin.shareInstrument')}</option>
            {instruments.filter(i => i.isActive).map(i => (
              <option key={i.id} value={i.id}>{i.symbol}</option>
            ))}
          </select>
          <input className="field-input" placeholder={t('admin.shareQty')}
            value={shareQty[u.id] ?? ''}
            onChange={e => setShareQty(prev => ({ ...prev, [u.id]: e.target.value }))} />
          <button className="ghost-btn" onClick={() => applyShares(u.id)}>
            {t('admin.applyShares')}
          </button>
        </div>
      </div>
    )
  }
}
