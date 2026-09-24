# SocialFlow web

React + TypeScript + Vite frontend for Redeemer Holding. Primary brand color: `#da2e29`.

## Run locally

The frontend is included under `src` in `Redeemer.SocialFlow.slnx` as a Visual Studio
JavaScript/TypeScript project (`Redeemer.SocialFlow.Web.esproj`). Reload the solution
if it was already open. Visual Studio requires the JavaScript/TypeScript project
tooling (available with the ASP.NET/web or Node.js development workloads).
Run `npm ci` once before building. Building the frontend project runs `npm run build`;
starting it runs `npm run dev`. Command-line solution builds may skip JavaScript
projects, so use `dotnet build Redeemer.SocialFlow.Web/Redeemer.SocialFlow.Web.esproj`
from the repository root to build the frontend explicitly.

Use Node.js 22.12+ and npm 11+ (recommended). On Windows PowerShell, use `npm.cmd` if
the execution policy blocks `npm.ps1`.

From the repository root, initialize the database and start the existing API:

```powershell
dotnet ef database update --project Redeemer.SocialFlow.Infrastructure --startup-project Redeemer.SocialFlow.Api
dotnet run --project Redeemer.SocialFlow.Api --no-launch-profile -- --urls http://localhost:5080 --environment Development
```

SQLite resolves relative paths against the process working directory. To ensure both
commands use the same database, set `ConnectionStrings__SocialFlow` to an **absolute**
SQLite path before running them, for example:

```powershell
$env:ConnectionStrings__SocialFlow = "Data Source=$PWD/socialflow.db"
```

In another terminal:

```powershell
cd Redeemer.SocialFlow.Web
npm ci
npm run dev
```

Open `http://127.0.0.1:5173`. Vite proxies `/api` to `http://localhost:5080`, so no
backend CORS changes are required. Copy `.env.example` to `.env.local` to override
`API_PROXY_TARGET`. Restart Vite after changing environment variables.

## Features

- Dashboard: all workflow counts, future scheduled posts, and recently updated posts.
- Calendar: monthly view by scheduled date, previous/next/today navigation, platform
  labels, and post details. Smaller screens can horizontally scroll the month grid.
- Posts: platform/status/creation-date filters sent to the API, text search within
  the results, and loading, empty, failure, and retry states.
- Create a draft with title/content/platform; edit all fields supported by the API.
- Submit for review, approve, reject, schedule, cancel, and confirm deletion of drafts
  or rejected posts. Button availability is a presentation hint; the API owns every
  business rule and can reject stale actions. No client workflow validation is copied.
- ProblemDetails title/detail, field errors, and reference IDs are displayed in place.
  Failed saves preserve user input. Buttons are disabled during writes.
- Semantic forms, keyboard-accessible native dialogs, focus restoration, reduced-motion
  support, and desktop/mobile layouts. No authentication or external publishing.

All displayed dates use the browser's timezone. Calendar grouping uses local scheduled
dates. Date filters cover entire local creation dates, inclusively; scheduling converts
local input to an ISO instant. The API's unpaginated list limitation still applies.

## Structure and deployment

`src/api/posts.ts` is the only HTTP client and owns API contracts and ProblemDetails
handling. `src/pages` contains the three screens; `src/components` contains shared UI,
editor, and post actions. Hash navigation supports reloads without server rewrite rules.

`npm run build` type-checks the app and creates static assets in `dist/`. Serve those
assets through a static host with a same-origin `/api` reverse proxy to ASP.NET Core.
`VITE_API_BASE_URL` can override the API prefix at build time; an external origin also
requires a deliberate CORS policy on that server. Environment values are public and
must not contain secrets. Vite's dev proxy is not part of the production bundle.

Fonts use Google Fonts with local system fallbacks. The wordmark is a text treatment
for this MVP; no official logo asset was supplied.

## Checks

```powershell
npm run build
npm test
npm run format:check
```

Vitest and Testing Library cover API contracts, abort/error handling, workflow actions,
draft creation/editing/deletion, timezone conversion, filters, calendar placement,
dashboard counts, navigation, refresh, and loading/empty/error states. HTTP calls are
mocked in frontend tests; backend API integration tests exercise the real SQLite API.
