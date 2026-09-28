#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_dir="$(cd -- "$script_dir/.." && pwd)"
compose_file="$script_dir/docker-compose.prod.yml"
env_file="$project_dir/.env"

read_env_value() {
  awk -v wanted="$1" '
    /^[[:space:]]*#/ { next }
    {
      equals = index($0, "=")
      if (!equals) next
      key = substr($0, 1, equals - 1)
      gsub(/^[[:space:]]+|[[:space:]]+$/, "", key)
      if (key != wanted) next
      value = substr($0, equals + 1)
      gsub(/^[[:space:]]+|[[:space:]]+$/, "", value)
      quote = sprintf("%c", 39)
      first = substr(value, 1, 1)
      last = substr(value, length(value), 1)
      if (length(value) >= 2 && ((first == "\"" && last == "\"") || (first == quote && last == quote)))
        value = substr(value, 2, length(value) - 2)
      found = 1
    }
    END { if (found) print value }
  ' "$env_file"
}

if [[ ! -f "$env_file" ]]; then
  printf '%s\n' 'Deployment blocked: create the production .env file beside the Compose project.' >&2
  exit 2
fi

if [[ -z "${IMAGE:-}" ]]; then
  IMAGE="$(read_env_value IMAGE)"
fi
if [[ ! "$IMAGE" =~ ^ghcr\.io/vitorhugo-dotnet/media-relay:sha-[0-9a-f]{40}$ ]]; then
  printf '%s\n' 'Deployment blocked: IMAGE must match ghcr.io/vitorhugo-dotnet/media-relay:sha-<40-character-commit-sha>.' >&2
  exit 2
fi
export IMAGE

required_keys=(MINIO_ROOT_USER MINIO_ROOT_PASSWORD PUBLIC_APP_BASE_URL PUBLIC_MEDIA_BASE_URL MINIO_PUBLIC_ENDPOINT UPLOAD_API_KEY)
if [[ "$(read_env_value DISCORD_ENABLED | tr '[:upper:]' '[:lower:]')" != "false" ]]; then
  required_keys+=(DISCORD_TOKEN DISCORD_APPLICATION_ID)
fi
for key in "${required_keys[@]}"; do
  value="$(read_env_value "$key")"
  normalized_value="$(printf '%s' "$value" | tr -d '[:space:]')"
  if [[ -z "$normalized_value" ]]; then
    printf 'Deployment blocked: required value %s is missing from the production .env file.\n' "$key" >&2
    exit 2
  fi
  if [[ "${value,,}" =~ replace[-_[:space:]]with|placeholder|change[-_[:space:]]me|your[-_[:space:]] ]]; then
    printf 'Deployment blocked: replace the example value for %s in the production .env file.\n' "$key" >&2
    exit 2
  fi
done

cd "$project_dir"
docker compose --env-file "$env_file" -f "$compose_file" config --quiet
docker compose --env-file "$env_file" -f "$compose_file" pull app
docker compose --env-file "$env_file" -f "$compose_file" build minio
docker compose --env-file "$env_file" -f "$compose_file" up -d --remove-orphans
printf 'MediaRelay deployment is running with %s.\n' "$IMAGE"
