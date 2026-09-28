# MediaRelay

MediaRelay is a self-hosted ASP.NET Core media uploader. It accepts browser uploads directly into MinIO, authenticated uploads from ShareX, and can publish uploads to Discord.

## Run locally

Requirements: .NET 10 SDK, Docker Engine, and Docker Compose.

MediaRelay builds its MinIO Community image locally from the pinned upstream source commit `9e49d5e7a648f00e26f2246f4dc28e6b07f8c84a` (release `RELEASE.2025-10-15T17-29-55Z`). Community is distributed as source; the Compose deployment and CI test job compile this source into `media-relay-minio:community-9e49d5e`. The MinIO repository is archived, so this image does not receive upstream Community security updates. Review and update the pinned source deliberately if you maintain this deployment.

```sh
cp .env.example .env
# Replace the example credentials in .env before starting the stack.
docker build -t media-relay:local .
docker compose --env-file .env -f deploy/docker-compose.prod.yml up -d --build
```

The example `.env` already selects `IMAGE=media-relay:local`. The Compose command builds MinIO Community from the pinned source commit. For production, use an immutable `ghcr.io/vitorhugo-dotnet/media-relay:sha-<40-character-commit-sha>` image tag.

The app health endpoint is `http://localhost:8080/health`; the MinIO S3 API is on `http://localhost:9000`. The MinIO console is not published to the host. Stop the stack with `docker compose --env-file .env -f deploy/docker-compose.prod.yml down`. The named `minio-data` volume remains when containers are recreated; `down -v` deletes it.

The compose stack binds the app and MinIO API to loopback. A reverse proxy on the VPS should route the public app host to `127.0.0.1:8080` and the media host to `127.0.0.1:9000`, with HTTPS enabled. Configure the `PUBLIC_APP_BASE_URL` and `PUBLIC_MEDIA_BASE_URL` values to those HTTPS origins. Set `MINIO_PUBLIC_ENDPOINT` to the media hostname (without a scheme); it is used to construct browser presigned upload requests.

## Configuration

Copy `.env.example` and replace every placeholder with a unique value. Keep `.env` private and restrict its permissions. `UPLOAD_API_KEY` is the bearer token ShareX sends. `MINIO_ROOT_USER` and `MINIO_ROOT_PASSWORD` are MinIO administrator credentials: the current application initializes the bucket and its anonymous read-only object policy, so it needs those permissions. Do not reuse these credentials elsewhere.

`DISCORD_ENABLED` defaults to `true`. Set it to `false` to run without Discord credentials. When enabled, configure `DISCORD_TOKEN` and `DISCORD_APPLICATION_ID`; `DISCORD_ALLOWED_GUILD_IDS` can optionally restrict accepted guilds. If left empty, the bot accepts `/upload` from any guild where the command is available. The Discord bot needs the `bot` and `applications.commands` OAuth scopes and permission to view channels, send messages, and read message history. On uncertain publication retries, MediaRelay checks the latest 50 messages in the source channel for an already-sent URL; read history is required for this duplicate reconciliation.

The Compose configuration sets MinIO's global `MINIO_API_CORS_ALLOW_ORIGIN` to `PUBLIC_APP_BASE_URL`, allowing the browser uploader origin. MinIO exposes this global origin control; it does not provide the per-bucket method and header restriction described by an ideal least-privilege CORS policy. The bucket policy remains limited to anonymous `GetObject` access.

To run the storage integration tests locally, build the same pinned image first: `docker build -f deploy/minio-community.Dockerfile -t media-relay-minio:community-9e49d5e .`, then run `dotnet test tests/MediaRelay.Tests/MediaRelay.Tests.csproj --filter FullyQualifiedName~MinioMediaStorageTests`. The tests use Docker to start the local image.

## ShareX

Import [`sharex/MediaRelay.sxcu`](sharex/MediaRelay.sxcu) into ShareX, then enter the same `UPLOAD_API_KEY` when prompted. The default upload URL in that file is `https://upload.hugodotnet.dev/api/sharex/upload`; change it to match your deployment's app hostname if needed.

## Production deployment

Create DNS records for the app and media hosts and configure the VPS reverse proxy as described above. Keep the MinIO console private. The proxy must preserve the URL path and forward requests to the corresponding loopback port. Allow uploads up to `MAX_UPLOAD_SIZE` in any proxy body-size limit.

The deployment workflow uses the protected GitHub Actions environment named `production`. Configure these repository or environment secrets for SSH delivery:

- `VPS_HOST`: VPS hostname or address.
- `VPS_USER`: SSH account with permission to manage the deployment directory and Docker Compose.
- `VPS_SSH_KEY`: private key for that account.
- `VPS_SSH_KNOWN_HOSTS`: pinned SSH host-key line(s) in OpenSSH `known_hosts` format, matching `VPS_HOST`.
- `VPS_DEPLOY_PATH`: absolute path to the MediaRelay deployment directory on the VPS.

Provision `VPS_SSH_KNOWN_HOSTS` from a trusted server console or administrator. Verify the host-key fingerprint through an independent trusted channel before adding its public key in `known_hosts` format; do not trust a key collected from the deployment runner at connection time. The workflow writes this secret to its private SSH configuration and checks that it contains a key for `VPS_HOST` before connecting. Keep the private key and host-key lines in the protected `production` environment secrets; neither value is printed by the workflow.

Create `.env` in that directory before deploying. It must include `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD`, `PUBLIC_APP_BASE_URL`, `PUBLIC_MEDIA_BASE_URL`, `MINIO_PUBLIC_ENDPOINT`, and `UPLOAD_API_KEY`; also set `DISCORD_TOKEN` and `DISCORD_APPLICATION_ID` when `DISCORD_ENABLED` is not `false`. Set `IMAGE` to `ghcr.io/vitorhugo-dotnet/media-relay:sha-<40-character-commit-sha>` for manual deployments. The `deploy/deploy.sh` script checks the immutable image reference and required values before pulling or restarting services; it does not print secret values.

The Compose file exposes only loopback ports for the app and MinIO API, keeps the console unbound, and stores MinIO data in a named volume. Back up that volume using your VPS's normal storage backup process.

GitHub Actions runs the .NET restore/build/test checks and a Docker build for pull requests without GHCR login or production secrets. A successful push to `main` publishes `ghcr.io/vitorhugo-dotnet/media-relay:sha-<commit-sha>` and `:latest`, then deploys the SHA-tagged image through the protected `production` environment. The workflow transfers `deploy/docker-compose.prod.yml`, `deploy/deploy.sh`, and `deploy/minio-community.Dockerfile`; the VPS builds the pinned MinIO source image during deployment. Keep the production `.env` on the VPS.

Set the GHCR package visibility to **Public** so the VPS can pull images without a registry credential. The deployment script intentionally uses the VPS's existing Docker login state and does not transfer a registry token.

To redeploy an existing image, use **Actions → MediaRelay CI/CD → Run workflow**, enable `deploy`, and provide its immutable `sha-<40-character-commit-sha>` tag in `image_tag`. Manual deployment does not build or publish an image. Pull requests never receive the GHCR publishing token or VPS secrets.
