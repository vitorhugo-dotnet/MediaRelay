# MediaRelay

MediaRelay is a self-hosted ASP.NET Core media uploader. It accepts browser uploads directly into MinIO, authenticated uploads from ShareX, and can publish uploads to Discord.

## Run locally

Requirements: .NET 10 SDK, Docker Engine, and Docker Compose.

```sh
cp .env.example .env
# Replace the example credentials in .env before starting the stack.
docker build -t media-relay:local .
docker compose --env-file .env -f deploy/docker-compose.prod.yml up -d
```

The example `.env` already selects `IMAGE=media-relay:local`. For production, use an immutable `ghcr.io/vitorhugo-dotnet/media-relay:sha-<40-character-commit-sha>` image tag.

The app health endpoint is `http://localhost:8080/health`; the MinIO S3 API is on `http://localhost:9000`. The MinIO console is not published to the host. Stop the stack with `docker compose --env-file .env -f deploy/docker-compose.prod.yml down`. The named `minio-data` volume remains when containers are recreated; `down -v` deletes it.

The compose stack binds the app and MinIO API to loopback. A reverse proxy on the VPS should route the public app host to `127.0.0.1:8080` and the media host to `127.0.0.1:9000`, with HTTPS enabled. Configure the `PUBLIC_APP_BASE_URL` and `PUBLIC_MEDIA_BASE_URL` values to those HTTPS origins. Set `MINIO_PUBLIC_ENDPOINT` to the media hostname (without a scheme); it is used to construct browser presigned upload requests.

## Configuration

Copy `.env.example` and replace every placeholder with a unique value. Keep `.env` private and restrict its permissions. `UPLOAD_API_KEY` is the bearer token ShareX sends. `MINIO_ROOT_USER` and `MINIO_ROOT_PASSWORD` are MinIO administrator credentials: the current application initializes the bucket and its anonymous read-only object policy, so it needs those permissions. Do not reuse these credentials elsewhere.

`DISCORD_ENABLED` defaults to `true`. Set it to `false` to run without Discord credentials. When enabled, configure `DISCORD_TOKEN` and `DISCORD_APPLICATION_ID`; `DISCORD_ALLOWED_GUILD_IDS` can optionally restrict accepted guilds. The Discord bot needs the `bot` and `applications.commands` OAuth scopes and permission to view channels, send messages, and read message history. On uncertain publication retries, MediaRelay checks the latest 50 messages in the source channel for an already-sent URL; read history is required for this duplicate reconciliation.

The Compose configuration sets MinIO's global `MINIO_API_CORS_ALLOW_ORIGIN` to `PUBLIC_APP_BASE_URL`, allowing the browser uploader origin. MinIO exposes this global origin control; it does not provide the per-bucket method and header restriction described by an ideal least-privilege CORS policy. The bucket policy remains limited to anonymous `GetObject` access.

## ShareX

Import [`sharex/MediaRelay.sxcu`](sharex/MediaRelay.sxcu) into ShareX, then enter the same `UPLOAD_API_KEY` when prompted. The default upload URL in that file is `https://upload.hugodotnet.dev/api/sharex/upload`; change it to match your deployment's app hostname if needed.

## Production deployment

Create DNS records for the app and media hosts and configure the VPS reverse proxy as described above. Keep the MinIO console private. The proxy must preserve the URL path and forward requests to the corresponding loopback port. Allow uploads up to `MAX_UPLOAD_SIZE` in any proxy body-size limit.

The deployment workflow uses the protected GitHub Actions environment named `production`. Configure these repository or environment secrets for SSH delivery:

- `VPS_HOST`: VPS hostname or address.
- `VPS_USER`: SSH account with permission to manage the deployment directory and Docker Compose.
- `VPS_SSH_KEY`: private key for that account.
- `VPS_DEPLOY_PATH`: absolute path to the MediaRelay deployment directory on the VPS.

Create `.env` in that directory before deploying. It must include `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD`, `PUBLIC_APP_BASE_URL`, `PUBLIC_MEDIA_BASE_URL`, `MINIO_PUBLIC_ENDPOINT`, and `UPLOAD_API_KEY`; also set `DISCORD_TOKEN` and `DISCORD_APPLICATION_ID` when `DISCORD_ENABLED` is not `false`. Set `IMAGE` to `ghcr.io/vitorhugo-dotnet/media-relay:sha-<40-character-commit-sha>` for manual deployments. The `deploy/deploy.sh` script checks the immutable image reference and required values before pulling or restarting services; it does not print secret values.

The Compose file exposes only loopback ports for the app and MinIO API, keeps the console unbound, and stores MinIO data in a named volume. Back up that volume using your VPS's normal storage backup process.
