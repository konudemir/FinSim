import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { useLang } from '../lang'
import { sectorLabel } from '../lang'
import { fmt } from '../format'
import {
  AdminBookSide, AreaSpark, CandleChart, ChartAxes, dirOf, fmtCompactTRY, signed,
  useAdminBookPage,
} from '../App'
import type { Instrument, PortfolioItem, PricePoint, Tick } from '../App'

export default function StockPage({
  i, pos, tick, history, isFavorite, onToggleFavorite, renderTicketFields, isAdmin,
}: {
  i: Instrument
  pos: PortfolioItem | undefined
  tick: Tick | undefined
  history: PricePoint[]
  isFavorite: boolean
  onToggleFavorite: () => void
  renderTicketFields: (idPrefix: string) => ReactNode
  isAdmin: boolean
}) {
  const { t, lang } = useLang()
  const [chartMode, setChartMode] = useState<'area' | 'candle'>('area')
  const buyBook = useAdminBookPage(i.id, 'Buy')
  const sellBook = useAdminBookPage(i.id, 'Sell')

  useEffect(() => {
    if (isAdmin) { buyBook.goTo(1); sellBook.goTo(1) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAdmin, i.id])

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
    <div className="stock-page" data-trend={rising ? 'up' : 'down'}>
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

      <div className="stock-body">
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

        {isAdmin && (
          <div className="admin-book-panels">
            <AdminBookSide title={t('admin.book.bids')} side="up" book={buyBook} />
            <AdminBookSide title={t('admin.book.asks')} side="down" book={sellBook} />
          </div>
        )}
      </div>

      <div className="ticket-in fullscreen-ticket">
        {renderTicketFields('stock-ticket')}
      </div>
    </div>
  )
}
