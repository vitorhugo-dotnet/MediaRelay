# Task 2 Report — Media validation and object IDs

## Status
Implemented and committed Task 2.

## Changes
- Added `MediaValidator.Validate(fileName, declaredContentType, header)` with the six exact extension/MIME pairs from the spec. The validator checks extension and declared MIME and requires matching PNG, JPEG, GIF, WebP, or MP4 header bytes. It returns a `ValidatedMedia` containing the normalized extension and expected MIME type.
- Added `ValidatedMedia` as a small immutable result record.
- Added `ObjectIdGenerator.Create(extension)`. It generates 18 bytes with `RandomNumberGenerator`, encodes them as 24 URL-safe Base64 characters, and appends the supplied safe lowercase extension. Original filenames are not accepted by this API and are never used in generated IDs.
- Added tests for every allowed pair, mismatched and unsupported types, truncated/incorrect signatures, generated ID format and uniqueness, and unsafe extensions.

## Validation
- TDD red: the requested focused validator run initially failed to compile because the Uploads API did not exist.
- `dotnet test tests/MediaRelay.Tests --filter FullyQualifiedName~MediaValidatorTests --no-restore`: 17 passed.
- `dotnet test tests/MediaRelay.Tests --filter FullyQualifiedName~ObjectIdGeneratorTests --no-restore`: 4 passed.
- `dotnet test tests/MediaRelay.Tests --no-restore`: 23 passed.
- `git diff --check`: passed.

## Limits
Signature checks are intentionally shallow magic-byte checks; deep media decoding is outside the spec. Browser-to-storage completion must separately verify the stored MIME metadata and size as required by the spec.
