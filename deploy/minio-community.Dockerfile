# Build the final Community edition source release. MinIO Community is source-only.
FROM golang:1.24.8-alpine3.22@sha256:3d78beb141d98f42337f1252ecf2a5f20374109929a4c3f6817f9e4179cc0ae5 AS build

ARG MINIO_COMMIT=9e49d5e7a648f00e26f2246f4dc28e6b07f8c84a
ARG MINIO_RELEASE_DATE=2025-10-15T17:29:55Z

RUN apk add --no-cache git
WORKDIR /src
RUN git init \
    && git remote add origin https://github.com/minio/minio.git \
    && git fetch --depth=1 origin "${MINIO_COMMIT}" \
    && git checkout --detach FETCH_HEAD \
    && test "$(git rev-parse HEAD)" = "${MINIO_COMMIT}"

RUN mkdir -p /out \
    && MINIO_RELEASE=RELEASE go run buildscripts/gen-ldflags.go "${MINIO_RELEASE_DATE}" > /tmp/minio-ldflags \
    && CGO_ENABLED=0 GOOS=linux go build -tags kqueue -trimpath \
    --ldflags "$(cat /tmp/minio-ldflags)" \
    -o /out/minio .

FROM alpine:3.22@sha256:5291449c3df73caf6ed85e649dec1b9e818b39a5d8c871e97afc13e9cd5e8fa8
RUN apk add --no-cache ca-certificates \
    && addgroup -S minio \
    && adduser -S -G minio minio \
    && mkdir /data \
    && chown minio:minio /data
COPY --from=build /out/minio /usr/bin/minio
USER minio
EXPOSE 9000 9001
ENTRYPOINT ["/usr/bin/minio"]
