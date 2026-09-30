# MediaRelay — Design Specification

**Date:** 2026-09-28  
**Status:** Draft for review  
**Target:** MVP  
**Runtime:** .NET 10  
**Primary integrations:** Discord, MinIO/S3-compatible storage, ShareX

## 1. Goal

Build a small self-hosted media sharing service that combines:

- a permanently connected Discord bot;
- a browser-based upload flow started from Discord;
- a ShareX Custom Uploader API;
- MinIO-backed public media hosting.

The primary result of every successful upload is a short, publicly accessible media URL such as:

```text
https://s3.hugojava.dev/x7Fk2.png
https://s3.hugojava.dev/a91Kd3.mp4
```

When a Discord upload finishes, the bot must automatically publish that URL into the Discord channel where `/upload` was invoked.

The service must remain intentionally small. It is a personal media uploader, not the first seed of Amazon S3 2: Electric Boogaloo.

---

## 2. Success Criteria

The MVP is successful when all of the following work:

1. The Discord bot stays online through the Discord Gateway.
2. A user executes `/upload`.
3. The bot responds ephemerally with an **Open uploader** button.
4. The button opens a browser upload page.
5. The user uploads a supported image or MP4.
6. The file is stored in MinIO.
7. The file becomes publicly accessible through `s3.hugojava.dev`.
8. The bot posts the resulting public URL into the original Discord channel.
9. Discord renders supported media inline when possible.
10. ShareX can upload independently through the API and receive the same kind of public URL.
11. Large browser uploads do not pass through ASP.NET Core.
12. No database, Redis, queue, CQRS or separate frontend deployment is required for the MVP.

---

## 3. Technology Stack

### Backend

- .NET 10
- ASP.NET Core Minimal API
- Discord.Net 3.20.1
- Discord.Net.Interactions
- Discord.Net.WebSocket
- Official MinIO .NET SDK
- ASP.NET Core `IMemoryCache`
- ASP.NET Core static files
- Built-in structured logging

### Frontend

- Static HTML
- CSS
- Vanilla JavaScript

No React, Angular, Vue or frontend build pipeline is required.

### Infrastructure

- Docker
- MinIO
- Reverse proxy
- HTTPS
- Public media domain: `s3.hugojava.dev`
- Public MinIO upload endpoint, if required for presigned uploads

---

## 4. High-Level Architecture

```text
                         Discord
                            │
                            │ Gateway WebSocket
                            ▼
                  ┌───────────────────────┐
                  │                       │
                  │    .NET 10 Process    │
                  │                       │
                  │  Discord.Net          │
                  │  Minimal API          │
                  │  Static uploader UI   │
                  │                       │
                  └──────┬─────────┬──────┘
                         │         │
             presign /   │         │ multipart
             validation  │         │ ShareX
                         │         │
                         ▼         ▼
                      ┌───────────────┐
                      │     MinIO     │
                      └───────┬───────┘
                              │
                              ▼
                 s3.hugojava.dev
```

The application is deployed as **one .NET process/container**.

Discord bot functionality is hosted inside the same process as the HTTP API.

MinIO remains a separate service.

---

## 5. Architectural Principles

### 5.1 One application

Do not split the MVP into separate Discord Bot, API and frontend applications.

The ASP.NET Core process owns:

- Discord Gateway connection;
- slash commands;
- upload session creation;
- MinIO orchestration;
- ShareX endpoint;
- upload completion;
- static uploader page;
- health checks.

### 5.2 MinIO serves media

ASP.NET Core must not proxy ordinary media downloads.

After upload:

```text
Browser / Discord
        ↓
s3.hugojava.dev
        ↓
MinIO
```

This keeps application memory, CPU and bandwidth usage low.

It also allows MinIO/reverse proxy to handle HTTP Range requests for MP4 playback and seeking.

### 5.3 Browser uploads go directly to object storage

Browser uploads must not use:

```text
Browser → ASP.NET → MinIO
```

for the media body.

The desired flow is:

```text
Browser
   ↓
request authorization
   ↓
ASP.NET Core
   ↓
presigned upload
   ↓
Browser → MinIO
```

The backend controls object identity and authorization while avoiding double bandwidth through the application.

### 5.4 ShareX may stream through the API

ShareX uses a normal authenticated multipart endpoint:

```text
ShareX
   ↓
ASP.NET Core
   ↓
MinIO
```

This flow is acceptable because ShareX integration benefits from a simple stable HTTP API and does not require the browser presigned flow.

---

## 6. Discord Bot

### 6.1 Connection model

Use:

```text
DiscordSocketClient
```

through Discord.Net.

The client must maintain a persistent Discord Gateway connection.

The bot must expose an online presence while connected.

Minimum Gateway intents should be enabled.

Initial target:

```text
GatewayIntents.Guilds
```

Do not enable privileged intents unless a future feature explicitly requires them.

In particular, MVP does not require:

```text
MessageContent
GuildMembers
GuildPresences
```

---

## 7. Discord Commands

### 7.1 `/upload`

Primary MVP command.

#### Behavior

User executes:

```text
/upload
```

The bot immediately creates an upload session and responds with an ephemeral message:

```text
📤 Upload de mídia

[ Abrir uploader ]
```

The response must be visible only to the invoking user.

The button opens:

```text
https://<application-domain>/u/{token}
```

Example:

```text
https://upload.hugodotnet.dev/u/P0uJl...
```

#### Session metadata

Each upload session stores:

```text
SessionId
TokenHash
DiscordUserId
DiscordGuildId
DiscordChannelId
CreatedAt
ExpiresAt
Status
ObjectId?
```

`Status`:

```text
Pending
Uploading
Completed
Expired
```

#### Expiration

Default session lifetime:

```text
30 minutes
```

Configurable through application settings.

The application must not depend on the original Discord interaction token for final publication.

Discord interaction tokens are short-lived, so upload completion must use the bot's authenticated Discord client to send the resulting URL to the original channel.

---

## 8. Upload Session Security

Upload session tokens must:

- use a cryptographically secure random generator;
- contain at least 256 bits of entropy;
- use URL-safe Base64 encoding;
- never use sequential identifiers;
- never expose Discord IDs as the authentication mechanism;
- be stored only as a SHA-256 hash in the session store;
- expire automatically;
- become unusable after successful completion.

The raw token must not be emitted into structured application logs.

A session may upload exactly one media object.

Repeated completion requests must be idempotent and must not produce duplicate Discord messages.

---

## 9. Session Persistence

For MVP:

```text
IMemoryCache
```

is sufficient.

This intentionally means:

- sessions disappear when the application restarts;
- an uploader page already open during restart may become invalid;
- completed media remains untouched in MinIO.

This tradeoff is accepted for the MVP.

Persistent sessions may later use SQLite if restart-survival becomes useful.

Redis is explicitly unnecessary.

---

## 10. Browser Upload Page

Route:

```text
GET /u/{token}
```

The page must provide:

- drag and drop;
- file picker;
- selected filename;
- selected file size;
- upload progress;
- clear error messages;
- success state;
- resulting public URL;
- copy URL button.

Supported file types:

### Images

```text
.png
.jpg
.jpeg
.gif
.webp
```

### Video

```text
.mp4
```

The browser performs client-side validation before requesting upload authorization.

Server-side validation remains authoritative.

---

## 11. Browser Upload Flow

### Step 1 — Open session

```text
GET /u/{token}
```

Server verifies that the token:

- exists;
- has not expired;
- has not completed.

The uploader HTML is returned.

### Step 2 — Prepare upload

Browser calls:

```http
POST /api/uploads/prepare
```

Request:

```json
{
  "token": "...",
  "fileName": "recording.mp4",
  "contentType": "video/mp4",
  "size": 52834123
}
```

The backend validates:

- session;
- extension;
- MIME type;
- declared size;
- maximum configured size.

The backend generates the final object ID itself.

Example:

```text
x7Fk2.mp4
```

Original filenames are never trusted as MinIO object names.

### Step 3 — Generate upload authorization

Preferred mechanism:

```text
MinIO Presigned POST policy
```

A presigned POST should be preferred over an unrestricted presigned PUT when it allows the required upload constraints to be expressed directly in policy.

The policy must bind at minimum:

- bucket;
- exact object key;
- expiration;
- expected content type.

Where supported, it should also enforce the configured maximum content length.

Fallback:

```text
PresignedPutObjectAsync
```

may be used if the POST policy proves unnecessarily complex, provided the uploaded object's final metadata and size are verified before completion.

Presigned authorization should expire after:

```text
15 minutes
```

### Step 4 — Direct upload

Browser uploads directly to MinIO.

```text
Browser → MinIO
```

ASP.NET Core does not receive the media bytes.

### Step 5 — Complete

Browser calls:

```http
POST /api/uploads/complete
```

Request:

```json
{
  "token": "...",
  "objectId": "x7Fk2.mp4"
}
```

The application performs a `StatObject` against MinIO and verifies:

- object exists;
- key belongs to this session;
- file size is valid;
- content type is allowed.

Only then is the upload considered complete.

---

## 12. Discord Publication

After successful completion, the backend retrieves the original:

```text
DiscordChannelId
```

and publishes:

```text
https://s3.hugojava.dev/x7Fk2.mp4
```

using the connected Discord bot.

The bot must not upload the media itself to Discord.

It posts only the public URL.

The URL must be sent as plain message content so Discord can attempt its normal inline media rendering.

After successful Discord publication:

```text
UploadSession.Status = Completed
```

The session cannot be reused.

---

## 13. ShareX Integration

Endpoint:

```http
POST /api/sharex/upload
```

Authentication:

```http
Authorization: Bearer <UPLOAD_API_KEY>
```

Content type:

```text
multipart/form-data
```

Expected field:

```text
file
```

Flow:

```text
ShareX
   ↓
POST /api/sharex/upload
   ↓
validate
   ↓
generate object ID
   ↓
stream to MinIO
   ↓
return public URL
```

Success response:

```json
{
  "url": "https://s3.hugojava.dev/x7Fk2.png"
}
```

The service must include a ready-to-import ShareX Custom Uploader configuration (`.sxcu`) in the repository.

ShareX uploads do **not** automatically publish to Discord in the MVP.

ShareX and Discord are independent entry points that share the same storage layer.

---

## 14. Object Naming

Object IDs must be generated by the backend.

Requirements:

- collision resistant;
- URL safe;
- case-sensitive alphabet permitted;
- original filename excluded;
- original file extension preserved only after server validation.

Preferred format:

```text
<random-id>.<validated-extension>
```

Example:

```text
x7Fk2.png
Kp39Za.mp4
```

Implementation may use a larger internal random value than shown in examples.

A minimum effective randomness of 64 bits is required.

Prefer 96 bits or more.

---

## 15. Media URLs

Canonical public URL format:

```text
https://s3.hugojava.dev/{objectId}
```

Examples:

```text
https://s3.hugojava.dev/x7Fk2.png
https://s3.hugojava.dev/Kp39Za.mp4
```

URLs must:

- require no authentication;
- support `GET`;
- support `HEAD`;
- preserve correct `Content-Type`;
- use `Content-Disposition: inline` where appropriate;
- support Range requests for MP4;
- not expose MinIO credentials;
- not expose internal bucket names in the canonical public URL.

---

## 16. Storage Layout

Single bucket for MVP:

```text
media
```

Example underlying object:

```text
media/x7Fk2.mp4
```

The public reverse proxy maps this internally to:

```text
https://s3.hugojava.dev/x7Fk2.mp4
```

Bucket policy permits anonymous read access to objects.

Anonymous write/list/delete access must not be allowed.

Uploads require either:

- backend MinIO credentials; or
- short-lived presigned authorization.

---

## 17. MIME and Extension Validation

Allowed pairs:

| Extension | MIME |
|---|---|
| `.png` | `image/png` |
| `.jpg` | `image/jpeg` |
| `.jpeg` | `image/jpeg` |
| `.gif` | `image/gif` |
| `.webp` | `image/webp` |
| `.mp4` | `video/mp4` |

Validation must not rely solely on the browser-provided MIME type.

At minimum, extension and declared MIME must match an allowed pair.

The implementation should inspect file signatures/magic bytes for files passing through the backend, particularly ShareX uploads.

For browser-to-MinIO uploads, completion must verify stored metadata and size.

Deep media decoding is out of scope.

---

## 18. Upload Limits

Configuration:

```text
MAX_UPLOAD_SIZE
```

Default:

```text
512 MiB
```

The same limit applies to Discord-browser and ShareX uploads.

The limit is configurable without recompilation.

Requests exceeding it must fail with a clear error.

---

## 19. API Endpoints

### Public UI

```text
GET /u/{token}
```

### Browser upload API

```text
POST /api/uploads/prepare
POST /api/uploads/complete
```

### ShareX

```text
POST /api/sharex/upload
```

### Health

```text
GET /health
```

No CRUD media API is required for MVP.

---

## 20. `/health`

Must report application liveness.

At minimum:

```json
{
  "status": "healthy"
}
```

A richer health response may include:

```text
Discord connection state
MinIO connectivity
```

but must not expose secrets or internal credentials.

Docker health checks should call this endpoint.

---

## 21. Internal Components

Recommended organization:

```text
src/
├── Program.cs
│
├── Discord/
│   ├── DiscordBotWorker.cs
│   ├── DiscordBotService.cs
│   ├── InteractionHandler.cs
│   └── Commands/
│       └── UploadModule.cs
│
├── Uploads/
│   ├── UploadSession.cs
│   ├── UploadSessionService.cs
│   ├── UploadService.cs
│   ├── MediaValidator.cs
│   └── UploadEndpoints.cs
│
├── Storage/
│   ├── IMediaStorage.cs
│   └── MinioMediaStorage.cs
│
├── ShareX/
│   └── ShareXEndpoints.cs
│
├── Health/
│   └── HealthEndpoints.cs
│
└── wwwroot/
    ├── upload.html
    ├── upload.css
    └── upload.js
```

Feature-oriented folders are preferred over architectural project proliferation.

Do not create separate projects named:

```text
Domain
Application
Infrastructure
Presentation
SharedKernel
```

The service does not have enough domain complexity to justify that architecture.

---

## 22. Core Interfaces

Storage should be abstracted behind one small application interface.

Conceptually:

```text
IMediaStorage
├── CreateBrowserUploadAsync(...)
├── UploadAsync(...)
├── StatAsync(...)
├── DeleteAsync(...)
└── GetPublicUrl(...)
```

The interface exists to isolate MinIO-specific behavior from Discord and ShareX flows.

It must not evolve into a generic multi-cloud storage framework.

Supporting AWS S3, Azure Blob or Cloudflare R2 is out of scope.

---

## 23. Discord Hosting Lifecycle

Discord.Net runs as an ASP.NET Core hosted background service.

Conceptual lifecycle:

```text
Application starts
      ↓
DiscordBotWorker starts
      ↓
LoginAsync
      ↓
StartAsync
      ↓
Gateway Ready
      ↓
Register/initialize interactions
      ↓
Remain connected
```

Application shutdown must:

- stop receiving new work;
- disconnect Discord cleanly;
- dispose resources through normal host shutdown.

Discord.Net owns Gateway reconnect/heartbeat protocol behavior.

Do not implement a custom Discord WebSocket client.

---

## 24. Slash Command Registration

Development:

```text
guild-scoped commands
```

This allows rapid command updates.

Production:

```text
global commands
```

or guild-scoped commands when the bot is intentionally restricted to specific servers.

Allowed guild IDs should be configurable.

Environment:

```text
DISCORD_ALLOWED_GUILD_IDS
```

Requests from unauthorized guilds must be rejected.

---

## 25. Discord Interaction Timing

The `/upload` command must perform only fast work before responding:

```text
validate guild
create session
build button
respond
```

No MinIO upload or heavy network operation occurs before the initial interaction response.

The final upload publication uses the persistent bot client rather than relying on the original interaction webhook.

---

## 26. Configuration

Environment variables / configuration:

```text
# Discord
DISCORD_TOKEN
DISCORD_APPLICATION_ID
DISCORD_ALLOWED_GUILD_IDS

# MinIO
MINIO_ENDPOINT
MINIO_PUBLIC_ENDPOINT
MINIO_BUCKET
MINIO_ACCESS_KEY
MINIO_SECRET_KEY
MINIO_USE_SSL

# Public URLs
PUBLIC_MEDIA_BASE_URL
PUBLIC_APP_BASE_URL

# Upload
UPLOAD_API_KEY
MAX_UPLOAD_SIZE
UPLOAD_SESSION_TTL_MINUTES
PRESIGNED_UPLOAD_TTL_MINUTES
```

Suggested defaults:

```text
MINIO_BUCKET=media
MAX_UPLOAD_SIZE=536870912
UPLOAD_SESSION_TTL_MINUTES=30
PRESIGNED_UPLOAD_TTL_MINUTES=15
```

Secrets must never be committed.

---

## 27. Discord Bot Token Security

`DISCORD_TOKEN` must:

- come from environment/secrets;
- never appear in logs;
- never appear in API responses;
- never reach browser JavaScript;
- never be embedded into Docker images.

The browser communicates only with the application's upload API.

---

## 28. MinIO Credentials

`MINIO_ACCESS_KEY` and `MINIO_SECRET_KEY` are server-only.

They must never be returned to clients.

Browser direct uploads receive only short-lived presigned authorization for one exact object.

Possession of an upload session token must not grant:

- bucket listing;
- deletion;
- arbitrary object upload;
- arbitrary object overwrite;
- MinIO administrative access.

---

## 29. Error Handling

API errors use `ProblemDetails`-style responses where appropriate.

Relevant error cases:

```text
invalid upload session
expired upload session
session already completed
unsupported extension
unsupported MIME type
file too large
presigned upload failure
object missing during completion
MinIO unavailable
Discord unavailable
Discord channel unavailable
invalid ShareX API key
```

Browser UI must convert API errors into human-readable messages.

Stack traces must never be returned in production.

---

## 30. Discord Failure After Successful Upload

If the MinIO upload succeeds but Discord publication fails, the media object must **not** be deleted.

The completion API should report that the file was successfully stored but publication failed.

The session should preserve enough state during its lifetime to retry publication without re-uploading.

Retry must not create duplicate messages after a known successful publication.

---

## 31. Logging

Use structured logging.

Useful fields:

```text
UploadSessionId
ObjectId
DiscordGuildId
DiscordChannelId
DiscordUserId
FileSize
ContentType
UploadSource
DurationMs
```

`UploadSource` values:

```text
DiscordBrowser
ShareX
```

Never log:

```text
Discord bot token
MinIO secret key
ShareX API key
raw upload session token
presigned URL query credentials/signature
```

---

## 32. Testing Strategy

### Unit tests

Cover:

- supported media validation;
- rejected MIME/extension combinations;
- object ID generation;
- upload-session expiration;
- one-time session semantics;
- public URL generation;
- maximum upload size;
- completion idempotency.

### Integration tests

Use a real MinIO container through Testcontainers.

Cover:

```text
upload object
stat object
correct Content-Type
public URL generation
presigned upload flow
object verification
```

### Discord tests

Do not require a real Discord connection for normal automated tests.

Wrap message publication behind a small interface so tests can verify:

```text
correct channel
correct URL
one publication
```

### API tests

Use ASP.NET Core integration testing for:

```text
/api/uploads/prepare
/api/uploads/complete
/api/sharex/upload
/health
```

---

## 33. Deployment

Docker deployment should contain:

```text
media-uploader
minio
reverse-proxy
```

The application container exposes only the HTTP application port.

MinIO management UI must not be publicly exposed without separate authentication.

Persistent MinIO data uses a Docker volume.

---

## 34. Public Networking

Recommended separation:

```text
upload.hugodotnet.dev
    → ASP.NET Core

s3.hugojava.dev
    → read-only MinIO media access

storage.hugodotnet.dev
    → MinIO S3 endpoint used for presigned browser uploads
```

`storage.hugodotnet.dev` may be replaced by another public S3 endpoint if infrastructure already provides one.

The canonical media URL remains:

```text
s3.hugojava.dev
```

regardless of the storage upload hostname.

---

## 35. CORS

The MinIO upload endpoint must accept browser upload requests only from the configured application origin.

Example allowed origin:

```text
https://upload.hugodotnet.dev
```

Do not configure:

```text
Access-Control-Allow-Origin: *
```

unless technically necessary and explicitly accepted.

Only required methods and headers should be allowed.

---

## 36. Discord Rich Preview

The MVP first attempts to use direct media URLs.

Example:

```text
https://s3.hugojava.dev/x7Fk2.mp4
```

Do not build Open Graph wrapper pages unless direct Discord rendering proves insufficient.

Optional post-MVP endpoints:

```text
GET /v/{id}
GET /i/{id}
```

may generate Open Graph metadata pointing to the underlying public object.

This is deliberately deferred until a real Discord preview issue exists.

---

## 37. Out of Scope for MVP

Explicitly excluded:

- user accounts;
- OAuth login;
- database;
- Redis;
- RabbitMQ;
- Kafka;
- MediatR;
- CQRS;
- full Clean Architecture;
- admin dashboard;
- React frontend;
- video transcoding;
- FFmpeg;
- HLS;
- DASH;
- media compression;
- thumbnail generation;
- multiple storage providers;
- CDN-specific integration;
- expiring public URLs;
- private media;
- media library UI;
- upload history;
- quotas;
- antivirus scanning;
- multiple application instances;
- Discord message commands;
- Discord native attachment ingestion;
- automatic ShareX-to-Discord posting.

---

## 38. Post-MVP Candidates

Possible future additions:

```text
/delete
/files
/recent
/stats
```

Additional possibilities:

- SQLite metadata persistence;
- deletion ownership;
- upload history;
- expiring links;
- configurable retention;
- image/video thumbnails;
- FFmpeg conversion;
- native Discord file-upload modal;
- ShareX option to automatically post to Discord;
- administrative web page;
- per-user quotas;
- multi-instance deployment.

Each should be justified by actual usage before implementation.

---

## 39. MVP User Flows

### Discord browser flow

```text
/user executes /upload
        ↓
Discord.Net creates UploadSession
        ↓
ephemeral message
        ↓
[ Abrir uploader ]
        ↓
browser opens /u/{token}
        ↓
user selects MP4
        ↓
POST /api/uploads/prepare
        ↓
presigned authorization returned
        ↓
browser uploads directly to MinIO
        ↓
POST /api/uploads/complete
        ↓
backend verifies MinIO object
        ↓
Discord.Net posts:
https://s3.hugojava.dev/x7Fk2.mp4
        ↓
session completed
```

### ShareX flow

```text
ShareX screenshot
        ↓
POST /api/sharex/upload
        ↓
API key validation
        ↓
media validation
        ↓
stream to MinIO
        ↓
https://s3.hugojava.dev/k91Ps.png
        ↓
ShareX copies URL
```

---

## 40. Acceptance Criteria

### Discord

- [ ] Bot connects through Discord Gateway.
- [ ] Bot appears online while connected.
- [ ] `/upload` exists.
- [ ] `/upload` returns an ephemeral response.
- [ ] Response contains a browser upload button.
- [ ] Button URL contains a secure temporary session token.
- [ ] Expired tokens are rejected.
- [ ] Completed tokens cannot upload again.

### Browser

- [ ] Upload page works without a frontend framework.
- [ ] Drag and drop works.
- [ ] File picker works.
- [ ] Upload progress is visible.
- [ ] Invalid media is rejected.
- [ ] Oversized media is rejected.
- [ ] Browser sends media directly to MinIO.
- [ ] Successful upload shows the resulting public URL.

### Storage

- [ ] Objects receive generated IDs.
- [ ] Original filenames are not used as object keys.
- [ ] Correct `Content-Type` is stored.
- [ ] Public media URLs work anonymously.
- [ ] MP4 URLs support HTTP Range.
- [ ] Anonymous bucket writes are impossible.

### Discord completion

- [ ] Successful browser upload causes the bot to post the public URL.
- [ ] URL is posted in the same channel that initiated `/upload`.
- [ ] Duplicate completion does not create duplicate messages.
- [ ] Discord failure does not delete successfully uploaded media.

### ShareX

- [ ] ShareX can authenticate using an API key.
- [ ] ShareX can upload supported media.
- [ ] ShareX receives JSON containing the public URL.
- [ ] Repository contains an importable `.sxcu`.
- [ ] ShareX and Discord produce the same canonical media URL format.

### Operations

- [ ] Application runs in Docker.
- [ ] `/health` works.
- [ ] MinIO survives container recreation through persistent volume.
- [ ] Discord/MinIO/API secrets are supplied externally.
- [ ] Logs contain no credentials or presigned URLs.

---

## 41. Design Decisions

### Minimal API instead of Controllers

The HTTP surface is small and endpoint-focused.

Controllers would add ceremony without solving an actual complexity problem.

### Discord.Net instead of raw Discord API

The bot intentionally maintains a persistent Gateway connection.

Discord.Net owns Gateway connection lifecycle, heartbeats, reconnects, event handling and interactions.

### One process instead of API + bot services

Both parts share:

```text
upload sessions
storage
configuration
logging
Discord publication
```

Splitting them would create deployment and communication complexity without useful isolation.

### Vanilla frontend instead of React

The uploader requires:

```text
file selection
drag/drop
progress
API calls
copy URL
```

A framework adds dependency and build complexity without meaningful benefit.

### MinIO direct browser upload

Avoids moving large MP4 files:

```text
Browser → API → MinIO
```

and instead uses:

```text
Browser → MinIO
```

after backend authorization.

### No database

All durable MVP state is the media itself.

Upload sessions are short-lived coordination state.

`IMemoryCache` is therefore enough.

### No Open Graph layer initially

Direct URLs are simpler.

Wrapper pages are introduced only if Discord rendering proves inadequate.

---

## 42. Final MVP Boundary

The MVP contains exactly three externally meaningful capabilities:

```text
1. Discord /upload → browser → MinIO → Discord URL
2. ShareX → API → MinIO → returned URL
3. Public media hosting through s3.hugojava.dev
```

Everything else exists only to support those flows.

If a proposed implementation component cannot be traced back to one of those three capabilities, it should probably not exist yet.

---

## 43. Repository and CI/CD

### Repository

Create the public GitHub repository:

```text
https://github.com/vitorhugo-dotnet/MediaRelay
```

Description:

```text
Self-hosted Discord and ShareX media uploader backed by MinIO.
```

Suggested topics:

```text
aspnet-core, csharp, discord-bot, docker, dotnet, minio, media-uploader, sharex
```

### Continuous integration

Use GitHub Actions modeled on `vitorhugo-dotnet/dotnet_RelayControl`:

- Run on pushes to `main`, pull requests, and manual dispatch.
- Restore and build the .NET solution in Release configuration.
- Discover and run test projects; upload test results as artifacts.
- Build a Docker image for pull request validation without publishing it.
- Publish a public GHCR image on pushes to `main`, tagged with the commit SHA and `latest`.
- Use GitHub Actions cache for Docker layers and attach OCI source/revision labels.
- Keep workflow permissions minimal; package write permission is limited to image publication.

### Deployment to VPS

- Deploy automatically after successful publication from `main`, with a manual dispatch option.
- Use a protected `production` GitHub Environment and repository/environment secrets for SSH host, user, key, port, and application directory.
- Transfer the production Compose file and deployment script, then pull and restart the SHA-tagged image over SSH.
- Do not include database migration steps; the MVP has no database.
- Keep MinIO data in a persistent Docker volume and keep application secrets on the VPS, outside the image and repository.
- Expose only the application and intended public media endpoints; do not publish the MinIO console.

Required deployment files are `Dockerfile`, production Compose configuration, and an idempotent deployment script. Exact VPS hostnames and credentials are deployment configuration and must not be committed.

### CI/CD acceptance criteria

- Pull requests restore, build, run the available tests, and validate the Docker build without publishing an image or deploying.
- A push to `main` publishes an immutable SHA-tagged GHCR image and the `latest` tag.
- A successful `main` publication deploys that exact SHA-tagged image to the VPS.
- Manual deployment can deploy the published image without rebuilding it.
- Missing deployment secrets fail with clear diagnostics before SSH operations.
- Workflow logs and artifacts contain no credentials or presigned upload URLs.
