#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_dir="$(cd -- "$script_dir/.." && pwd)"
compose_file="$script_dir/docker-compose.prod.yml"
env_file="$project_dir/.env"

if [[ -z "${IMAGE:-}" ]]; then
  printf '%s\n' 'Deployment blocked: IMAGE must be set to an immutable SHA-tagged image.' >&2
  exit 2
fi
if [[ ! "$IMAGE" =~ ^ghcr\.io/vitorhugo-dotnet/media-relay:sha-[0-9a-f]{40}$ ]]; then
  printf '%s\n' 'Deployment blocked: IMAGE must match ghcr.io/vitorhugo-dotnet/media-relay:sha-<40-character-commit-sha>.' >&2
  exit 2
fi
if [[ ! -f "$env_file" ]]; then
  printf '%s\n' 'Deployment blocked: create the production .env file beside the Compose project.' >&2
  exit 2
fi

required_keys=(MINIO_ROOT_USER MINIO_ROOT_PASSWORD PUBLIC_APP_BASE_URL PUBLIC_MEDIA_BASE_URL MINIO_PUBLIC_ENDPOINT UPLOAD_API_KEY)
if [[ "$(grep -E '^[[:space:]]*DISCORD_ENABLED[[:space:]]*=' "$env_file" | tail -n 1 | cut -d= -f2- | tr -d '\r[:space:]')" != "false" ]]; then
  required_keys+=(DISCORD_TOKEN DISCORD_APPLICATION_ID)
fi
for key in "${required_keys[@]}"; do
  if ! grep -Eq "^[[:space:]]*${key}[[:space:]]*=.+" "$env_file"; then
    printf 'Deployment blocked: required value %s is missing from the production .env file.\n' "$key" >&2
    exit 2
  fi
done

cd "$project_dir"
docker compose --env-file "$env_file" -f "$compose_file" config --quiet
docker compose --env-file "$env_file" -f "$compose_file" pull app
docker compose --env-file "$env_file" -f "$compose_file" up -d --remove-orphans
printf 'MediaRelay deployment is running with %s.\n' "$IMAGE"
