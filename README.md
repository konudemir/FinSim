# FinSim
- FinSim is a financial stock simulator that lets the user act as a trader on a live order book against other users and market maker bots.
- FinSim does not mirror real life stock prices. Stocks follow the *shape* of real world moves taken from a price feed, but the price levels are FinSim's own and drift away from the real ones on purpose.
- FinSim has two different language settings easily configurable: English and Turkish. Just click on the EN / TR button to switch.
- There is also a night and a day theme next to it. The choice is remembered in the browser, and the first visit follows whatever the operating system asks for.

## Tech Stack
* .NET 10.0 for backend
* React, TypeScript and Vite for Frontend
* Tailwind CSS for the styling
* .NET BackgroundService for price movements, order matching and calculations done in intervals
* PostgreSQL for database
* Microsoft.AspNetCore.Identity for all types of password and security operations
* SignalR for real time communication between the frontend and the background service
* Yahoo Finance's chart endpoint as the external price feed
* xUnit for the tests
* Docker for running without dependencies

## Architecture
This project consists of 5 different folders.
finsim-web which contains all of the frontend software.
and 4 different .NET projects inside /src:
#### FinSim.Api:
This is the project that runs the whole thing. It includes program.cs, the background workers, the controllers and the error middleware.
It can reference the Application and Infrastructure projects and also it can reference Domain through Application too.
#### FinSim.Application:
This is the project that has Dto's, interfaces for repositories inside Infrastructure and Services.
The engines that run on every tick (prices, matching, margin, expiry, bots) live here too.
It can reference only Domain.
#### FinSim.Domain:
This project has the classes inside the Models folder. It has the declarations and the implementations (although not really long ones) of the object classes.
It also contains some enums inside Models/Enums.
Domain cannot reference any other projects than itself.
#### FinSim.Infrastructure
This project contains the DBContext for FinSim, data migrations, the repository implementations, the instrument seed data and some Services like EmailSender, JwtTokenService and the Yahoo price source.
It can reference Application and Domain.

The tests live in a separate /tests folder, outside of /src.

## Getting Started

### With Docker

You only need Docker installed.

```bash
docker compose up --build
```

This starts PostgreSQL, applies the migrations, runs the API and serves the frontend.
Open http://localhost:5173 and register an account.

`docker compose down` stops everything. Add `-v` to also delete the database.

The admin panel is given to one account by e-mail. Register first, then put that e-mail into
`Seed__AdminEmail` inside docker-compose.yml and restart. The seeder grants the Admin role on
startup and does nothing if no account is registered under that address yet.

### Without Docker

### Prerequisites

- .NET 10 SDK
- Node 22+
- Docker
- EF Core CLI tools:

```bash
dotnet tool install --global dotnet-ef
```

### 1. Start PostgreSQL

```bash
docker compose up -d db
```

### 2. Configure the API

Put your connection string and JWT settings into `src/FinSim.Api/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "BorsaDb": "Host=localhost;Port=5432;Database=borsadb;Username=postgres;Password=yourpassword"
  },
  "Jwt": {
    "Issuer": "FinSim",
    "Audience": "FinSim",
    "Key": "replace-this-with-32-characters"
  }
}
```

Don't put these into `appsettings.json` instead, that file holds the `Bots` block and overwriting it
turns the bots off.

The JWT key must be 32 characters or longer, otherwise the app throws on startup. You can generate a key with 32 characters on your terminal using:
```powershell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Max 256 }))
```

### 3. Create the database

From the repository root:

```bash
dotnet ef database update -p src\FinSim.Infrastructure -s src\FinSim.Api
```

The API applies the migrations itself on startup too, so you can skip this if you only want to run
the thing. You still need it when you are adding a migration of your own.

### 4. Run the backend

```bash
cd src/FinSim.Api
dotnet run
```

Runs on http://localhost:5209

### 5. Run the frontend

In a separate terminal:

```bash
cd finsim-web
npm install
npm run dev
```

Runs on http://localhost:5173

You can use this url and run the app after signing up.
You may also login once your login token's time is up and you may also reset your password.

## API

All endpoints are served from `http://localhost:5209`. Endpoints whose Auth is **Yes** require a token; the token comes from `POST /api/auth/login` as an httpOnly `finsim_token` cookie and is
valid for an hour. **Admin** means the same token, but the account also needs the Admin role.

### Auth

| Method | Path | Description | Auth |
|---|---|---|---|
| POST | `/api/auth/register` | Create an account and receive 80.000 ₺ of starting cash | No |
| POST | `/api/auth/login` | Post your username and password to get an hour valid JWT, set as a cookie | No |
| POST | `/api/auth/logout` | Clears the token cookie | No |
| GET | `/api/auth/me` | The username the cookie belongs to | Yes |
| POST | `/api/auth/forgot-password` | Sends a link (not currently to an email but to the terminal FinSim.Api is running at) to reset the password. Returns 200 even if the e-mail is not registered | No |
| POST | `/api/auth/reset-password` | Set a new password using the emailed (for now given through the terminal )token | No |

### Instruments

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/api/instruments` | List every instrument (stocks and funds) | No |
| GET | `/api/instruments/board` | The stock board, one page at a time. Takes `sort` (`symbol_asc`, `symbol_desc`, `price_asc`, `price_desc`), an optional `q` that matches symbol or name, and `page` / `limit` | No |
| GET | `/api/instruments/admin-board` | The same board without the filters, so inactive instruments and funds show up too | Admin |
| GET | `/api/instruments/by-id/{id}` | The instrument of the specified id, company profile included | No |
| GET | `/api/instruments/{symbol}` | Gives the stock who has that symbol, for example `THYAO`. Returns 404 if no stock has it | No |
| GET | `/api/instruments/{id}/history` | Price history of one instrument, `from` and `to` are optional | No |
| GET | `/api/instruments/index-history` | The last `points` values of the FinSim index, 120 if you don't say | No |
| POST | `/api/instruments/create` | Adds a new instrument | Admin |
| PUT | `/api/instruments/{id}/active` | Opens an instrument back to trading, or closes it. Closing also liquidates everyone out of it, see below | Admin |
| GET | `/api/instruments/{id}/liquidation-preview` | How many users and how many shares closing this instrument would touch, before you actually do it | Admin |

### Account

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/api/users/balance` | Returns free cash balance, locked cash balance and the total of the two | Yes |
| GET | `/api/transactions` | The trades the user was on either side of, newest first, one page at a time. Takes `page` and `limit` | Yes |
| GET | `/api/users/portfolio` | The stocks that the user holds, includes an average cost for each different stock. A negative quantity is a short position. | Yes |
| GET | `/api/users/portfolio/board` | The same holdings as a board, one page at a time. Takes the same `sort`, `q`, `page` and `limit` as the stock board | Yes |
| GET | `/api/users/pnl-history` | One account value point per day for the last `days` days, 90 if you don't say | Yes |

### Orders

| Method | Path | Description | Auth |
|---|---|---|---|
| POST | `/api/order/market` | Places a market order. Sweeps whatever is resting on the book right now and cancels the rest | Yes |
| POST | `/api/order/limit` | Places a limit order. Stays pending until something on the other side crosses it, checked on each tick. Takes an optional stop price and an optional expiry | Yes |
| POST | `/api/order/{id}/cancel` | Cancels a pending order and release the reserved cash (buy) or shares (sell) | Yes |
| POST | `/api/order/{id}/replace` | Places an expired order again as a brand new one, validated from scratch | Yes |
| GET | `/api/order` | The user's orders, newest first, one page at a time. `open=true` gives the resting ones, `open=false` the settled ones, omitted gives both. Takes `page` and `limit` | Yes |

### Favorites

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/api/favorites` | The instruments the user starred | Yes |
| GET | `/api/favorites/board` | The starred instruments as a board, one page at a time. Takes `sort`, an optional `q` that matches symbol or name, `page` and `limit` | Yes |
| POST | `/api/favorites/{instrumentId}` | Star an instrument | Yes |
| DELETE | `/api/favorites/{instrumentId}` | Unstar it | Yes |

### Funds

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/api/funds` | List every fund with its holdings | No |
| GET | `/api/funds/{id}` | One fund | No |
| POST | `/api/funds` | Creates a fund out of a basket of active stocks | Admin |
| PUT | `/api/funds/{id}/holdings` | Rebalances the basket without moving the fund's price | Admin |

### Admin

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/api/admin/users` | Every user with their cash, position value and net deposits | Admin |
| GET | `/api/admin/users/board` | The same list one page at a time. `bots=true` gives the bot accounts instead of the real ones. Takes `sort` (`name_asc`, `name_desc`), an optional `q`, `page` and `limit` | Admin |
| GET | `/api/admin/book/{instrumentId}` | The open order book of one instrument, both sides, summed into price levels | Admin |
| GET | `/api/admin/book/{instrumentId}/orders` | The individual orders behind one side of that book, newest first, one page at a time. Takes `direction` (`Buy` or `Sell`), `page` and `limit` | Admin |
| POST | `/api/admin/users/{id}/cash` | Adds or removes free cash. Counted as a deposit, not as profit | Admin |
| POST | `/api/admin/users/{id}/shares` | Adds or removes shares as an inventory correction, no cash moves | Admin |
| POST | `/api/admin/instruments/{id}/reload-price` | Pulls the real price for that instrument right now instead of waiting for the poll | Admin |

### Real-time

The frontend opens a SignalR connection to `/hubs/prices`. On every tick the server broadcasts a
`PriceUpdate` event carrying the market move, the FinSim index value and the current price and
traded volume of every instrument. On top of that, any user whose orders were touched during that
tick gets an `OrderUpdate` event with those orders, their new balance and their new portfolio, so
the screen doesn't have to poll for a fill.

## How It Works

### The instrument list
The instruments aren't typed into the code, they come from `seed.json` next to the DbSeeder, which
holds 100 BIST symbols. On startup the seeder inserts whatever is missing, asking the feed for a
real opening price per symbol and falling back to the price written in the file when Yahoo doesn't
answer, so an outage can't fill the database with made up levels. Symbols that are already there are
left alone, which is what makes the seeder safe to run on every boot.

Each row also carries a company profile — sector, industry, city, employees, website, shares
outstanding and a paragraph of description — shown on the instrument page. Only static descriptors
are stored. Anything derived from a price is deliberately left out, because our prices drift from the
real ones and the number would be wrong the moment the sim runs. Shares outstanding is a count and
not a price, so the market cap on the page is computed against FinSim's own price. Rows that predate
these columns get backfilled once, and only where the field is still empty, so an admin's edit
survives the next deploy.

### Price movements
Prices do not move randomly. A background service runs every 15 seconds and two things push a price:

The external feed. Every instrument is mapped to a real symbol, and a few of the stalest ones get
polled each tick (not all of them at once, so the request rate stays flat). The *ratio* between two
consecutive real prices is applied to our own price, not the real price itself. So the move is
borrowed but the level is ours. A ratio that is too big to be believable is treated as a split or as
garbage data, the anchor gets reset and the move is thrown away.

Trades. Every fill sets the instrument's price to the price it traded at, the same as a last-trade
price in a real market.

Funds don't move on their own at all, they are repriced from their basket after the stocks move.

The same background service also computes a FinSim index. It is equal-weighted — the average of every
instrument's `CurrentPrice / BasePrice` ratio, scaled to 10.000 at the start. So a 1.000 ₺ stock
is not more important than a 10 ₺ one. Funds are left out of it, otherwise their constituents would
be counted twice. This result is shown on the frontend to show the user how the market has acted on that particular time interval.

### Order matching
There is a real order book. Every order, from a user or from a bot, rests on it until something on
the other side crosses it. On each tick the matcher walks each instrument's book with the bids sorted
highest price first and the asks lowest price first, FIFO inside the same price. The fill happens at
the *resting* order's price, so whoever was there first gets their price. When neither side is
resting, two market orders meeting on the same tick, it fills at the price the tick started from.

An order can fill in pieces. A big order eats several smaller ones on the other side and sits at
`PartiallyFilled` until the rest of it goes.

A market order isn't a separate thing internally, it's an aggressive limit priced 5% through the
current price that must fill on this tick, and whatever it can't fill is cancelled and refunded
instead of resting on the book.

A limit order reserves cash or stocks instead of spending. Cancelling a pending order releases
whatever was reserved and marks it `Cancelled`. You can give it an expiry, and once it expires
you can re-place it with `replace` — which builds a brand new order and runs every check again,
because between placement and expiry the cash may be gone or the instrument may be closed.

A sell can also carry a stop price. It sits out of the book until the market falls to it, and the
tick that trips it flips the order's `Triggered` flag and turns it into a market sell right there.
The flag is stored rather than recomputed, so a stop that has already fired stays fired and the
book views know not to show an untriggered stop as resting depth.

Two things the walk refuses to do: fill outside a ±5% collar around the price at the start of the
tick, so a single silly quote can't drag a stock anywhere, and match a user against themselves.

### Shorting and margin
Selling a stock you don't hold opens a short position, which shows as a negative quantity in the
portfolio. Instead of reserving shares it reserves cash: 50% of the order's value as initial margin,
and once the position is open the sale proceeds are held as collateral too. Both sit in the locked
balance, and every partial fill recomputes the whole position's collateral from scratch rather than
adding to it, so rounding can't drift over time.

Every tick the margin engine values every short holder's account. If their equity falls under 30% of
what their shorts are worth, all of their short positions are bought back at the market price and
their pending orders on those instruments are cancelled. There is no warning and no partial call.

### Bots
50 market maker bots run in the background so a new user isn't staring at an empty book. Each one
quotes near the current price on whichever side it can afford, in small sizes on purpose so that a
user's order fills across several of them. Their spread, size and role come from their own id, so a
bot behaves the same way across restarts without storing anything.

A bot can't see the book, so aggression is a pricing choice and not a reaction. Most quotes sit off
the current price and rest, and a minority are priced through it, which is the only way a bot ever
crosses. Most bots coin-flip their direction when both sides are open. A small fixed slice of them
are contrarians who lean the other way on purpose, buying into a falling market and selling into a
rising one, and they cross far more often, because leaning against a move only counts if the order
actually trades. A bot that holds nothing in a name would otherwise have to buy every time, so a
bounded share of that flow opens a small short instead. Without it the whole crowd is a one-way bid
on nearly every instrument.

Real markets concentrate volume in a handful of names, so a hot list picked from the instrument ids
takes most of the random picks. That alone would starve the rest purely by chance, so a few
guaranteed actions round-robin through the cold ones every tick and every instrument gets touched
regardless of luck.

They go through the exact same OrderService a user does, with no shortcuts, which is also the point:
if a bot can do it, the path is tested. A resting quote only frees up by filling, so a bot close to
its open order cap cancels its own quotes furthest from the current price to make room instead of
filling up and going silent. Everything about them is in the `Bots` block of appsettings, including
`Enabled` if you want the book to yourself.

### Funds
A fund is an instrument whose price comes from a basket of stocks rather than from a feed. Its price
is the basket value divided by a divisor, and the divisor is picked so the fund starts at the price
you asked for. On a rebalance the divisor is recomputed against the price right now, so swapping the
basket around doesn't put a jump in the chart. Funds can only hold active stocks, which is also what
makes a fund of funds impossible.

### Closing an instrument
Deactivating an instrument doesn't just hide it. Every pending order on it is cancelled and refunded,
then every remaining long is force-sold and every remaining short is force-covered. Those go into the
real book as market orders first so a genuine counterparty can get a fair fill, and only whatever the
book can't absorb is settled directly at the last price. The instrument is delisted after that walk,
not before, because the matcher skips inactive instruments.

### Daily snapshots
A second background worker writes one account valuation per user per day, which is what the P&L chart
reads. It's kept away from the price tick on purpose: it's a full scan over every user and it must not
be able to roll a price tick back. Writing is idempotent, it asks who is missing today's row rather
than remembering when it last ran, so restarts are harmless. Admin cash and share grants are added to
a separate net deposits figure so that free money doesn't show up on the chart as profit. Those grants
are also written to their own audit table, kept away from orders and transactions because a cash
top-up has no instrument and a share grant has no cash leg.

### Paging

The list endpoints take a `page` and a `limit`. `page` is 1-based and `limit` is clamped, 25 if you
don't say and 100 at most, so a client can't ask for the whole table in one request. Anything below
1 falls back to the default rather than erroring, because a bad page number isn't worth failing a
board over.

Every response comes back in the same shape: the rows, the page and page size they were drawn with,
and the total row count for the same filter. The client works out the page count, and whether there
is a next or previous page, from those.

Each query runs twice against the same filter: once to count and once to fetch the page. The count
deliberately runs against the unsorted query, since there's no reason for Postgres to sort rows it
is only going to count, and both see the same filters so the total always describes the set the
page came from.

Every sort ends in the row's id. Without that tiebreak the ordering isn't total — two orders can
share a timestamp and plenty of stocks share a price — and a row that compares equal to another can
land on two different pages, or on none. Symbol sorts collate as Turkish explicitly, so the order
the database returns matches the order the frontend renders.

The board pages leave the description paragraph out of the projection. It's around 1.5KB per
instrument and a list of rows has no use for it, so it only goes over the wire when a single
instrument is asked for.

### Colliding

A user can hit cancel at the exact moment the worker decides their limit order matches. Both read
the order and see `Pending`, without protection both would go on to complete their tasks.

Each order carries a concurrency token, so every update is conditional on the row not having
changed since it was read. Whichever side commits first wins; the other matches zero rows and is
told the order is no longer pending. This way an order can't go on and be refunded at the same time even if it happens at a certain time.

The whole tick — prices, expiries, bots, matching, margin — runs in one transaction. If anything in it
collides, the entire tick rolls back and the same orders get matched again on the next pass.

### Unit Testing

You can run the tests with `dotnet test tests/FinSim.Tests`. There are 164 tests covering the cash
checks, the cash and share reservations, the average cost calculation, the matching engine and its
book scenarios, taker pricing, short positions and their margin, forced liquidation, the P&L history,
net deposits and the paging of every board.

The test project references only Application and Domain.
### Exception Handling

Expected failures come back from the services as result enums, and the controllers turn them into
short codes like `InsufficientFunds` rather than sentences. The frontend maps those codes to text. This leads to
the user getting the error in their own selected language without the API knowing about it.

Anything the services didn't expect — a null reference, a database timeout — is caught by a
middleware instead of reaching the client as a stack trace. The real exception goes to the log and
the caller gets `ServerError` with a trace id it can quote back, plus the actual detail while in
development. A request the browser abandoned mid-flight isn't treated as an error, and a response
that already started writing is left alone, because the headers are gone by then.