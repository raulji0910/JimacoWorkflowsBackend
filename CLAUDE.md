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

**V1 (manual entry)** still works exactly as below and is the only path actually wired into
production use today. **Phase 2 (`Jimaco.Aprobaciones.Sincronizador`)** now exists as a working
prototype — see its own section further down — but hasn't been deployed to the real World Office
server yet; treat it as built-and-tested-in-isolation, not live. World Office's schema exploration
(which tables/columns hold an OC, confirmed against 4 real OC PDFs) lives in
`ProyectosJimaco/WorkflowDocumentos/esquema-worldoffice-oc.md` on the user's machine — read that
before touching `Jimaco.Aprobaciones.Sincronizador`, don't re-derive the schema from scratch. Also
see the project memory `project_jimaco_workflow_documentos` for the full decision history
(including why direct SQL read access was chosen over World Office's Cloud API — this installation
is on-premise, the Cloud API path was never confirmed usable).

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
- `Jimaco.Aprobaciones.Sincronizador` — standalone console tool, **not part of the Docker stack, not
  deployed to the cloud**. Phase 2 of the World Office integration; see its own section below for
  why and how it deploys.
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

## Jimaco.Aprobaciones.Sincronizador (Phase 2 prototype)

**Why this is a separate console tool, not a background job inside the Api container:** the
World Office SQL Server lives on the hardware store's own local network (on-premise), which has
outbound internet access but is not reachable *from* the internet (no public IP, no port
forwarding) — and shouldn't be made reachable, that would mean exposing a SQL Server to the public
internet. So the connection has to run the other way: a small agent runs *inside* that local
network (on the World Office server itself, or any PC on that same LAN that can reach the SQL
instance) and pushes data *out* over a normal outbound HTTPS call to the already-public Jimaco
Aprobaciones API — the same direction any browser or Windows Update traffic already goes. No VPN,
no reverse tunnel, no inbound firewall rule needed. See
`ProyectosJimaco/WorkflowDocumentos/sincronizador-oc.html` for the diagram this was designed from.

**What it does, once per run** (designed to be invoked by a Windows Scheduled Task every few
minutes, not a long-running service — see `Program.cs`, it runs one pass and exits):
1. Reads the last processed `IdAsientoContable` from a local JSON file (`WatermarkStore` —
   `Sincronizador:RutaMarcaDeAgua` in config, defaults to `marca-de-agua.json` next to the exe).
2. Queries `[CuentasContables - Asientos]` (World Office's generic all-document-types table —
   see `esquema-worldoffice-oc.md`) for rows whose `prefijo` matches **any** `TipoDocumento` that
   has `PrefijoWorldOffice` configured (2026-09-09 — used to be hardcoded to `prefijo = 'OC'`, now
   fetched from `GET /api/tiposdocumento` at the start of each run, so adding a new synced document
   type is a pure admin-screen change, not a code change — see "Prefijo de World Office por tipo de
   documento" below), `senAnulado = 0`, and `IdAsientoContable` past the watermark — via
   `WorldOfficeReader`, using the read-only `wf_readonly` SQL login. This reading side never writes
   anything — see "Escritura de vuelta a World Office" below for the one place this project *does*
   write, which is a separate class with its own separate (write-capable) login, not this one.
3. For each new OC: resolves the proveedor and "elaborado por" (both are `Terceros` rows, joined
   by `IdTerceroExterno`/`IdTerceroInterno` respectively — `Terceros.NombreCompleto` handles the
   company-vs-person name shape), and sums `CCA_M_Inventarios.TotalRenglon` for the line-item
   total (World Office's document header has no total-value column of its own).
4. Calls the *same* `POST /api/documentos` the manual form uses (`JimacoAprobacionesClient`,
   reusing `Jimaco.Aprobaciones.Negocio`'s DTOs directly rather than redeclaring them — a service
   account logs in via the normal `/api/auth/login`, JWT cached and refreshed from its own `exp`
   claim, same as `documento-detalle.component.ts` does client-side). No special sync-only
   endpoint exists or should exist — this is a second *caller* of the ordinary creation path, not
   a privileged backdoor.
5. Saves the watermark **only after each OC's `CrearAsync` call succeeds** — if one fails, the
   batch stops right there (doesn't skip it, doesn't keep going past it) so the next run retries
   from the same point. Deliberately simple; revisit if OC volume ever makes "skip the bad one and
   keep going" worth the complexity.

**Config** (`appsettings.json`, checked in with placeholder values — real secrets go in
`appsettings.Local.json`, gitignored, layered on top): `WorldOffice:ConnectionString` (SQL auth as
`wf_readonly`, `TrustServerCertificate=True` since this is a local-network named instance, not a
publicly-trusted cert), `Jimaco:ApiBaseUrl` (now that production exists —
`https://aprobaciones.54-232-227-230.sslip.io/` — point it there, not at `localhost`, once actually
installing this on a machine in World Office's network), `Jimaco:UsuarioServicio:Email`/`Password`
(a normal `Usuario` row, created from the Usuarios admin screen like any other — no roles needed,
it never approves anything, only creates — remember this has to be created against *whichever*
environment's database `ApiBaseUrl` points at). No `TipoDocumentoOrdenCompraId` config anymore
(removed 2026-09-09) — which `TipoDocumento` each World Office prefix maps to now comes from
`GET /api/tiposdocumento` at runtime, see "Prefijo de World Office por tipo de documento" below.
`WorldOffice:ConnectionStringEscritura` is optional — see "Escritura de vuelta a World Office"
below.

**Deploying it** (this runs on a machine that is not a dev box and may not have the .NET runtime):
```bash
dotnet publish Jimaco.Aprobaciones.Sincronizador -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/sincronizador
```
Copy the `publish/sincronizador` folder to the target machine, edit `appsettings.Local.json` there
with real values (never paste real credentials into a chat/PR — same rule as the `.env` SMTP
password), then create a Windows Scheduled Task: Action = start
`Jimaco.Aprobaciones.Sincronizador.exe`, "Start in" = that folder (needed so it finds
`appsettings.json` and can write `marca-de-agua.json` next to itself), trigger = repeat every few
minutes, "run whether user is logged on or not" so it survives nobody being logged into that
server. The account running the task needs write access to that folder (for the watermark file).

**Verified so far:** `JimacoAprobacionesClient` (login, create, token reuse across calls) tested
against the real local API — works. `WorldOfficeReader`'s queries were validated interactively in
SSMS against the real World Office database (see `esquema-worldoffice-oc.md`) but the reader class
itself has **not** been run end-to-end against that database yet — this dev machine can't reach
it, only a machine on that LAN can. Don't claim this has been fully tested until someone runs it
from inside that network.

## Prefijo de World Office por tipo de documento (2026-09-09)

`TipoDocumento.PrefijoWorldOffice` (string, **obligatorio** a nivel de DTO/formulario, columna
nullable a nivel de DB por simplicidad de migración) — el prefijo exacto de WO
(`[CuentasContables - Asientos].prefijo`) que corresponde a ese tipo de documento acá. Motivado por
el usuario: una OC real (`prefijo='OC'`) y una OC de pruebas con su propio prefijo en WO son
documentos distintos aunque se llamen parecido — antes de esto, el Sincronizador tenía `'OC'`
hardcodeado en el SQL y un `TipoDocumentoOrdenCompraId` fijo en su config; ahora consulta
`GET /api/tiposdocumento`, arma un diccionario prefijo→TipoDocumentoId con los que tengan
`PrefijoWorldOffice` seteado y estén `Activo`, y consulta WO por todos esos prefijos en un solo
`WHERE prefijo IN (...)`. **Validación de unicidad**: `TipoDocumentoService` rechaza crear/editar
un tipo con un prefijo que ya use otro (ambiguo para el Sincronizador, no sabría a cuál de los dos
crear el documento). Migración `AgregarPrefijoWorldOffice` — la fila existente ("Orden de Compra")
quedó con `'OC'` de respaldo, que es su valor real, no un placeholder al azar.

**Ojo con la marca de agua al agregar un prefijo nuevo más adelante**: es global (un solo
`IdAsientoContable` "hasta acá ya se revisó"), así que documentos viejos de un prefijo recién
agregado, anteriores a la marca actual, no se traen solos — hace falta un backfill puntual la
primera vez (bajar la marca de agua a mano, o un query aparte para ese prefijo).

## Escritura de vuelta a World Office (2026-09-09) — la ÚNICA escritura automática a WO de todo este proyecto

Hasta acá, todo lo que este proyecto hacía contra World Office era de solo lectura — regla
explícita, repetida varias veces. Esto la rompe a propósito, por pedido directo del usuario:
**cuando se aprueba el primer paso (`Orden == 1`) de CUALQUIER flujo** (genérico, no específico de
"Orden de Compra" — el mecanismo de WO es el mismo para todos los prefijos, ver
`esquema-worldoffice-oc.md` y la limpieza masiva que se hizo en memoria `project_jimaco_workflow_documentos`),
**si el documento tiene un origen de WO guardado, se marca como "pendiente de escribir en WO"** y
el Sincronizador la refleja allá en su próxima corrida.

**Piezas nuevas:**
- `InstanciaDocumento.IdAsientoContableOrigen`/`PrefijoOrigen` — el `IdAsientoContable` (PK real en
  WO) + `prefijo` de la fila de origen. Null en documentos creados a mano; el Sincronizador los pasa
  al crear (`CrearInstanciaDocumentoDto`). Se guardan los dos juntos a propósito — el `prefijo` es
  un chequeo de seguridad extra al escribir (nunca alcanza con el Id solo como excusa para no
  validar nada más).
- `InstanciaDocumento.PendienteEscrituraWO` (bool) — se prende en `InstanciaDocumentoService.EjecutarAccionAsync`,
  caso `Aprobado`, cuando `paso.Orden == 1 && IdAsientoContableOrigen != null`. **No se revierte**
  si un paso posterior rechaza/devuelve el documento (decisión explícita del usuario — esa
  aprobación comercial ya fue real, lo que pase después es control interno nuestro).
- `Usuario.UsuarioWO` (string, nullable) — el usuario de World Office de esa persona (campo nuevo
  en el form de Usuarios). Se usa como `IdTerceroAprobador` al escribir — así queda a nombre del
  usuario real de WO de quien aprobó en Jimaco Aprobaciones, no de un texto genérico. Si la persona
  no tiene `UsuarioWO` configurado, se usa el respaldo `"JIMACO APROBACIONES"`.
- `SincronizacionController` (`GET /api/sincronizacion/pendientes-wo`, `POST .../{id}/confirmar`,
  `POST .../{id}/conflicto`) — solo los usa el Sincronizador, no la UI. `ListarPendientesEscrituraWOAsync`
  resuelve el `UsuarioWO` a usar buscando en el `Historial` quién aprobó el paso de `Orden == 1`.
- `Jimaco.Aprobaciones.Sincronizador/WorldOfficeWriter.cs` — clase separada de `WorldOfficeReader`,
  con su **propio login de solo-UPDATE** (`wf_aprobaciones_writer`, ver
  `exploracion-worldoffice.sql` Pasos 8-10 — `GRANT UPDATE` de columna puntual sobre
  `senAprobado`/`IdTerceroAprobador` nomás, nunca `db_datawriter` sobre toda la tabla). Antes de
  escribir, siempre relee el estado actual (`senAprobado`/`senAnulado`) — si ya está aprobado, no
  hace nada (idempotente, se confirma igual); si está anulado, no escribe nada y se reporta como
  conflicto (visible en el detalle del documento, `ConflictoWO`) en vez de reintentarlo para
  siempre. Concurrencia real cubierta: si alguien aprobó/anuló por Access mientras el documento
  seguía pendiente acá, el Sincronizador lo detecta en su próxima corrida — **no hay forma de
  detectarlo en el momento exacto del clic en Jimaco Aprobaciones** (no hay conexión en vivo hacia
  la red de WO desde la Api pública, solo el Sincronizador puede hablar con WO, y lo hace cada
  tanto, no al instante).
- **Opt-in por configuración**: si `WorldOffice:ConnectionStringEscritura` no está seteado, el
  Sincronizador se salta esta fase entera (sigue funcionando solo para lectura). No usar el mismo
  login que `WorldOffice:ConnectionString` (ese sigue siendo `wf_readonly`, de solo lectura).
- **Reintentos**: automáticos, sin mecanismo especial — cada corrida vuelve a pedir la cola de
  pendientes (`ListarPendientesEscrituraWOAsync`), así que cualquier falla puntual (WO caído en ese
  momento, error de red) simplemente se reintenta solo en la corrida siguiente. Un error en un
  documento puntual (a diferencia del lote de "OC nuevas") NO frena a los demás — son
  independientes.

**Todavía no verificado contra una WO real** (mismo estado que el resto del Sincronizador) —
probado end-to-end contra la Api local con datos simulados (`IdAsientoContableOrigen`/`PrefijoOrigen`
inventados), incluidos los tres casos (escribir bien, ya aprobado, conflicto anulado). Falta correr
`WorldOfficeWriter` de verdad desde una máquina con alcance a la red de World Office, con el login
`wf_aprobaciones_writer` ya creado ahí.

## Production deployment (live, 2026-09-04)

**https://aprobaciones.54-232-227-230.sslip.io** — same Lightsail box as Jimaco Cotizaciones and
Ferrealiados (`54.232.227.230`, São Paulo), by explicit user choice (cheaper than a new instance).
Repos cloned as siblings under `/opt/jimaco/Jimaco.Aprobaciones` + `Jimaco.Aprobaciones.Web`, same
as the other two. **Do not redeploy/restart things on this shared box without the user's explicit
go-ahead** (see `feedback_jimaco_deploy_workflow` in memory) — Jimaco Cotizaciones and Ferrealiados
are real production systems on it, not just neighbors.

**How it shares Caddy** (no `caddy` service of its own — that server's `jimaco-caddy` already owns
host ports 80/443): `docker-compose.prod.yml`'s `web` service joins the *external* network
`jimacocotizaciones_default` (the network Jimaco Cotizaciones' compose project created) in addition
to its own default network, so `jimaco-caddy` can reach it by container name. The corresponding
site block lives in `/opt/jimaco/Jimaco.Cotizaciones/Caddyfile` on the server (not in this repo —
it's a shared file edited by hand):
```
aprobaciones.54-232-227-230.sslip.io {
    reverse_proxy aprobaciones-web:8080
}
```
After editing that file, reload with `docker exec jimaco-caddy caddy reload --config /etc/caddy/Caddyfile`
— don't `docker compose restart caddy` from the Aprobaciones side, this repo doesn't own that container.

**Memory is genuinely tight on that box** (4GB/2vCPU Lightsail plan, ~635MB "available" measured
right after this deploy, down from ~1.2GB with just the other two apps running). `db`'s
`MSSQL_MEMORY_LIMIT_MB=768` (set in `docker-compose.prod.yml`) caps *our* SQL Server specifically —
the other two don't have a cap set, so if things get slow across all three apps under real load,
upgrading the Lightsail plan is the fix, not something to solve with more config here.

**Redeploy after pushing changes — NEVER `--build` on the server (2026-09-08).** That server is a
2vCPU/4GB Lightsail box already running Jimaco Cotizaciones + Ferrealiados live — building
.NET+Angular inside Docker *there* once starved it of RAM badly enough that the whole instance
OOM'd and rebooted (all three apps' containers restarted at once). Build locally, ship the image:
```bash
# Local (build once for both, or split if only one changed):
docker compose build api web
docker save jimacoaprobaciones-api:latest jimacoaprobaciones-web:latest | gzip > /tmp/aprobaciones-images.tar.gz
scp -i LightsailDefaultKey-sa-east-1.pem /tmp/aprobaciones-images.tar.gz ubuntu@54.232.227.230:/tmp/

# Server: pull code (for docker-compose.yml/CLAUDE.md/etc — the image itself doesn't need it),
# load the image, then start WITHOUT --build:
ssh -i LightsailDefaultKey-sa-east-1.pem ubuntu@54.232.227.230
cd /opt/jimaco/Jimaco.Aprobaciones && git pull
docker load -i /tmp/aprobaciones-images.tar.gz && rm /tmp/aprobaciones-images.tar.gz
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```
This likely applies to Jimaco Cotizaciones and Ferrealiados too (their own `CLAUDE.md`s still
document the old `--build`-on-server pattern that caused this) — worth fixing there too next time
either gets redeployed.

**Still pending on the server** (not blocking, but real gaps — don't assume these are done):
- `SMTP_PASSWORD` in the server's `.env` is still the placeholder — email notifications will fail
  to send in production until someone edits that file on the server with the real
  `sistemas@jimaco.com.co` mailbox password (same as the local `.env`, never pasted into chat/PRs).
- The seeded `admin@jimaco.local` / `Admin123!` account is live on a public HTTPS URL — change
  that password via the Usuarios screen before real use, same caveat as Jimaco Cotizaciones always
  had.
- No automated DB backups set up for this database yet, same as Jimaco Cotizaciones.
- `App__FrontendBaseUrl` in the server's `docker-compose.prod.yml` still needs the real domain
  (same placeholder pattern as `Cors__AllowedOrigins__0`) — until edited, the "aprobar/rechazar
  desde el correo" links in notification emails will point at the wrong host in production.

## Pending decisions (do not assume these have been resolved — check with the user)

- **World Office integration (Phase 2).** Prototype built (`Jimaco.Aprobaciones.Sincronizador`,
  see its own section above) using read-only direct SQL Server access — the Cloud API path was
  never confirmed usable for this on-premise license, so direct SQL read access is what got built.
  Not yet deployed to the real World Office server, and not yet pointed at a real (non-`localhost`)
  Jimaco Aprobaciones API, because that API isn't deployed anywhere public yet either — both are
  still pending the user's go-ahead to actually go live.
- **Notification provider — Email done, WhatsApp not.** `NotificacionService` + `SmtpEmailSender`
  send real email today (SMTP directly against the company's own mailbox, e.g.
  `sistemas@jimaco.com.co` via cPanel — see `Smtp:*` config) and it's been tested delivering to a
  real inbox. WhatsApp is still unbuilt — the `CanalNotificacion.WhatsApp` value on `Notificacion`
  exists but nothing dispatches it, and a provider decision (Meta Cloud API vs Twilio) hasn't been
  made. The WhatsApp infrastructure from a previous unrelated project
  (`project_prospeccion_constructoras`) was decommissioned and can't be reused as-is.
- **Aprobar/rechazar desde el correo (2026-09-07) — construido.** El correo de "documento
  pendiente" (`NotificacionService.NotificarPasoAsync`) incluye un link a
  `{App:FrontendBaseUrl}/accion-correo/{id}?token=...` que permite ver el PDF y aprobar/rechazar
  con comentario **sin loguearse**. El token lo genera `IJwtGenerador.GenerarTokenAccionCorreo`
  (JWT normal firmado con el mismo `Jwt:Secret`, pero con `purpose=correo-accion` + `doc={id}` y
  sin claims de Rol — los roles se re-consultan en BD igual que en un login real) y vence en
  `Jwt:VigenciaAccionCorreoDias` (7 por defecto). **Importante por seguridad**: el link NUNCA
  aprueba solo con abrirse (muchos clientes de correo/antivirus pre-visitan los links
  automáticamente) — hace falta un clic explícito en la página, que dispara el `POST` real. El
  alcance del token está acotado por `TokenCorreoActionFilter` (filtro global registrado en
  `Program.cs`): si el JWT trae `purpose=correo-accion`, solo puede pegarle a acciones marcadas con
  `[PermiteTokenCorreo]` (`Obtener`, `ObtenerPdf`, `EjecutarAccion` en `DocumentosController`) y
  únicamente si el `{id}` de la ruta coincide con el `doc` del token — cualquier otro endpoint, o el
  mismo endpoint con otro id, da 403 aunque el JWT sea válido. Verificado end-to-end con Playwright:
  ver sin login, aprobar de verdad (avanza el paso), y reutilizar el mismo link después de que el
  paso ya avanzó a un rol distinto (rechazado con 403 y mensaje claro, no aprueba silenciosamente).
  Config nueva: `App:FrontendBaseUrl` (en `docker-compose.yml`/`docker-compose.prod.yml`, este
  último con placeholder `CAMBIAR-DOMINIO-PRODUCCION` — **al desplegar en el servidor, poner el
  dominio real ahí igual que ya se hace con `Cors__AllowedOrigins__0`**, si no los links de los
  correos van a apuntar mal). Página del lado del frontend: `accion-correo.component.ts` (fuera del
  `authGuard`, ver `app.routes.ts`), usa un `HttpContextToken` (`TOKEN_CORREO` en
  `auth.interceptor.ts`) para que el interceptor use el token del link en vez de (o aunque exista)
  una sesión logueada en el mismo navegador.
- **`SMTP_HOST` real (2026-09-08) — no es `mail.jimaco.com.co`.** Ese hostname (el que cPanel
  recomienda en "Connect Devices" para `sistemas@jimaco.com.co`) **no tiene registro DNS** — nunca
  se creó el subdominio `mail` en la zona DNS real de `jimaco.com.co` (el MX del dominio apunta a
  Microsoft 365/Outlook, `jimaco-com-co.mail.protection.outlook.com`, pero el buzón en sí vive en
  cPanel/ColombiaHosting — dos cosas separadas). El host real que sí funciona (mismo servidor,
  detectado por PTR de la IP y confirmado enviando un correo real) es
  **`s3452.mex1.stableserver.net`** — el certificado SSL del servidor está a nombre de eso (y de
  `*.bom1.mysecurecloudhost.com`/`*.bom1.stableserver.net`/`*.bom1.whgi.net`), no de
  `mail.jimaco.com.co`, por eso conectar con ese hostname tira `SslHandshakeException` (nombre no
  coincide) — y antes de eso, directamente `SocketException` (NXDOMAIN) porque el hostname ni
  resuelve. **`Smtp:Host` local (`.env`) y de producción deben usar `s3452.mex1.stableserver.net`**,
  no `mail.jimaco.com.co`. Esto podría cambiar si algún día alguien agrega el registro DNS faltante
  del lado de Jimaco — si vuelve a fallar con `SocketException`/`SslHandshakeException`, repetir
  este diagnóstico (PTR de la IP del dominio) en vez de asumir que es igual que antes.
- **"Reenviar notificación" (2026-09-07) — construido.** Botón/endpoint (`POST
  /api/documentos/{id}/reenviar-notificacion`) para disparar de nuevo, a demanda, la notificación
  del paso actual sin tocar el documento — para cuando el envío automático de `Crear`/`EjecutarAccion`
  falló (SMTP/DNS caído, etc.) o para controlar el momento exacto del envío. Solo lo puede pedir
  quien emitió el documento (mismo chequeo que `ReenviarAsync`). A diferencia del envío automático
  (que nunca rompe la acción que lo disparó, ver `NotificarSinRomperAsync`), acá **si importa que
  quien lo pidió se entere** — devuelve `{ enviadas, fallidas }` leyendo las filas de `Notificacion`
  recién creadas, en vez de tragarse el resultado en silencio.
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
