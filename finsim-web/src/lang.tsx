import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'

export type Lang = 'tr' | 'en'

const KEY = 'finsim_lang'

const tr = {
  'app.tagline': 'Borsa Simülasyonu',
  'admin.reloadPrice': 'Fiyatı Yenile',
  'admin.notAuthorized': 'Bu sayfayı görüntüleme yetkiniz yok.',
  'stock.unknownSymbol': 'Bilinmeyen sembol: {{symbol}}',
  'stock.backToMarket': 'Piyasaya dön',
  'app.market': 'Market',
  'app.logout': 'Çıkış',
  'app.toDay': 'Gündüz moduna geç',
  'app.toNight': 'Gece moduna geç',
  'app.close': 'Kapat',
  'app.offline': 'connection lost',

  'nav.toggle': 'Menü',
  'nav.favorites': 'Favoriler',
  'nav.portfolio': 'Portföyüm',
  'nav.market': 'Borsa',
  'nav.comingSoon': 'Yakında',

  'strip.equity': 'Hesap Değeri',
  'strip.openPL': 'Açık pozisyon',
  'strip.free': 'Hesap Bakiyesi',
  'strip.position': 'Pozisyon',
  'strip.realized': 'Kâr/Zarar',
  'strip.locked': 'kilitli {n}',
  'strip.margin': 'marj {n}',

  'board.title': 'Tahta',
  'board.note': '{n} enstrüman · emir için seç',
  'board.lots': '{n} lot',
  'board.locked': '{n} kilitli',
  'board.avgCost': 'ort. {n}',
  'board.noPosition': 'pozisyon yok',
  'board.closed': 'işleme kapalı',
  'board.portfolioTitle': 'Portföyüm',
  'board.portfolioNote': '{n} pozisyon',
  'board.portfolioEmpty': 'Henüz pozisyon yok.',
  'board.favoritesNote': '{n} enstrüman',
  'board.favoritesEmpty': 'Henüz favori enstrüman yok.',
  'board.otherTitle': 'Diğer Hisseler',
  'board.otherNote': '{n} enstrüman · emir için seç',
  'board.otherEmpty': 'Enstrüman yok.',
  'board.fundsTitle': 'Fonlar',
  'board.fundsNote': '{n} fon · emir için seç',
  'board.fundBadge': 'FON',
  'board.shortBadge': 'AÇIĞA SATIŞ',
  'board.favorite': 'Favorilere ekle',
  'board.unfavorite': 'Favorilerden çıkar',
  'fs.open': 'Açılış',
  'fs.high': 'En yüksek',
  'fs.low': 'En düşük',
  'fs.volume': 'Hacim',
  'fs.loading': 'Geçmiş veriler yükleniyor…',
  'fs.areaView': 'Alan',
  'fs.candleView': 'Mum',
  'board.shortLots': '{n} lot açık',
  'search.placeholder': 'Sembol veya isim ara',
  'search.noResults': 'Eşleşen hisse yok',
  'sort.symbolAsc': 'Sembol A→Z',
  'sort.symbolDesc': 'Sembol Z→A',
  'sort.priceDesc': 'Fiyat yüksek→düşük',
  'sort.priceAsc': 'Fiyat düşük→yüksek',

  'ledger.title': 'Emir Defteri',
  'ledger.note': 'son 50 kayıt',
  'ledger.empty': 'Henüz emir yok. Tahtadan bir hisse seç, aşağıdan adet gir.',
  'ledger.symbol': 'Hisse',
  'ledger.type': 'Tip',
  'ledger.side': 'Yön',
  'ledger.qty': 'Adet',
  'ledger.price': 'Fiyat',
  'ledger.limit': 'Limit',
  'ledger.avgFill': 'Ort. Fiyat',
  'ledger.status': 'Durum',
  'ledger.locked': 'Kilitli',
  'ledger.cancel': 'iptal et',
  'ledger.expiresIn': '{n} sonra dolar',
  'ledger.replace': 'yeniden ver',

  'pager.prev': 'Önceki sayfa',
  'pager.next': 'Sonraki sayfa',

  'order.market': 'Piyasa',
  'order.limit': 'Limit',
  'order.buy': 'Alış',
  'order.sell': 'Satış',
  'status.pending': 'Bekliyor',
  'status.filled': 'Gerçekleşti',
  'status.cancelled': 'İptal',
  'status.expired': 'Süresi Doldu',

  'ticket.instrument': 'Enstrüman',
  'ticket.pick': 'Tahtadan seç',
  'ticket.orderType': 'Emir tipi',
  'ticket.qty': 'Adet',
  'ticket.limitPrice': 'Limit fiyatı',
  'ticket.stopPrice': 'Stop fiyatı',
  'ticket.stopHint': 'satışta, ops.',
  'ticket.expiry': 'Geçerlilik',
  'ticket.expiryDate': 'Son geçerlilik tarihi',
  'ticket.buy': 'Al',
  'ticket.sell': 'Sat',
  'ticket.marginPreview': 'Ayrılacak marj: ₺{n}',

  'err.minQty': 'Adet en az 1 olmalı.',
  'err.minPrice': 'Limit fiyatı 0’dan büyük olmalı.',
  'err.minStop': 'Stop fiyatı 0’dan büyük olmalı.',
  'err.stopTooHigh': 'Stop fiyatı hem limit fiyatının hem de güncel fiyatın altında olmalı.',
  'err.orderFailed': 'Emir geçmedi.',
  'err.cancelFailed': 'İptal geçmedi.',

  // server codes
  'srv.InvalidCredentials': 'Kullanıcı adı veya parola hatalı.',
  'srv.UsernameTaken': 'Bu kullanıcı adı alınmış.',
  'srv.EmailTaken': 'Bu e-posta adresi alınmış.',
  'srv.AccountCreated': 'Hesap açıldı. Şimdi giriş yapabilirsin.',
  'srv.ResetLinkSent': 'Bu adres kayıtlıysa sıfırlama bağlantısı gönderildi.',
  'srv.PasswordUpdated': 'Parolan güncellendi.',
  'srv.OrderCancelled': 'Emir iptal edildi.',
  'srv.UserNotFound': 'Kullanıcı bulunamadı.',
  'srv.InstrumentNotFound': 'Hisse bulunamadı.',
  'srv.OrderNotFound': 'Emir bulunamadı.',
  'srv.InstrumentInactive': 'Bu hisse işleme kapalı.',
  'srv.InsufficientFunds': 'Yeterli bakiyen yok.',
  'srv.NoPosition': 'Bu hissede pozisyonun yok.',
  'srv.InsufficientShares': 'Yeterli lotun yok.',
  'srv.NotCancellable': 'Sadece bekleyen emirler iptal edilebilir.',
  'srv.OrderFailed': 'Emir işlenemedi.',
  'srv.InvalidEmail': 'Geçerli bir e-posta adresi gir.',
  'srv.PasswordTooShort': 'Parola en az 8 karakter olmalı.',
  'srv.PasswordRequiresDigit': 'Parola en az bir rakam içermeli.',
  'srv.PasswordRequiresUpper': 'Parola en az bir büyük harf içermeli.',
  'srv.PasswordRequiresLower': 'Parola en az bir küçük harf içermeli.',
  'srv.PasswordRequiresNonAlphanumeric': 'Parola en az bir sembol içermeli.',
  'srv.PasswordRequiresUniqueChars': 'Parola daha fazla farklı karakter içermeli.',
  'srv.PasswordMismatch': 'Mevcut parola hatalı.',
  'srv.InvalidToken': 'Bağlantı geçersiz veya süresi dolmuş.',
  'srv.DuplicateUserName': 'Bu kullanıcı adı alınmış.',
  'srv.DuplicateEmail': 'Bu e-posta adresi alınmış.',
  'srv.unknown': 'Bir hata oluştu.',
  'srv.InvalidQuantity': 'Adet en az 1 olmalı.',
  'srv.InvalidPrice': 'Fiyat 0’dan büyük olmalı.',
  'srv.InvalidStopPrice': 'Stop fiyatı geçersiz. Yalnızca satışta, limit ve güncel fiyatın altında olabilir.',
  'srv.CrossingNotAllowed': 'Bu emir pozisyonu sıfırın öbür tarafına geçiriyor. Önce mevcut pozisyonu kapat.',
  'srv.InsufficientMargin': 'Açığa satış için yeterli marjın yok.',
  'srv.NotExpired': 'Sadece süresi dolmuş emirler yeniden verilebilir.',
  'srv.OrderTypeNotSupported': 'Bu emir tipi bu işlemi desteklemiyor.',
  'srv.InvalidExpiry': 'Geçerlilik süresi negatif olamaz.',

  'gate.tag': 'Financial Terminal',
  'gate.username': 'Kullanıcı adı',
  'gate.password': 'Parola',
  'gate.email': 'E-posta',
  'gate.firstName': 'Ad',
  'gate.lastName': 'Soyad',
  'gate.pwHint': 'En az 8 karakter, bir büyük harf, bir rakam ve bir sembol.',
  'gate.login': 'Giriş yap',
  'gate.register': 'Hesap aç',
  'gate.sendReset': 'Sıfırlama bağlantısı gönder',
  'gate.toRegister': 'Hesabın yok mu? Hesap aç',
  'gate.toForgot': 'Parolamı unuttum',
  'gate.toLogin': 'Girişe dön',
  'gate.forgotHint': 'Hesabının e-posta adresini gir, sıfırlama bağlantısını gönderelim.',
  'gate.registered': 'Hesap açıldı. Şimdi giriş yapabilirsin.',
  'gate.resetSent': 'Bu adres kayıtlıysa sıfırlama bağlantısı gönderildi.',
  'gate.noConnection': 'Bağlantı kurulamadı.',
  'gate.showPassword': 'Parolayı göster',
  'gate.hidePassword': 'Parolayı gizle',

  'landing.headline': 'Canlı Piyasa Simülasyonu',
  'landing.sub': 'FinSim, BIST enstrümanlarında canlı fiyat akışıyla çalışan bir borsa simülasyonudur. Sanal TL bakiyenle piyasa ve limit emirleri ver, portföyünü anlık izle.',
  'landing.f1': 'Canlı fiyat akışı',
  'landing.f2': 'Piyasa ve limit emirleri',
  'landing.f3': 'Anlık portföy ve kâr/zarar',
  'landing.f4': 'Sanal bakiye, gerçek risk yok',
  'landing.stat1Label': 'enstrüman',
  'landing.stat2Value': 'Piyasa + Limit',
  'landing.stat2Label': 'emir tipi',
  'landing.stat3Value': 'Canlı',
  'landing.stat3Label': 'fiyat akışı',
  'landing.connecting': 'Canlı piyasa verisi bekleniyor…',

  'reset.tag': 'Parola Sıfırlama',
  'reset.account': 'Hesap:',
  'reset.newPassword': 'Yeni parola',
  'reset.newPasswordAgain': 'Yeni parola (tekrar)',
  'reset.submit': 'Parolayı güncelle',
  'reset.mismatch': 'Parolalar eşleşmiyor.',
  'reset.done': 'Parolan güncellendi. Girişe yönlendiriliyorsun…',
  'reset.invalid': 'Bağlantı geçersiz veya süresi dolmuş.',


  'srv.ServerError': 'Sunucuda beklenmeyen bir hata oluştu.',
  'ledger.spent': 'Tutar',

  'tx.title': 'İşlem Geçmişi',
  'tx.note': 'son 50 kayıt',
  'tx.empty': 'Henüz gerçekleşmiş işlem yok.',
  'tx.symbol': 'Hisse',
  'tx.side': 'Yön',
  'tx.qty': 'Adet',
  'tx.price': 'Fiyat',
  'tx.total': 'Tutar',
  'tx.date': 'Tarih',
  'status.rejected': 'Reddedildi',

  'srv.ConcurrencyConflict': 'Piyasa aynı anda güncellendi, tekrar dene.',
  'srv.AlreadyInactive': 'Bu hisse zaten işleme kapalı.',
  'srv.InvalidAmount': 'Geçersiz tutar.',

  'admin.panelButton': 'Yönetim',
  'admin.title': 'Yönetim Paneli',
  'admin.instrumentsTitle': 'Enstrümanlar',
  'admin.userSearchPlaceholder': 'Kullanıcı adı veya e-posta ara',
  'sort.nameAsc': 'İsim A→Z',
  'sort.nameDesc': 'İsim Z→A',
  'admin.symbol': 'Sembol',
  'admin.name': 'İsim',
  'admin.basePrice': 'Taban Fiyat',
  'admin.create': 'Oluştur',
  'admin.active': 'Aktif',
  'admin.inactive': 'Pasif',
  'admin.deactivate': 'devre dışı bırak',
  'admin.reactivate': 'yeniden etkinleştir',
  'admin.confirmDeactivateTitle': 'Enstrümanı devre dışı bırak',
  'admin.confirmDeactivateBody':
    '{symbol} devre dışı bırakılacak. {users} kullanıcının elindeki toplam {shares} lot, {price} fiyatından zorla satılacak. Bu işlem geri alınamaz.',
  'admin.confirmDeactivateNoHoldings':
    '{symbol} devre dışı bırakılacak. Hiçbir kullanıcının pozisyonu yok.',
  'admin.confirm': 'Onayla',
  'admin.cancel': 'Vazgeç',
  'admin.instrumentCreated': 'Enstrüman oluşturuldu.',
  'admin.deactivated': 'Enstrüman devre dışı bırakıldı.',
  'admin.reactivated': 'Enstrüman yeniden etkinleştirildi.',
  'admin.usersTitle': 'Kullanıcılar',
  'admin.botUsersTitle': 'Bot Kullanıcılar',
  'admin.botView': 'Bot Görünümü',
  'admin.closeBotView': 'Bot Görünümünü Kapat',
  'admin.botCount': 'Bot Sayısı',
  'admin.totalFreeCash': 'Toplam Serbest Bakiye',
  'admin.totalLockedCash': 'Toplam Kilitli Bakiye',
  'admin.totalRealized': 'Toplam Gerçekleşen K/Z',
  'admin.winLoss': 'Kazanan / Kaybeden',
  'admin.totalDeposits': 'Toplam Başlangıç Bütçesi',
  'admin.totalAccountValue': 'Toplam Hesap Değeri',
  'admin.totalNetPnl': 'Toplam Net K/Z',
  'admin.avgNetPnl': 'Bot Başına Ort. Net K/Z',
  'admin.netWorthTitle': 'Bot Bazında Net K/Z',
  'admin.initialBudget': 'Başlangıç Bütçesi',
  'admin.holdingsValue': 'Pozisyon Değeri',
  'admin.netPnl': 'Net K/Z',
  'admin.exposureTitle': 'Enstrüman Bazında Bot Pozisyonu',
  'admin.netQty': 'Net Adet',
  'admin.lockedQty': 'Kilitli Adet',
  'admin.marketValue': 'Piyasa Değeri',
  'admin.botsHolding': 'Bot Sayısı',
  'admin.topGainers': 'En Karlı Botlar',
  'admin.topLosers': 'En Zararlı Botlar',
  'admin.noExposure': 'Hiçbir bot pozisyon tutmuyor.',
  'admin.cashUtilTitle': 'Bot Bakiye Kullanımı',
  'admin.utilPct': 'Kilitli %',
  'admin.free': 'Serbest',
  'admin.locked': 'Kilitli',
  'admin.realized': 'Gerçekleşen K/Z',
  'admin.noHoldings': 'pozisyon yok',
  'admin.cashDelta': 'Tutar (+/-)',
  'admin.reason': 'Açıklama',
  'admin.applyCash': 'Bakiyeyi güncelle',
  'admin.shareInstrument': 'Enstrüman',
  'admin.shareQty': 'Adet (+/-)',
  'admin.applyShares': 'Payları güncelle',
  'admin.cashApplied': 'Bakiye güncellendi.',
  'admin.sharesApplied': 'Paylar güncellendi.',

  'alert.liquidatedTitle': 'POZİSYON ZORUNLU KAPATILDI',
  'alert.liquidatedBody': '{symbol}: marj çağrısı nedeniyle {qty} lotluk açığa satış {amount} karşılığında zorla kapatıldı.',
  'alert.marginCall': 'Marj çağrısı riski: açığa satış pozisyonun(un) bakım marjına yaklaşıyor. Zamanında karşılanmazsa otomatik olarak kapatılır.',
  'pending.title': 'Open Orders',
  'pending.note': 'all',
  'pending.empty': 'No open orders.',
  
  'pnl.title': 'Kâr/Zarar Geçmişi',
  'pnl.live': 'canlı',
  'pnl.empty': 'Henüz geçmiş yok — ilk kayıt yarın alınacak.',
  'pnl.portfolioValue': 'Portföy',
  'pnl.realized': 'Gerçekleşen',
  'pnl.range.30': '30G',
  'pnl.range.90': '90G',
  'pnl.range.365': '1Y',

  'status.partiallyFilled': 'Kısmen Gerçekleşti'
}

const en: typeof tr = {
  'admin.reloadPrice': 'Reload Price',
  'admin.notAuthorized': 'You are not authorized to view this page.',
  'stock.unknownSymbol': 'Unknown symbol: {{symbol}}',
  'stock.backToMarket': 'Back to market',
  'status.partiallyFilled': 'Partially Filled',
  'app.tagline': 'Stock Market Simulator',
  'app.market': 'Market',
  'app.logout': 'Sign out',
  'app.toDay': 'Switch to light mode',
  'app.toNight': 'Switch to dark mode',
  'app.close': 'Close',
  'app.offline': 'connection lost',

  'nav.toggle': 'Menu',
  'nav.favorites': 'Favorites',
  'nav.portfolio': 'My Portfolio',
  'nav.market': 'Stock Market',
  'nav.comingSoon': 'Coming soon',

  'strip.equity': 'Account Value',
  'strip.openPL': 'Open position',
  'strip.free': 'Account Balance',
  'strip.position': 'Holdings',
  'strip.realized': 'Realized P/L',
  'strip.locked': 'locked {n}',
  'strip.margin': 'margin {n}',

  'board.title': 'Board',
  'board.note': '{n} instruments · select one to trade',
  'board.lots': '{n} lots',
  'board.locked': '{n} locked',
  'board.avgCost': 'avg {n}',
  'board.noPosition': 'no position',
  'board.closed': 'not tradeable',
  'board.portfolioTitle': 'My Portfolio',
  'board.portfolioNote': '{n} positions',
  'board.portfolioEmpty': 'No holdings yet.',
  'board.favoritesNote': '{n} instruments',
  'board.favoritesEmpty': 'No favorite instruments yet.',
  'board.otherTitle': 'Other Stocks',
  'board.otherNote': '{n} instruments · select one to trade',
  'board.otherEmpty': 'No instruments available.',
  'board.fundsTitle': 'Funds',
  'board.fundsNote': '{n} funds · select one to trade',
  'board.fundBadge': 'FUND',
  'board.shortBadge': 'SHORT',
  'board.favorite': 'Add to favorites',
  'board.unfavorite': 'Remove from favorites',
  'fs.open': 'Open',
  'fs.high': 'High',
  'fs.low': 'Low',
  'fs.volume': 'Volume',
  'fs.loading': 'Loading history…',
  'fs.areaView': 'Area',
  'fs.candleView': 'Candle',
  'board.shortLots': '{n} lots short',
  'search.placeholder': 'Search symbol or name',
  'search.noResults': 'No matching stocks',
  'sort.symbolAsc': 'Symbol A→Z',
  'sort.symbolDesc': 'Symbol Z→A',
  'sort.priceDesc': 'Price high→low',
  'sort.priceAsc': 'Price low→high',

  'ledger.title': 'Order Book',
  'ledger.note': 'last 50 records',
  'ledger.empty': 'No orders yet. Pick a stock from the board and enter a quantity below.',
  'ledger.symbol': 'Symbol',
  'ledger.type': 'Type',
  'ledger.side': 'Side',
  'ledger.qty': 'Qty',
  'ledger.price': 'Price',
  'ledger.limit': 'Limit',
  'ledger.avgFill': 'Avg Fill',
  'ledger.status': 'Status',
  'ledger.locked': 'Locked',
  'ledger.cancel': 'cancel',
  'ledger.expiresIn': 'expires in {n}',
  'ledger.replace': 're-place',

  'pager.prev': 'Previous page',
  'pager.next': 'Next page',

  'order.market': 'Market',
  'order.limit': 'Limit',
  'order.buy': 'Buy',
  'order.sell': 'Sell',
  'status.pending': 'Pending',
  'status.filled': 'Filled',
  'status.cancelled': 'Cancelled',
  'status.expired': 'Expired',

  'ticket.instrument': 'Instrument',
  'ticket.pick': 'Select from board',
  'ticket.orderType': 'Order type',
  'ticket.qty': 'Quantity',
  'ticket.limitPrice': 'Limit price',
  'ticket.stopPrice': 'Stop price',
  'ticket.stopHint': 'sell only, opt.',
  'ticket.expiry': 'Expiry',
  'ticket.expiryDate': 'Expiry date',
  'ticket.buy': 'Buy',
  'ticket.sell': 'Sell',
  'ticket.marginPreview': 'Margin to reserve: ₺{n}',

  'err.minQty': 'Quantity must be at least 1.',
  'err.minPrice': 'Limit price must be greater than 0.',
  'err.minStop': 'Stop price must be greater than 0.',
  'err.stopTooHigh': 'Stop price must be below both the limit price and the current price.',
  'err.orderFailed': 'Order was rejected.',
  'err.cancelFailed': 'Cancel was rejected.',

  // server codes
  'srv.InvalidCredentials': 'Username or password is incorrect.',
  'srv.UsernameTaken': 'That username is taken.',
  'srv.EmailTaken': 'That email address is taken.',
  'srv.AccountCreated': 'Account created. You can sign in now.',
  'srv.ResetLinkSent': 'If that address is registered, a reset link has been sent.',
  'srv.PasswordUpdated': 'Your password has been updated.',
  'srv.OrderCancelled': 'Order cancelled.',
  'srv.UserNotFound': 'User not found.',
  'srv.InstrumentNotFound': 'Instrument not found.',
  'srv.OrderNotFound': 'Order not found.',
  'srv.InstrumentInactive': 'That instrument is not tradeable.',
  'srv.InsufficientFunds': 'Not enough cash.',
  'srv.NoPosition': 'You hold no position in that instrument.',
  'srv.InsufficientShares': 'Not enough shares.',
  'srv.NotCancellable': 'Only pending orders can be cancelled.',
  'srv.OrderFailed': 'The order could not be processed.',
  'srv.InvalidEmail': 'Enter a valid email address.',
  'srv.PasswordTooShort': 'Password must be at least 8 characters.',
  'srv.PasswordRequiresDigit': 'Password must contain a digit.',
  'srv.PasswordRequiresUpper': 'Password must contain an uppercase letter.',
  'srv.PasswordRequiresLower': 'Password must contain a lowercase letter.',
  'srv.PasswordRequiresNonAlphanumeric': 'Password must contain a symbol.',
  'srv.PasswordRequiresUniqueChars': 'Password must use more distinct characters.',
  'srv.PasswordMismatch': 'Current password is incorrect.',
  'srv.InvalidToken': 'This link is invalid or has expired.',
  'srv.DuplicateUserName': 'That username is taken.',
  'srv.DuplicateEmail': 'That email address is taken.',
  'srv.unknown': 'Something went wrong.',
  'srv.InvalidQuantity': 'Quantity must be at least 1.',
  'srv.InvalidPrice': 'Price must be greater than 0.',
  'srv.InvalidStopPrice': 'Invalid stop price. Sell orders only, below the limit and current price.',
  'srv.CrossingNotAllowed': 'That order would cross through zero. Close the current position first.',
  'srv.InsufficientMargin': 'Not enough margin available to short this.',
  'srv.NotExpired': 'Only expired orders can be re-placed.',
  'srv.OrderTypeNotSupported': 'This order type does not support that action.',
  'srv.InvalidExpiry': 'Expiry cannot be negative.',


  'gate.tag': 'Financial Terminal',
  'gate.username': 'Username',
  'gate.password': 'Password',
  'gate.email': 'Email',
  'gate.firstName': 'First name',
  'gate.lastName': 'Last name',
  'gate.pwHint': 'At least 8 characters, one uppercase letter, one digit and one symbol.',
  'gate.login': 'Sign in',
  'gate.register': 'Create account',
  'gate.sendReset': 'Send reset link',
  'gate.toRegister': "Don't have an account? Sign up",
  'gate.toForgot': 'Forgot my password',
  'gate.toLogin': 'Back to sign in',
  'gate.forgotHint': 'Enter your account email and we will send a reset link.',
  'gate.registered': 'Account created. You can sign in now.',
  'gate.resetSent': 'If that address is registered, a reset link has been sent.',
  'gate.noConnection': 'Could not reach the server.',
  'gate.showPassword': 'Show password',
  'gate.hidePassword': 'Hide password',

  'landing.headline': 'Live Market Simulation',
  'landing.sub': 'FinSim is a stock market simulator running on live BIST-style pricing. Place market and limit orders with a virtual TL balance and watch your portfolio move in real time.',
  'landing.f1': 'Live price feed',
  'landing.f2': 'Market and limit orders',
  'landing.f3': 'Real-time portfolio and P&L',
  'landing.f4': 'Virtual balance, no real risk',
  'landing.stat1Label': 'instruments',
  'landing.stat2Value': 'Market + Limit',
  'landing.stat2Label': 'order types',
  'landing.stat3Value': 'Live',
  'landing.stat3Label': 'price feed',
  'landing.connecting': 'Waiting for live market data…',

  'reset.tag': 'Password Reset',
  'reset.account': 'Account:',
  'reset.newPassword': 'New password',
  'reset.newPasswordAgain': 'New password (again)',
  'reset.submit': 'Update password',
  'reset.mismatch': 'Passwords do not match.',
  'reset.done': 'Password updated. Redirecting to sign in…',
  'reset.invalid': 'This link is invalid or has expired.',

  'srv.ServerError': 'Something went wrong on the server.',
  'ledger.spent': 'Amount',

  'tx.title': 'Transaction History',
  'tx.note': 'last 50 records',
  'tx.empty': 'No transactions yet.',
  'tx.symbol': 'Symbol',
  'tx.side': 'Side',
  'tx.qty': 'Qty',
  'tx.price': 'Price',
  'tx.total': 'Amount',
  'tx.date': 'Date',

  'status.rejected': 'Rejected',

  'srv.ConcurrencyConflict': 'The market updated at the same moment, try again.',
  'srv.AlreadyInactive': 'That instrument is already inactive.',
  'srv.InvalidAmount': 'Invalid amount.',

  'admin.panelButton': 'Admin',
  'admin.title': 'Admin Panel',
  'admin.instrumentsTitle': 'Instruments',
  'admin.userSearchPlaceholder': 'Search username or email',
  'sort.nameAsc': 'Name A→Z',
  'sort.nameDesc': 'Name Z→A',
  'admin.symbol': 'Symbol',
  'admin.name': 'Name',
  'admin.basePrice': 'Base Price',
  'admin.create': 'Create',
  'admin.active': 'Active',
  'admin.inactive': 'Inactive',
  'admin.deactivate': 'deactivate',
  'admin.reactivate': 'reactivate',
  'admin.confirmDeactivateTitle': 'Deactivate instrument',
  'admin.confirmDeactivateBody':
    '{symbol} will be deactivated. {shares} shares held by {users} users will be force-sold at {price}. This cannot be undone.',
  'admin.confirmDeactivateNoHoldings':
    '{symbol} will be deactivated. No user currently holds a position in it.',
  'admin.confirm': 'Confirm',
  'admin.cancel': 'Cancel',
  'admin.instrumentCreated': 'Instrument created.',
  'admin.deactivated': 'Instrument deactivated.',
  'admin.reactivated': 'Instrument reactivated.',
  'admin.usersTitle': 'Users',
  'admin.botUsersTitle': 'Bot Users',
  'admin.botView': 'Bot View',
  'admin.closeBotView': 'Close Bot View',
  'admin.botCount': 'Bot Count',
  'admin.totalFreeCash': 'Total Free Cash',
  'admin.totalLockedCash': 'Total Locked Cash',
  'admin.totalRealized': 'Total Realized P&L',
  'admin.winLoss': 'Winning / Losing',
  'admin.totalDeposits': 'Total Initial Budget',
  'admin.totalAccountValue': 'Total Account Value',
  'admin.totalNetPnl': 'Total Net P&L',
  'admin.avgNetPnl': 'Avg Net P&L / Bot',
  'admin.netWorthTitle': 'Net P&L by Bot',
  'admin.initialBudget': 'Initial Budget',
  'admin.holdingsValue': 'Holdings Value',
  'admin.netPnl': 'Net P&L',
  'admin.exposureTitle': 'Bot Exposure by Instrument',
  'admin.netQty': 'Net Qty',
  'admin.lockedQty': 'Locked Qty',
  'admin.marketValue': 'Market Value',
  'admin.botsHolding': '# Bots',
  'admin.topGainers': 'Top Gainers',
  'admin.topLosers': 'Top Losers',
  'admin.noExposure': 'No bot holds a position.',
  'admin.cashUtilTitle': 'Bot Cash Utilization',
  'admin.utilPct': 'Locked %',
  'admin.free': 'Free',
  'admin.locked': 'Locked',
  'admin.realized': 'Realized P/L',
  'admin.noHoldings': 'no holdings',
  'admin.cashDelta': 'Amount (+/-)',
  'admin.reason': 'Reason',
  'admin.applyCash': 'Update balance',
  'admin.shareInstrument': 'Instrument',
  'admin.shareQty': 'Quantity (+/-)',
  'admin.applyShares': 'Update shares',
  'admin.cashApplied': 'Balance updated.',
  'admin.sharesApplied': 'Shares updated.',

  'alert.liquidatedTitle': 'POSITION FORCE-CLOSED',
  'alert.liquidatedBody': '{symbol}: your {qty}-lot short was force-covered for {amount} on a margin call.',
  'alert.marginCall': 'Margin call risk: a short position is approaching its maintenance margin. It will be force-covered automatically if not addressed.',
  'pending.title': 'Open Orders',
  'pending.note': 'all',
  'pending.empty': 'No open orders.',

  'pnl.title': 'P&L History',
  'pnl.live': 'live',
  'pnl.empty': 'No history yet — the first record is taken tomorrow.',
  'pnl.portfolioValue': 'Portfolio',
  'pnl.realized': 'Realized',
  'pnl.range.30': '30D',
  'pnl.range.90': '90D',
  'pnl.range.365': '1Y',
}

export type LangKey = keyof typeof tr

const dictionaries: Record<Lang, typeof tr> = { tr, en }

function initial(): Lang {
  const saved = localStorage.getItem(KEY)
  if (saved === 'tr' || saved === 'en') return saved
  return navigator.language.startsWith('tr') ? 'tr' : 'en'
}

type LangValue = {
  lang: Lang
  toggle: () => void
  t: (key: LangKey, vars?: Record<string, string | number>) => string
  tServer: (data: unknown) => string
}

const LangContext = createContext<LangValue | null>(null)

export function LangProvider({ children }: { children: ReactNode }) {
  const [lang, setLang] = useState<Lang>(initial)

  useEffect(() => {
    document.documentElement.lang = lang
    localStorage.setItem(KEY, lang)
  }, [lang])

  const t = (key: LangKey, vars?: Record<string, string | number>) => {
    let out = dictionaries[lang][key]
    if (vars) {
      for (const [k, v] of Object.entries(vars)) {
        out = out.replace(`{${k}}`, String(v))
      }
    }
    return out
  }

  // Turns a server error payload into readable text.
  // The API sends stable codes ("InsufficientFunds"), not sentences.
  const tServer = (data: unknown): string => {
    const one = (code: string) => {
      const key = `srv.${code}` as LangKey
      return key in dictionaries[lang] ? t(key) : t('srv.unknown')
    }

    if (typeof data === 'string') return one(data)
    if (Array.isArray(data)) return data.map(c => one(String(c))).join(' ')

      // our own middleware: { error: "ServerError", traceId: "..." }
    const wrapped = (data as { error?: string; traceId?: string })
    if (wrapped?.error) {
      const text = one(wrapped.error)
      return wrapped.traceId ? `${text} (${wrapped.traceId.slice(-8)})` : text
    }

    // ASP.NET model validation: { errors: { Password: ["PasswordTooShort"] } }
    const errs = (data as { errors?: Record<string, string[]> })?.errors
    if (errs && typeof errs === 'object') {
      return Object.values(errs).flat().map(c => one(String(c))).join(' ')
    }

    return t('srv.unknown')
  }

  const toggle = () => setLang(l => (l === 'tr' ? 'en' : 'tr'))

  return (
    <LangContext.Provider value={{ lang, toggle, t, tServer }}>
      {children}
    </LangContext.Provider>
  )
}

export function useLang(): LangValue {
  const ctx = useContext(LangContext)
  if (!ctx) throw new Error('useLang must be used inside <LangProvider>')
  return ctx
}