# FRISL Enterprise Asset Management System (ASP.NET MVC + Blazor)

This implementation follows your FRISL EAMS documentation using:
- ASP.NET Core MVC for workflow screens
- Blazor Server component for interactive dashboard
- EF Core + SQLite for persistence
- Lifecycle/state machine enforcement with immutable status history
- OpenAPI/Swagger for API documentation

## Implemented Modules
- Asset Registration with auto tag-code generation (`FRISL-YEAR-CAT-DEPT-SEQ`)
- RFID uniqueness and master registration
- Two-leg assignment workflow (initiation + receipt confirmation)
- Repair/replacement/discard workflow
- Loan workflow (internal/external) with external exit-grant creation
- RFID door scan monitoring with unauthorized-exit alerts
- Asset request intake
- Immutable asset lifecycle history
- Admin dashboard metrics (Blazor)
- Audit framework (sessions, results, variance)
- Reporting engine (filtered asset report + CSV export)
- Depreciation report (straight-line) with CSV export
- API-first endpoints for assets and monitoring
- Integration webhooks/queue processing for procurement and RFID batch ingestion
- Lightweight RBAC guard for sensitive actions (`X-Role` header or `?role=` query)

## Run
1. Install .NET 9 SDK
2. From project root:
   - `cd FrislEams.Web`
   - `dotnet restore`
   - `dotnet run`
3. Open `https://localhost:5001` (or printed URL)
4. Swagger UI (development): `/swagger`

## Notes
- Database file: `FrislEams.Web/eams.db`
- Seed data is created automatically for categories, departments, locations, staff, and suppliers.
- Current RBAC is scaffolded at domain level and can be upgraded to ASP.NET Identity/Policy auth.

## API Endpoints
- `GET /api/assets`
- `POST /api/assets/register`
- `POST /api/assets/{id}/status`
- `POST /api/monitoring/rfid-scan`
- `GET /api/monitoring/events`
- `GET /api/monitoring/assets-report`
- `POST /api/integration/procurement-sync`
- `POST /api/integration/rfid-batch`
- `GET /api/integration/events`

### RBAC Example
- `POST /api/assets/register` with header `X-Role: Admin`
- `POST /api/monitoring/rfid-scan` with header `X-Role: Auditor`
