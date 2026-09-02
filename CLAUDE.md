# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## What this is

Jimaco Aprobaciones: a generic, parametrizable **document approval workflow engine** for the
hardware-store business (Jimaco). The pilot use case is the **Orden de Compra (OC)**: emitted →
approved by Gerente Comercial → registered by Asistente Contable → paid by Gerente General →
picked up/confirmed by Logística. But the engine itself has **no hardcoded knowledge of "OC"** —
document types, their fields, roles, and the sequence/rules of approval steps are all data
(`TipoDocumento`, `DefinicionFlujo`, `PasoFlujo`), configured through admin screens, not code.
Adding a new document type or changing a flow should never require a deploy.

**V1 scope (current):** documents are created manually in this system (a few key fields + a PDF
attachment uploaded by the emisor) — there is deliberately **no integration with World Office**
(the company's accounting ERP) yet. That's a planned Phase 2: World Office exposes a Cloud REST
API for Compras, but this installation looks like the on-premise/desktop edition (confirmed via
screenshot: RDP into a dedicated server, World Office 9.0.3 desktop client) — whether that API
applies to an on-premise license, or the fallback is read-only direct SQL Server access, is still
pending confirmation from World Office support. See the project memory
`project_jimaco_workflow_documentos` in the user's Claude memory for the full history of this
decision if you need it — don't re-derive it from scratch.

## Repo layout (two repos, one system — same pattern as Jimaco Cotizaciones)

This is the **backend** repo. The Angular frontend is meant to live in a sibling repo,
**`Jimaco.Aprobaciones.Web`** (not yet created as of this writing) — `docker-compose.yml` here
builds it from `../Jimaco.Aprobaciones.Web`, so clone them as siblings.

## Architecture

**Solution layout** (`Jimaco.Aprobaciones.slnx`) — simple N-tier, matching Jimaco Cotizaciones and
this org's other .NET projects:

- `Jimaco.Aprobaciones.Modelo` — EF Core entities (`Entidades/`), `AppDbContext`, migrations (`Migraciones/`).
- `Jimaco.Aprobaciones.Negocio` — business logic. `Servicios/` has the real implementations,
  `Interfaces/` the contracts, `DTOs/` the API-facing shapes. `ServiceCollectionExtensions.AddNegocio()` wires DI.
- `Jimaco.Aprobaciones.Api` — ASP.NET Core Web API. Controllers only call into Negocio services;
  business-rule violations are thrown as exceptions from Negocio and translated to HTTP status
  codes by `Api/Middleware/ExcepcionesMiddleware.cs` (`KeyNotFoundException`→404,
  `UnauthorizedAccessException`→403, `InvalidOperationException`→409) — don't add try/catch in
  controllers for these, the middleware already covers it.
- `Jimaco.Aprobaciones.TestUnitarios` — xUnit + Moq + EF Core InMemory. `InstanciaDocumentoServiceTests.cs`
  covers the workflow engine transitions (create → approve → advance/complete, return, resend,
  role-authorization checks) — this is the most important test file in the repo; extend it before
  touching `InstanciaDocumentoService`.

### The workflow engine (core domain model)

- **`Rol`** — NOT a fixed enum (unlike Jimaco Cotizaciones' `RolUsuario`). Roles are rows in a
  table, created/edited from the admin UI, because the whole point of this system is that new
  roles/steps don't require a code change. `Usuario`↔`Rol` is many-to-many via `UsuarioRol`
  (a user can hold several roles).
- **`TipoDocumento`** — a document type (e.g. "Orden de Compra"), with a list of
  `CampoTipoDocumento` (dynamic fields: Texto/Numero/Fecha/Adjunto/Seleccion) captured beyond the
  fixed fields every document has.
- **`DefinicionFlujo`** — an ordered sequence of `PasoFlujo` for one `TipoDocumento`. Only one
  definition should be `Activo` per document type at a time (enforced by
  `DefinicionFlujoService.CrearAsync`, which deactivates any previous active one — not a DB
  constraint, so historical inactive versions can coexist).
- **`PasoFlujo`** — one step: `Orden` (position), which `Rol`(s) can act on it
  (`PasoFlujoRoles` — **any** user holding any of those roles can act, no unanimity/quorum logic),
  and whether it allows `PermiteDevolver`/`PermiteRechazar`. **Approving always advances to the
  next `Orden` in the same flow** (or completes the document if there is none) — this is not
  configurable per step, only the step sequence itself is. **Returning ("Devolver") is
  configurable**: if `PasoDestinoDevolucionId` is set, the document goes back to that specific
  earlier step; if it's null (the default), the document goes to `EstadoInstanciaDocumento.Devuelto`
  with no current step, and the original emisor must call `ReenviarAsync` to push it back into the
  flow at the *first* step (not wherever it was returned from).
- **`InstanciaDocumento`** — one concrete document (e.g. OC #123). Fixed indexed fields
  (`NumeroReferencia`, `Proveedor`, `Valor`, `FechaDocumento`) exist for filtering/reporting without
  parsing JSON; `DatosJson` holds the values of that document type's dynamic `CampoTipoDocumento`s
  as a flat `Dictionary<string,string>` serialized with `System.Text.Json`. `Estado` is one of
  `EnProceso`/`Devuelto`/`Completado`/`Rechazado`.
- **`HistorialAccion`** — append-only audit trail (who, when, which step, which action, comment).
  Never update or delete rows here.
- **`Adjunto`** — files attached to a document (in V1: the PDF exported from World Office).
  Storage is behind `IAlmacenamientoArchivos` (implemented by `AlmacenamientoArchivosDisco`,
  local disk under `Almacenamiento:RutaAdjuntos`, a Docker volume in prod) specifically so the
  workflow engine has zero knowledge of *where* files live — swap the implementation later
  (e.g. S3) without touching `InstanciaDocumentoService`.
- **`Notificacion`** — a queue table for per-step notifications (Email/WhatsApp/EnApp). **Nothing
  actually sends these yet** — no provider is wired (see "Pending" below). Rows are created but
  never dispatched; treat this as a placeholder until a provider decision is made.

**`InstanciaDocumentoService`** is the engine itself (`CrearAsync`, `EjecutarAccionAsync`,
`ReenviarAsync`, `ListarPendientesAsync`, adjunto upload/download) — it has **no reference to any
concrete document type**; everything it does is driven by the `DefinicionFlujo`/`PasoFlujo` rows
loaded for whatever `InstanciaDocumento` it's handed. If you're tempted to add an `if (tipoDocumento
== "OC")` anywhere in this service, that's a sign the config model needs a new field instead.

**Auth & roles:** JWT via `AddAuthentication().AddJwtBearer()` (standard ASP.NET Core handler, same
choice as Jimaco Cotizaciones). Unlike that project's single `ClaimTypes.Role` claim, this JWT
carries **one `ClaimTypes.Role` claim per role the user holds** (`JwtGenerador.GenerarToken`) — ASP.NET
Core's `[Authorize(Roles = "X")]` already matches against any of several role claims, so
`[Authorize(Roles = "Admin")]` works unchanged. On first run with an empty `Usuarios` table,
`Program.cs` seeds the `Admin` role (`Rol.Id = AppDbContext.RolAdminId = 1`, via `HasData` in
`OnModelCreating`) and an `admin@jimaco.local` / `Admin123!` user holding it — change this
password before any real deployment, same caveat as Jimaco Cotizaciones.

## Commands

### Backend (.NET 10)
```bash
dotnet build                                                        # whole solution
dotnet test Jimaco.Aprobaciones.TestUnitarios                       # all tests
dotnet test Jimaco.Aprobaciones.TestUnitarios --filter "FullyQualifiedName~InstanciaDocumentoServiceTests"

# EF Core migrations (Modelo holds them, Api is the startup project since it has the DbContext registration)
dotnet ef migrations add <Nombre> --project Jimaco.Aprobaciones.Modelo --startup-project Jimaco.Aprobaciones.Api --output-dir Migraciones
```

### Docker (full stack, local — requires the frontend repo checked out as `../Jimaco.Aprobaciones.Web`)
```bash
cp .env.example .env                          # first time only, then fill in real values
docker compose up -d                          # db + api + web
docker compose build api web                  # rebuild after backend/frontend changes
docker compose logs api --tail 50             # api applies migrations + seeds admin on boot; check here first
```
Local dev ports are deliberately offset from Jimaco Cotizaciones' (`db` 1434 not 1433, `api` 8081
not 8080, `web` 4201 not 4200) so both stacks can run side by side on the same dev machine.

## Production deployment — NOT set up yet

No server exists for this project yet. When it's time: **do not deploy without the user's explicit
go-ahead** (see `feedback_jimaco_deploy_workflow` in memory — same rule applied to Jimaco
Cotizaciones). If it ends up on the *same* Lightsail box as Jimaco Cotizaciones, read the big
comment in `docker-compose.prod.yml` first — that server's existing `jimaco-caddy` container
already owns host ports 80/443 for Cotizaciones' domain, so this app needs a new site block added
to that *existing* Caddyfile rather than its own `caddy` service.

## Pending decisions (do not assume these have been resolved — check with the user)

- **World Office integration (Phase 2).** Not built. Two candidate approaches were identified but
  neither confirmed: (a) World Office Cloud's REST API (`Compras` module, JWT auth) — unconfirmed
  whether it's available to this on-premise-looking license; (b) read-only SQL Server access to
  the World Office database directly (a `wf_readonly` login was drafted, schema not yet explored —
  see `ProyectosJimaco/WorkflowDocumentos/exploracion-worldoffice.sql` in the user's OneDrive
  folder). Whichever is chosen, it should only need to *create* an `InstanciaDocumento` via the
  same `IInstanciaDocumentoService.CrearAsync` path the manual form uses — don't build a second
  parallel creation path.
- **Notification provider (Email/WhatsApp).** Not wired. `Notificacion` rows are created but never
  sent. WhatsApp specifically needs a provider decision (Meta Cloud API vs Twilio, etc.) — the
  WhatsApp infrastructure from a previous unrelated project (`project_prospeccion_constructoras`)
  was decommissioned and can't be reused as-is.
- **Visual flow designer.** Out of scope for V1 on purpose — flows are configured via
  CRUD-style admin screens (create role, create step, assign roles/actions per step), not a
  drag-and-drop designer. The data model already supports one being added later as a pure UI layer.

## Non-obvious gotchas

- **`DefinicionFlujoService.CrearAsync` does two `SaveChangesAsync` calls on purpose.** The input
  DTO (`PasoFlujoInputDto.PasoDestinoDevolucionOrden`) references a return-target step **by its
  `Orden`** within the same submitted flow, not by database Id — because those steps don't have
  Ids yet when the request arrives. The first save persists all steps (so they get Ids), then a
  second pass resolves `Orden → Id` and a second save writes `PasoDestinoDevolucionId`. If you
  change this method, keep that two-pass shape; collapsing it to one save will NRE or silently
  drop the return-target wiring.
- **`JsonStringEnumConverter` is registered globally** (`Program.cs`,
  `AddControllers().AddJsonOptions(...)`), exactly like Jimaco Cotizaciones — every enum in a DTO
  (`EstadoInstanciaDocumento`, `TipoAccion`, `TipoCampo`, `CanalNotificacion`, ...) serializes as
  its string name, and the frontend must send the string name back, not the numeric value.
- **Roles are data, not an enum** — resist the urge to add a `RolUsuario` enum "for convenience"
  anywhere; it would reintroduce the exact rigidity this system was built to avoid. If code needs
  to check for the seeded Admin role specifically, use `AppDbContext.RolAdminId`, not a hardcoded
  literal `1` or a name string comparison.
- **`Swashbuckle` is pinned to 7.2.0**, same reason as Jimaco Cotizaciones: 10.x pulls in
  `Microsoft.OpenApi` 2.x which reworked the security-scheme API used in `Program.cs`'s Swagger
  setup. Don't bump without rewriting that setup.
- **`Adjunto.RutaArchivo` is a relative path inside the configured storage root, never an absolute
  path or the user's original filename** — `AlmacenamientoArchivosDisco.GuardarAsync` renames to a
  GUID + original extension specifically to avoid path-traversal from a crafted filename. Don't
  "simplify" this to store the original filename as the path.
