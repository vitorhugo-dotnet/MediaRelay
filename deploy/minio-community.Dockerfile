# Build the final Community edition source release. MinIO Community is source-only.
ARG GO_IMAGE=golang:1.24.8-alpine3.22
FROM ${GO_IMAGE} AS build

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

FROM alpine:3.22
RUN apk add --no-cache ca-certificates \
    && addgroup -S minio \
    && adduser -S -G minio minio \
    && mkdir /data \
    && chown minio:minio /data
COPY --from=build /out/minio /usr/bin/minio
USER minio
EXPOSE 9000 9001
ENTRYPOINT ["/usr/bin/minio"]
