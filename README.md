# FinSim
- FinSim is a financial stock simulator that changes stock prices randomly and lets the user act as a trader in this simulation.
- FinSim does not mirror real life stock movements. It is completely random and not real.
- FinSim has two different language settings easily configurable: English and Turkish. Just click on the EN / TUR button to switch.

## Tech Stack
* .NET 10.0 for backend
* React and Vite for Frontend
* .NET BackgroundService for price movements and calculations done in intervals
* PostgreSQL for database
* Microsoft.AspNetCore.Identity for all types of password and security operations
* SignalR for real time communication between the frontend and the background service
* Docker for running without dependencies

## Architecture
This project consists of 5 different folders.
finsim-web which contains all of the frontend software.
and 4 different .NET projects inside /src:
#### FinSim.Api:
This is the project that runs the whole thing. It includes program.cs, the background worker and the controllers.
It can reference the Application and Infrastructure projects and also it can reference Domain through Application too.
#### FinSim.Application:
This is the project that has Dto's, interfaces for repositories inside Infrastructure and Services.
It can reference only Domain.
#### FinSim.Domain:
This project has the classes inside the Models folder. It has the declarations and the implementations (although not really long ones) of the object classes.
It also contains some enums inside Models/Enums.
Domain cannot reference any other projects than itself.
#### FinSim.Infrastructure
This project contains the DBContext for FinSim, data migrations and some Services like EmailSender and JwtTokenService.
It can reference Application and Domain.

## Getting Started

### With Docker

You only need Docker installed.

```bash
docker compose up --build
```

This starts PostgreSQL, applies the migrations, runs the API and serves the frontend.
Open http://localhost:5173 and register an account.

`docker compose down` stops everything. Add `-v` to also delete the database.

### Without Docker

### Prerequisites

- .NET 10 SDK
- Node 20+
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

Create `src/FinSim.Api/appsettings.json`:

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

The JWT key must be 32 characters or longer, otherwise the app throws on startup. You can generate a key with 32 characters on your terminal using:
```powershell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Max 256 }))
```

### 3. Create the database

From the repository root:

```bash
dotnet ef database update -p src\FinSim.Infrastructure -s src\FinSim.Api
```

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

All endpoints are served from `http://localhost:5209`. Endpoints whose Auth is **Yes** require a token; the token comes from `POST /api/auth/login` and is
valid for an hour.

### Auth

| Method | Path | Description | Auth |
|---|---|---|---|
| POST | `/api/auth/register` | Create an account and receive 80.000 ₺ of starting cash | No |
| POST | `/api/auth/login` | Post your username and password to get an hour valid JWT | No |
| POST | `/api/auth/forgot-password` | Sends a link (not currently to an email but to the terminal FinSim.Api is running at) to reset the password. Returns 200 even if the e-mail is not registered | No |
| POST | `/api/auth/reset-password` | Set a new password using the emailed (for now given through the terminal )token | No |

### Instruments

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/api/instruments` | List every instrument (stock) | No |
| GET | `/api/instruments/by-id/{id}` | The instrument of the specified id| No |
| GET | `/api/instruments/{symbol}` | Gives the stock who has that symbol, for example `THYAO`. Returns 404 if no stock has it | No |

### Account

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/api/users/balance` | Returns free cash balance, locked cash balance and the total of the two | Yes |
| GET | `/api/users/portfolio` | The stocks that the user holds, includes an average cost for each different stock. | Yes |

### Orders

| Method | Path | Description | Auth |
|---|---|---|---|
| POST | `/api/order/market` | Places a market order. Fills immediately at the current price | Yes |
| POST | `/api/order/limit` | Places a limit order. Stays pending until the price reaches the limit, checked on each price tick | Yes |
| POST | `/api/order/{id}/cancel` | Cancels a pending order and release the reserved cash (buy) or shares (sell) | Yes |
| GET | `/api/order` | The 50 most recent orders | Yes |

### Real-time

The frontend opens a SignalR connection to `/hubs/prices`. On every tick the server broadcasts a
`PriceUpdate` event carrying the market move, the FinSim index value and the current price of
every instrument.

## How It Works

### Price simulation
A background service simulates the market using 3 parameters:
a market move that has the same effect on all stocks,
a random change happening to every stock differently
and a pull to every stock to its base value.
All 3 parameters are determined using randomness.

The same background service also computes a FinSim index. It is equal-weighted — the average of every
instrument's `CurrentPrice / BasePrice` ratio. So a 1.000 ₺ stock
is not more important than a 10 ₺ one. This result is shown on the frontend to show the user how the market has acted on that particular time interval.

### Order matching
A market order fills immediately at whatever the instrument costs at that moment. Cash moves, the
position is created or its average cost recalculated, and a transaction record is written.

A limit order reserves cash or stocks instead of spending. The order sits as `Pending` until the market tick finds the market price at or
better than the limit, at which point it fills and the order is settled. Cancelling a pending
order releases whatever was reserved and marks it `Cancelled`.

### Colliding

A user can hit cancel at the exact moment the worker decides their limit order matches. Both read
the order and see `Pending`, without protection both would go on to complete their tasks.

Each order carries a concurrency token, so every update is conditional on the row not having
changed since it was read. Whichever side commits first wins; the other matches zero rows and is
told the order is no longer pending. This way an order can't go on and be refunded at the same time even if it happens at a certain time.

### Unit Testing

You can run the tests with `dotnet test tests/FinSim.Tests`. There are 46 tests covering the cash
checks, the cash and share reservations, the average cost calculation and the limit order matching engine.

The test project references only Application and Domain.
### Exception Handling

Expected failures come back from the services as result enums, and the controllers turn them into
short codes like `InsufficientFunds` rather than sentences. The frontend maps those codes to text. This leads to
the user getting the error in their own selected language without the API knowing about it.