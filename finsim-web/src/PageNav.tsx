import { useLang } from './lang'

type PageNavProps = {
  page: number
  totalPages: number
  hasNext: boolean
  hasPrevious: boolean
  goTo: (page: number) => void
}

// Always the first and last page, plus one page either side of the current
// one, with an ellipsis marking any break in that sequence.
function pageNumbers(page: number, totalPages: number): (number | 'ellipsis')[] {
  const keep = new Set<number>([1, totalPages, page - 1, page, page + 1])
  const out: (number | 'ellipsis')[] = []
  let prev = 0
  for (let p = 1; p <= totalPages; p++) {
    if (!keep.has(p)) continue
    if (prev && p - prev > 1) out.push('ellipsis')
    out.push(p)
    prev = p
  }
  return out
}

/** Shared page-number pager: `<PageNav {...usePagedList(...)} />`. */
export default function PageNav({ page, totalPages, hasNext, hasPrevious, goTo }: PageNavProps) {
  const { t } = useLang()
  if (totalPages <= 1) return null
  return (
    <div className="pager">
      <button
        type="button"
        className="ghost-btn"
        disabled={!hasPrevious}
        onClick={() => goTo(page - 1)}
        aria-label={t('pager.prev')}
      >
        ‹
      </button>
      {pageNumbers(page, totalPages).map((p, idx) =>
        p === 'ellipsis' ? (
          <span key={`e${idx}`} className="pager-ellipsis" aria-hidden="true">…</span>
        ) : (
          <button
            key={p}
            type="button"
            className="ghost-btn pager-num"
            aria-current={p === page ? 'page' : undefined}
            disabled={p === page}
            onClick={() => goTo(p)}
          >
            {p}
          </button>
        )
      )}
      <button
        type="button"
        className="ghost-btn"
        disabled={!hasNext}
        onClick={() => goTo(page + 1)}
        aria-label={t('pager.next')}
      >
        ›
      </button>
    </div>
  )
}
