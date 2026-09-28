# MediaRelay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver MediaRelay's Discord `/upload`, browser and ShareX upload flows, public MinIO media hosting, and tested Docker/VPS delivery in a public GitHub repository.

**Architecture:** One .NET 10 ASP.NET Core Minimal API process hosts Discord.Net as a background worker, upload APIs, and static UI. An `IMediaStorage` adapter isolates MinIO; browser uploads use short-lived presigned requests, ShareX streams through the API, and in-memory sessions coordinate Discord publication. Docker Compose runs the application and persistent MinIO; GitHub Actions tests, builds, publishes to GHCR, and deploys the exact image to a VPS over SSH.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, Discord.Net 3.20.1, official MinIO .NET SDK, `IMemoryCache`, static HTML/CSS/vanilla JavaScript, xUnit, Docker Compose, GitHub Actions, GHCR, `gh` CLI.

**Spec:** `docs/superpowers/specs/2026-09-28-media-relay-design.md`

## Global Constraints

- Runtime: .NET 10.
- Discord.Net: 3.20.1 with Discord.Net.Interactions and Discord.Net.WebSocket.
- Supported media: `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`, `.mp4` with their exact allowed MIME pair from spec section 17.
- Maximum upload size defaults to `536870912` bytes (512 MiB); session TTL defaults to 30 minutes; presigned upload TTL defaults to 15 minutes.
- Bucket defaults to `media`; canonical media host is `media.hugodotnet.dev`; application host is `upload.hugodotnet.dev`.
- Browser media bytes go directly to MinIO; ShareX media bytes stream through the API.
- No database, Redis, queue, frontend framework/build, transcoding, or multi-cloud abstraction.
- Session tokens, Discord token, MinIO credentials, ShareX key, and presigned URL query credentials must not be logged, committed, returned to unauthorized clients, or included in images.
- Anonymous storage access is read-only; browser CORS allows only the configured application origin and required methods/headers.
- Public GitHub repository: `vitorhugo-dotnet/MediaRelay`; description: `Self-hosted Discord and ShareX media uploader backed by MinIO.`
- Suggested GitHub topics: `aspnet-core`, `csharp`, `discord-bot`, `docker`, `dotnet`, `minio`, `media-uploader`, `sharex`.
- Do not publish/deploy from pull requests. Only deploy the SHA-tagged image produced from `main`.

## Review Focus

- Extension/MIME/signature disagreement or truncated file headers must be rejected; pin in Task 3 validator tests.
- Expired, reused, concurrently completed, or invalid upload-session tokens must not authorize another object or message; pin in Task 4 session/API tests.
- Presigned object metadata/size mismatch and files at/over the configured size boundary must fail completion cleanly; pin in Tasks 5–6 tests.
- Discord publication failures after storage must preserve the object and permit retry without a duplicate known-successful post; pin in Task 7 tests.
- Malicious/incorrect ShareX form fields, missing API key, and oversized streamed bodies must fail without exposing secrets; pin in Task 8 API tests.

## File Map

- `src/MediaRelay/Program.cs`: configuration, dependency registration, route mapping, hosted services.
- `src/MediaRelay/Options/`: strongly typed, validated Discord, MinIO, public URL, and upload options.
- `src/MediaRelay/Discord/`: Gateway worker, interaction setup, upload slash command, and publication interface.
- `src/MediaRelay/Uploads/`: media rules, upload sessions, orchestration, and browser upload endpoints.
- `src/MediaRelay/Storage/`: `IMediaStorage` and the MinIO implementation.
- `src/MediaRelay/ShareX/`: authenticated ShareX endpoint and response contract.
- `src/MediaRelay/Health/`: liveness endpoint.
- `src/MediaRelay/wwwroot/`: static upload page, styles, and browser behavior.
- `tests/MediaRelay.Tests/`: unit and API tests using fakes; MinIO integration tests use Testcontainers.
- `MediaRelay.sln`: application and test solution.
- `Dockerfile`, `.dockerignore`, `deploy/docker-compose.prod.yml`, `deploy/deploy.sh`: image and VPS deployment.
- `.github/workflows/vps-ci-cd.yml`: PR validation, image publication, and gated VPS deploy.
- `sharex/MediaRelay.sxcu`: importable ShareX Custom Uploader configuration.
- `README.md`, `.env.example`, `.gitignore`: setup, configuration, and usage documentation without secrets.

## Tasks

### Task 1: Scaffold the solution and configuration contracts

**Files:**
- Create: `MediaRelay.sln`
- Create: `src/MediaRelay/MediaRelay.csproj`
- Create: `src/MediaRelay/Program.cs`
- Create: `src/MediaRelay/Options/DiscordOptions.cs`
- Create: `src/MediaRelay/Options/MinioOptions.cs`
- Create: `src/MediaRelay/Options/PublicUrlOptions.cs`
- Create: `src/MediaRelay/Options/UploadOptions.cs`
- Create: `tests/MediaRelay.Tests/MediaRelay.Tests.csproj`
- Create: `.gitignore`
- Create: `.env.example`

**Interfaces:**
- Produces: `DiscordOptions`, `MinioOptions`, `PublicUrlOptions`, and `UploadOptions` bound from configuration and validated at startup; `Program` binds and validates configuration; each feature task maps its endpoint extensions when those endpoints are created.

- [x] Create the .NET 10 web and test projects, add the solution, and add Discord.Net 3.20.1, its Interactions/WebSocket packages, MinIO SDK, and test dependencies at the spec-pinned versions.
- [x] Add startup option validation for required secrets/endpoints and defaults `media`, `536870912`, 30 minutes, and 15 minutes; unit-test valid defaults and missing required production values.
- [x] Add `.env.example` containing names and non-secret example values for every configuration key in spec section 26; ensure no actual credential is present.
- [x] Run `dotnet test MediaRelay.sln` and confirm the initial test project passes.
- [x] Commit as `chore: scaffold MediaRelay solution`.

### Task 2: Implement media validation and object identifiers

**Files:**
- Create: `src/MediaRelay/Uploads/MediaValidator.cs`
- Create: `src/MediaRelay/Uploads/ValidatedMedia.cs`
- Create: `src/MediaRelay/Uploads/ObjectIdGenerator.cs`
- Test: `tests/MediaRelay.Tests/Uploads/MediaValidatorTests.cs`
- Test: `tests/MediaRelay.Tests/Uploads/ObjectIdGeneratorTests.cs`

**Interfaces:**
- Produces: `ValidatedMedia Validate(string fileName, string declaredContentType, ReadOnlySpan<byte> header)`; `string ObjectIdGenerator.Create(string extension)`.

- [x] Add failing tests for every allowed extension/MIME pair, mismatched MIME/extension, unsupported types, common image/MP4 magic bytes, truncated/incorrect signatures, and generated IDs that do not contain original filenames.
- [x] Run `dotnet test tests/MediaRelay.Tests --filter FullyQualifiedName~MediaValidatorTests` and confirm the new cases fail before implementation.
- [x] Implement validation using an explicit extension-to-MIME map and signature checks for files that pass through the backend; browser completion will also validate stored MIME and size metadata.
- [x] Run validator and object ID tests; require all allowed fixtures to pass and all invalid pairs/signatures to be rejected.
- [x] Commit as `feat: validate media and generate object ids`.

### Task 3: Add upload session lifecycle

**Files:**
- Create: `src/MediaRelay/Uploads/UploadSession.cs`
- Create: `src/MediaRelay/Uploads/UploadSessionService.cs`
- Test: `tests/MediaRelay.Tests/Uploads/UploadSessionServiceTests.cs`

**Interfaces:**
- Produces: `Task<CreatedUploadSession> CreateAsync(ulong guildId, ulong channelId, ulong userId, CancellationToken ct)`; `Task<UploadSession?> FindAsync(string token, CancellationToken ct)`; one-time transition methods to prepare, complete, and mark Discord publication status.
- `CreatedUploadSession` returns the raw URL-safe token exactly once with non-secret metadata. `UploadSession` stores only the token hash, session id, guild/channel/user IDs, expiration, object id, validated media metadata, and publication status; never log the raw token.

- [ ] Test creation, cryptographically random URL-safe bearer tokens, token hashing, 30-minute expiry default, rejection after expiration, and token-bearing route compatibility.
- [ ] Test concurrency: only one upload preparation/claim succeeds for a session, repeat completion is idempotent, and invalid state transitions fail.
- [ ] Run the focused session test filter and verify failure before implementation.
- [ ] Implement the state machine over `IMemoryCache` with expiration and atomic per-session transitions; keep the original channel and user metadata for publication/audit fields.
- [ ] Run focused session tests and verify all race/idempotency assertions pass.
- [ ] Commit as `feat: add expiring upload sessions`.

### Task 4: Isolate MinIO storage operations

**Files:**
- Create: `src/MediaRelay/Storage/IMediaStorage.cs`
- Create: `src/MediaRelay/Storage/MinioMediaStorage.cs`
- Test: `tests/MediaRelay.Tests/Storage/MinioMediaStorageTests.cs`
- Modify: `tests/MediaRelay.Tests/MediaRelay.Tests.csproj`

**Interfaces:**
- Produces: `Task<PresignedUpload> CreateBrowserUploadAsync(string objectId, string contentType, long maxSize, TimeSpan ttl, CancellationToken ct)`; `Task UploadAsync(string objectId, Stream content, string contentType, CancellationToken ct)`; `Task<StoredObjectInfo?> StatAsync(string objectId, CancellationToken ct)`; `string GetPublicUrl(string objectId)`.
- `PresignedUpload` contains only the one-object upload URL and required request headers/fields; storage abstraction has no provider-selection logic.

- [ ] Use a local MinIO Testcontainer to test bucket initialization, upload/stat round-trip, saved Content-Type, generated public URL, presigned upload constraints, and missing-object result.
- [ ] Run focused storage tests and confirm they fail before the adapter exists.
- [ ] Implement MinIO bucket initialization with anonymous read-only policy; never grant anonymous write/list/delete. Configure presign TTL and exact object metadata/size conditions supported by the S3-compatible request.
- [ ] Ensure public URLs are built only from `PUBLIC_MEDIA_BASE_URL` and the generated object id; test that internal bucket and MinIO endpoint are not included.
- [ ] Run the focused Testcontainers storage tests and confirm all pass.
- [ ] Commit as `feat: add MinIO media storage`.

### Task 5: Build browser upload API and health endpoint

**Files:**
- Create: `src/MediaRelay/Uploads/UploadEndpoints.cs`
- Create: `src/MediaRelay/Uploads/UploadService.cs`
- Create: `src/MediaRelay/Health/HealthEndpoints.cs`
- Modify: `src/MediaRelay/Program.cs`
- Test: `tests/MediaRelay.Tests/Uploads/UploadEndpointsTests.cs`
- Test: `tests/MediaRelay.Tests/Health/HealthEndpointTests.cs`

**Interfaces:**
- Produces routes `POST /api/uploads/prepare`, `POST /api/uploads/complete`, and `GET /health`.
- `POST /api/uploads/prepare` accepts session token, filename, declared MIME, and size; returns a generated object id and presigned authorization.
- `POST /api/uploads/complete` accepts the upload session token and returns stored URL plus publication state after verifying object metadata.

- [ ] Add API tests for missing/invalid/expired token, unsupported file, oversized size, unavailable MinIO, missing object, metadata mismatch, successful prepare/complete, and duplicate completion.
- [ ] Add health test asserting `GET /health` returns HTTP 200 and `{"status":"healthy"}` without credentials or internal endpoint details.
- [ ] Run focused API tests to confirm they fail before route mapping.
- [ ] Implement prepare/complete using the session, validator, and `IMediaStorage`; enforce one-time state transitions, size ceiling, stored size and Content-Type verification, ProblemDetails errors, and safe logging fields.
- [ ] Map health liveness independently of optional MinIO detail; ensure production exception middleware never returns stack traces.
- [ ] Run focused API and health tests; assert completion never repeats a publication action after successful completion.
- [ ] Commit as `feat: add browser upload API and health endpoint`.

### Task 6: Implement Discord Gateway and publication

**Files:**
- Create: `src/MediaRelay/Discord/DiscordBotWorker.cs`
- Create: `src/MediaRelay/Discord/DiscordBotService.cs`
- Create: `src/MediaRelay/Discord/InteractionHandler.cs`
- Create: `src/MediaRelay/Discord/IMediaPublisher.cs`
- Create: `src/MediaRelay/Discord/DiscordMediaPublisher.cs`
- Create: `src/MediaRelay/Discord/Commands/UploadModule.cs`
- Modify: `src/MediaRelay/Uploads/UploadSessionService.cs`
- Modify: `src/MediaRelay/Uploads/UploadEndpoints.cs`
- Modify: `src/MediaRelay/Program.cs`
- Test: `tests/MediaRelay.Tests/Discord/UploadModuleTests.cs`
- Test: `tests/MediaRelay.Tests/Discord/DiscordMediaPublisherTests.cs`

**Interfaces:**
- Produces: `Task<bool> IMediaPublisher.PublishAsync(ulong guildId, ulong channelId, string publicUrl, CancellationToken ct)`.
- `/upload` validates allowed guild, creates a session, and returns an ephemeral message with an uploader button; it performs no storage operation before responding.

- [ ] Test command response is ephemeral, points to `/u/{token}`, rejects a disallowed guild, and creates session with the invoking guild/channel/user IDs.
- [ ] Test publisher targets the original channel and posts one canonical URL; fake Discord client failure returns a failed result without deleting stored media.
- [ ] Test retry after a failed publication can succeed and retry after known success cannot create a duplicate post.
- [ ] Implement hosted Discord.Net lifecycle with clean login/start/stop/disposal, interaction command registration, development guild scope, production global or configured guild scope, and allowed guild filtering.
- [ ] Integrate browser completion with `IMediaPublisher`; preserve stored object and session state on Discord failure; log structured identifiers but never token, secret, or presigned URL.
- [ ] Run focused Discord and upload completion tests without making a real Discord connection.
- [ ] Commit as `feat: add Discord upload command and publication`.

### Task 7: Add the static browser uploader

**Files:**
- Create: `src/MediaRelay/wwwroot/upload.html`
- Create: `src/MediaRelay/wwwroot/upload.css`
- Create: `src/MediaRelay/wwwroot/upload.js`
- Create: `src/MediaRelay/Uploads/UploadPageEndpoints.cs`
- Modify: `src/MediaRelay/Program.cs`
- Test: `tests/MediaRelay.Tests/Uploads/UploadPageEndpointTests.cs`

**Interfaces:**
- Produces `GET /u/{token}` and serves only static application assets; the browser calls the prepare/complete APIs and uploads media bytes directly to the presigned MinIO URL.

- [ ] Test page routing, expired/invalid sessions, static asset availability, and no secret values embedded in HTML/JavaScript.
- [ ] Implement file picker, drag/drop, supported format/size precheck, visible upload progress, human-readable API errors, successful public URL display/copy, and mobile-friendly plain HTML/CSS/JavaScript.
- [ ] Use the exact signed headers/fields returned by prepare; complete only after MinIO upload succeeds. Do not send file bytes to ASP.NET Core.
- [ ] Configure storage CORS to only accept configured `PUBLIC_APP_BASE_URL` origin and required methods/headers; exercise a local browser-to-MinIO upload in the integration verification.
- [ ] Run endpoint and browser-to-MinIO verification and confirm file bytes bypass the application container.
- [ ] Commit as `feat: add browser upload page`.

### Task 8: Add authenticated ShareX upload

**Files:**
- Create: `src/MediaRelay/ShareX/ShareXEndpoints.cs`
- Create: `src/MediaRelay/ShareX/ShareXUploadRequest.cs`
- Create: `sharex/MediaRelay.sxcu`
- Modify: `src/MediaRelay/Program.cs`
- Test: `tests/MediaRelay.Tests/ShareX/ShareXEndpointTests.cs`

**Interfaces:**
- Produces `POST /api/sharex/upload`, authenticated by constant-time comparison of configured `UPLOAD_API_KEY`; success returns JSON with canonical public URL.

- [ ] Test missing/wrong key, invalid extension/MIME/signature, oversized streaming request, correct MinIO upload metadata, and response shape expected by the `.sxcu` configuration.
- [ ] Implement bounded streaming with request-size enforcement before buffering, magic-byte validation, generated object key, structured safe logs, and clear ProblemDetails errors.
- [ ] Add importable `.sxcu` file configured to send the API key using a user-supplied ShareX field/environment setup; never write a real key into the file.
- [ ] Run focused ShareX API tests using fake storage and verify no key appears in logs or responses.
- [ ] Commit as `feat: add ShareX custom uploader`.

### Task 9: Package and run the production stack

**Files:**
- Create: `Dockerfile`
- Create: `.dockerignore`
- Create: `deploy/docker-compose.prod.yml`
- Create: `deploy/deploy.sh`
- Modify: `README.md`
- Test: `tests/MediaRelay.Tests/Deployment/ComposeConfigurationTests.cs`

**Interfaces:**
- Produces an application container exposing only the ASP.NET Core port, a MinIO service with persistent volume, production environment configuration, and an idempotent VPS deploy script consuming `IMAGE`/Compose environment values.

- [ ] Test Compose configuration parses, MinIO data uses a named persistent volume, MinIO console has no public port, and app health check calls `/health`.
- [ ] Build a multi-stage .NET 10 Docker image; exclude `.git`, test results, local secrets, and developer files; do not pass secrets as build args.
- [ ] Implement deployment script to require a SHA-tagged `IMAGE`, check required host-side env/secrets, pull and restart services idempotently, and never print credentials.
- [ ] Add README local run, MinIO setup, Discord app/bot configuration, ShareX import, production DNS/reverse proxy routing, and GitHub deployment secret/environment instructions.
- [ ] Run Compose validation and `docker build`; start local stack and verify health and persistent MinIO data across app recreation.
- [ ] Commit as `build: package MediaRelay for Docker deployment`.

### Task 10: Add CI, image publication, and VPS deployment workflow

**Files:**
- Create: `.github/workflows/vps-ci-cd.yml`
- Modify: `README.md`

**Interfaces:**
- Pull requests validate solution, tests, and Docker build with read-only repository permissions and no deploy/publish secrets.
- `main` publishes `ghcr.io/vitorhugo-dotnet/media-relay:sha-<commit>` and `:latest`, then deploys that exact SHA image through the protected `production` environment.
- Manual dispatch accepts a deploy boolean and an image tag. When deploy is true, require an immutable `sha-<commit>` tag under `ghcr.io/vitorhugo-dotnet/media-relay`; deploy that existing image without rebuilding or republishing it.

- [ ] Add workflow triggers for `main`, pull requests, and `workflow_dispatch`; separate build, test, publish, and deploy jobs with explicit minimal permissions and concurrency control.
- [ ] Build job resolves `MediaRelay.sln`, installs .NET 10, restores and builds Release; test job runs all test projects and uploads TRX artifacts even when tests fail.
- [ ] Pull request Docker build must not log into GHCR or deploy. Main publication logs into GHCR using `GITHUB_TOKEN`, attaches source/revision OCI labels, caches layers, and pushes SHA plus `latest` tags.
- [ ] Deploy job requires successful image publication on `main` or an explicit manual deployment with an immutable SHA image input; use protected `production` environment, SSH secrets, and clear preflight errors; transfer only Compose/deploy files and restart that exact image.
- [ ] Follow the job boundaries and GHCR/SSH delivery pattern in `vitorhugo-dotnet/dotnet_RelayControl`; omit EF/migration actions because the spec has no database.
- [ ] Validate workflow syntax and behavior with a PR-style run; verify it cannot publish/deploy on PR and that missing secrets fail before SSH.
- [ ] Commit as `ci: build publish and deploy MediaRelay`.

### Task 11: Create and publish the public GitHub repository

**Files:**
- Modify Git remote: `origin` for `vitorhugo-dotnet/MediaRelay`
- Publish: current `main` branch and repository contents

**Interfaces:**
- Produces public repository `https://github.com/vitorhugo-dotnet/MediaRelay` with the spec description/topics and committed, verified source.

- [ ] Confirm local `main` contains the completed implementation and all focused/unit/integration checks and Docker build pass; inspect `git status` for secrets and generated artifacts.
- [ ] Use `gh repo create vitorhugo-dotnet/MediaRelay --public --description "Self-hosted Discord and ShareX media uploader backed by MinIO." --source . --remote origin` only after confirming the repository does not already exist; if it exists, inspect it and stop before overwriting unrelated content.
- [ ] Set topics with `gh repo edit vitorhugo-dotnet/MediaRelay --add-topic ...` for `aspnet-core`, `csharp`, `discord-bot`, `docker`, `dotnet`, `minio`, `media-uploader`, and `sharex`.
- [ ] Push `main`, verify public repository URL/description/topics and Actions workflow visibility; configure GHCR package as public if required by GitHub defaults.
- [ ] Confirm required VPS/production environment secrets are listed in README; do not attempt a real VPS deployment unless the user has supplied those secrets/configuration.

## Self-Review Coverage

- Discord Gateway, `/upload`, allowed guilds, interaction timing, cleanup and publication recovery: Tasks 3, 5–6.
- Browser routes, UI, session security, direct MinIO upload, size and completion metadata verification: Tasks 3–5, 7.
- ShareX authentication, streaming, validation and `.sxcu`: Tasks 2 and 8.
- Storage URL, metadata, anonymous read-only policy, presigned upload, CORS and persistence: Tasks 4, 7, 9.
- Health, structured logging, ProblemDetails, secrets and Docker health: Tasks 1, 5, 6, 8–10.
- CI tests/build/image labels/tags, PR isolation, manual deployment, SSH to VPS and GH repository metadata: Tasks 9–11.
- No database/migration path, no alternative storage providers, no frontend build, and no deferred features are added.





