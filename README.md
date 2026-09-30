# MediaRelay

MediaRelay is a self-hosted ASP.NET Core media uploader. It accepts browser uploads directly into MinIO, authenticated uploads from ShareX, and can publish uploads to Discord.
 
## Run locally

Requirements: .NET 10 SDK, Docker Engine, and Docker Compose.

The local Compose stack builds its MinIO Community image from the pinned upstream source commit `9e49d5e7a648f00e26f2246f4dc28e6b07f8c84a` (release `RELEASE.2025-10-15T17-29-55Z`), as does the CI test job. Community is distributed as source. The MinIO repository is archived, so this image does not receive upstream Community security updates. Review and update the pinned source deliberately if you maintain this deployment.

```sh
cp .env.example .env
# Replace example credentials and URLs in .env before starting the local stack.
docker compose -f docker-compose.yml up -d --build
```

The `.env.example` sets `COMPOSE_FILE=deploy/docker-compose.prod.yml`, so plain `docker compose up` selects the production app-only configuration without merging Compose files. The local command above explicitly selects `docker-compose.yml`, which starts the app and MinIO together and builds both images. For production, set `IMAGE` to an immutable `ghcr.io/vitorhugo-dotnet/media-relay:sha-<40-character-commit-sha>` tag and configure `MINIO_ENDPOINT` for the existing MinIO service.

The local app health endpoint is `http://localhost:8080/health`; the local MinIO S3 API is on `http://localhost:9000`. The MinIO console is not published to the host. Stop the local stack with `docker compose -f docker-compose.yml down`. The named `minio-data` volume remains when containers are recreated; `down -v` deletes it.

The compose stack binds the app and MinIO API to loopback. A reverse proxy on the VPS should route the public app host to `127.0.0.1:8080` and the media host to `127.0.0.1:9000`, with HTTPS enabled. Configure the `PUBLIC_APP_BASE_URL` and `PUBLIC_MEDIA_BASE_URL` values to those HTTPS origins. Set `MINIO_PUBLIC_ENDPOINT` to the public S3 hostname (without a scheme); it is used to construct browser presigned upload requests. `MINIO_USE_SSL` controls the app-to-MinIO internal connection, while `MINIO_PUBLIC_USE_SSL` controls the browser-facing MinIO endpoint used to sign those requests. Local HTTP endpoints should set both values to `false`; production can keep the internal value `false` while setting the public value to `true`. `PUBLIC_MEDIA_BASE_URL` is separate: it is the URL returned for the final uploaded media.

## Configuration

Copy `.env.example` and replace every placeholder with a unique value. Keep `.env` private and restrict its permissions. `UPLOAD_API_KEY` is the bearer token ShareX sends. `MINIO_ROOT_USER` and `MINIO_ROOT_PASSWORD` are MinIO administrator credentials: the current application initializes the bucket and its anonymous read-only object policy, so it needs those permissions. Do not reuse these credentials elsewhere.

`DISCORD_ENABLED` defaults to `true`. Set it to `false` to run without Discord credentials. When enabled, configure `DISCORD_TOKEN` and `DISCORD_APPLICATION_ID`; `DISCORD_ALLOWED_GUILD_IDS` can optionally restrict accepted guilds. If left empty, the bot accepts `/upload` from any guild where the command is available. The Discord bot needs the `bot` and `applications.commands` OAuth scopes and permission to view channels, send messages, and read message history. On uncertain publication retries, MediaRelay checks the latest 50 messages in the source channel for an already-sent URL; read history is required for this duplicate reconciliation.

The Compose configuration sets MinIO's global `MINIO_API_CORS_ALLOW_ORIGIN` to `PUBLIC_APP_BASE_URL`, allowing the browser uploader origin. MinIO exposes this global origin control; it does not provide the per-bucket method and header restriction described by an ideal least-privilege CORS policy. The bucket policy remains limited to anonymous `GetObject` access.

To run the storage integration tests locally, build the same pinned image first: `docker build -f deploy/minio-community.Dockerfile -t media-relay-minio:community-9e49d5e .`, then run `dotnet test tests/MediaRelay.Tests/MediaRelay.Tests.csproj --filter FullyQualifiedName~MinioMediaStorageTests`. The tests use Docker to start the local image.

## ShareX

Import [`sharex/MediaRelay.sxcu`](sharex/MediaRelay.sxcu) into ShareX, then enter the same `UPLOAD_API_KEY` when prompted. The default upload URL in that file is `https://upload.hugodotnet.dev/api/sharex/upload`; change it to match your deployment's app hostname if needed.

## Production deployment

Create DNS records for the app and media hosts and configure the VPS reverse proxy as described above. Keep the MinIO console private. The proxy must preserve the URL path and forward requests to the corresponding app or existing MinIO endpoint. Allow uploads up to `MAX_UPLOAD_SIZE` in any proxy body-size limit.

The deployment workflow uses the protected GitHub Actions environment named `production`. Configure these repository or environment secrets and variables for SSH delivery:

- `VPS_HOST`: VPS hostname or address.
- `VPS_PORT`: SSH port; set it as a repository or `production` environment secret or variable. Defaults to `22` when omitted.
- `VPS_USER`: SSH account with permission to manage the deployment directory and Docker Compose.
- `VPS_SSH_KEY`: private key for that account.
- `VPS_DEPLOY_PATH`: absolute path to the MediaRelay directory on the VPS. It contains `.env`, `docker-compose.prod.yml`, and `deploy.sh` at its root.

The workflow uses `StrictHostKeyChecking=no` for the deployment SSH connection, so a separate host-key secret is not required.

Create `.env` in that directory before deploying. It must include `MINIO_ENDPOINT` (for example, `filestorage-minio:9000`), `MINIO_USE_SSL=false`, `MINIO_PUBLIC_ENDPOINT` (for example, `s3.hugojava.dev`), `MINIO_PUBLIC_USE_SSL=true`, `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD`, `PUBLIC_APP_BASE_URL`, `PUBLIC_MEDIA_BASE_URL`, and `UPLOAD_API_KEY`; also set `DISCORD_TOKEN` and `DISCORD_APPLICATION_ID` when `DISCORD_ENABLED` is not `false`. Set `IMAGE` to `ghcr.io/vitorhugo-dotnet/media-relay:sha-<40-character-commit-sha>` for manual deployments. The `deploy/deploy.sh` script checks the immutable image reference and required values before pulling or restarting only the MediaRelay app; it does not manage the existing MinIO container or print secret values.

The local Compose file exposes only loopback ports for the app and MinIO API, keeps the console unbound, and stores MinIO data in a named volume. Production Compose exposes only the MediaRelay app; the existing MinIO service remains independently managed. Back up MinIO using the storage service's normal backup process.

GitHub Actions runs the .NET restore/build/test checks and a Docker build for pull requests without GHCR login or production secrets. A successful push to `main` publishes `ghcr.io/vitorhugo-dotnet/media-relay:sha-<commit-sha>` and `:latest`, then deploys the SHA-tagged image through the protected `production` environment. The workflow copies `deploy/docker-compose.prod.yml` and `deploy/deploy.sh` directly into the root of `VPS_DEPLOY_PATH`, beside the production `.env`; it does not build or manage MinIO during deployment. Keep the production `.env` on the VPS.

Set the GHCR package visibility to **Public** so the VPS can pull images without a registry credential. The deployment script intentionally uses the VPS's existing Docker login state and does not transfer a registry token.

To redeploy an existing image, use **Actions → MediaRelay CI/CD → Run workflow**, enable `deploy`, and provide its immutable `sha-<40-character-commit-sha>` tag in `image_tag`. Manual deployment does not build or publish an image. Pull requests never receive the GHCR publishing token or VPS secrets.
