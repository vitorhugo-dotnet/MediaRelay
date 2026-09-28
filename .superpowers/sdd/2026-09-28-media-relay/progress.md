# SDD ledger — plan: docs/superpowers/plans/2026-09-28-media-relay.md

Base at execution start: 0fc97104305043a8817368568e632e993a19f61e (`main` contains the approved spec and plan, plus local worktree ignore rule).

## Preflight plan scan — shared files/interfaces

| Tasks | Shared file or interface | Producer vs consumer check | Finding |
|---|---|---|---|
| 1 → 5 | `Program.cs`, upload options | Task 1 registers the Minimal API and validated config; Task 5 adds upload and health routes. | Consistent; endpoint extensions can be registered without changing ownership. |
| 1 → 6 | `Program.cs`, Discord options/packages | Task 1 creates host/config contracts; Task 6 adds hosted Discord lifecycle and DI. | Consistent; Task 6 owns Discord registrations. |
| 1 → 7 | `Program.cs`, static files | Task 1 hosts ASP.NET; Task 7 adds page routes/static middleware. | Consistent. |
| 1 → 8 | `Program.cs`, upload API key config | Task 1 supplies upload configuration; Task 8 maps ShareX route. | Consistent; key remains server-side. |
| 1 → 10 | `MediaRelay.sln`, test project | Task 1 establishes solution and test paths; Task 10 builds/tests the solution. | Consistent. |
| 2 → 3 | `ValidatedMedia` metadata | Validator output supplies extension/MIME/size metadata retained by session state. | Consistent. |
| 2 → 5 | `MediaValidator`, `ObjectIdGenerator` | Task 5 uses them to validate prepare requests and assign object keys. | Consistent. |
| 2 → 8 | `MediaValidator`, `ObjectIdGenerator` | Task 8 uses them for streamed ShareX data. | Consistent. |
| 3 → 5 | `UploadSessionService` | Task 5 creates/claims/completes browser sessions. | Consistent; transition API must be defined during Task 3. |
| 3 → 6 | `UploadSessionService` | Task 6 creates Discord-origin sessions and preserves publication state. | Consistent. |
| 3 → 7 | session lookup | Task 7 validates `/u/{token}` and handles invalid/expired sessions. | Consistent. |
| 4 → 5 | `IMediaStorage` | Task 5 creates presigned uploads, stats objects, and returns public URLs. | Consistent; signatures must match Task 4 exactly. |
| 4 → 7 | presigned upload and storage CORS | Browser uses Task 4 authorization; Task 7 constrains MinIO CORS to app origin. | Consistent; test both sides in integration verification. |
| 4 → 8 | `IMediaStorage` | Task 8 streams validated objects through storage. | Consistent. |
| 4 → 9 | MinIO configuration | Task 9 runs the same configured MinIO endpoint/bucket persistently. | Consistent; no second storage abstraction. |
| 5 → 6 | completion and publication state | Task 6 connects successful completion to `IMediaPublisher` and retry state. | Consistent; publication outcome remains explicit in API result. |
| 5 → 7 | API routes and `Program.cs` | Task 7 calls Task 5 endpoints from static UI. | Consistent. |
| 5 → 8 | `Program.cs`, endpoint registration | Task 8 adds independent ShareX route alongside upload/health. | Consistent. |
| 6 → 7 | `Program.cs`, uploader route | Discord button URL resolves to Task 7 route and created token. | Consistent. |
| 9 → 10 | Compose and deployment files | Task 10 transfers/uses Task 9 production files and image variable. | Consistent; deploy exact immutable SHA. |
| 9 → 11 | deployable repository contents | Task 11 publishes completed source and deployment artifacts. | Consistent. |
| 10 → 11 | Actions workflow and repository | Task 11 publishes main branch after workflow is ready. | Consistent; no PR publish/deploy. |

## Preflight plan scan — per-task internal consistency

| Task | Own text: files, tests, interfaces and constraints | Finding |
|---|---|---|
| 1 | Creates .NET 10 solution, host/options, test project and non-secret env template; validates defaults and required production config. | Consistent. |
| 2 | Validator and ID generator have isolated tests before implementation and focused verification. | Consistent. |
| 3 | Session creation, hash-only storage, expiry, atomic one-time transitions and idempotence are covered by tests. | Consistent. |
| 4 | MinIO adapter owns presigning, upload/stat/public URL, read-only anonymous policy and Testcontainers checks. | Consistent; enforce actual size again at completion per spec. |
| 5 | Prepare/complete/health route tests align with API contracts and error behavior. | Consistent. |
| 6 | Discord lifecycle/command/publisher tests use fakes; no real connection required. | Consistent. |
| 7 | Page/UI calls prepare/complete and uploads bytes to MinIO; tests plus local integration check exercise route and direct transfer. | Consistent. |
| 8 | ShareX API key, streamed media validation and `.sxcu` response contract align. | Consistent. |
| 9 | Container and Compose checks cover persistent MinIO and health; deploy script uses image config. | Consistent. |
| 10 | PR validation, main publish, manual SHA deploy and SSH workflow map to repository workflow. | Consistent. |
| 11 | Creates remote only after checking it is absent, applies public metadata/topics and pushes verified main. | Consistent; remote creation/push are explicitly user-authorized by the request. |

No conflicting task pairs or self-contradictions found. The review package/task reports must confirm exact signatures, especially session transitions and storage presign request fields.

## Task progress

Task 1: implementation complete — options bind from configuration and the specified environment variable names, required values validate at startup, default values and failure cases are tested; `dotnet test MediaRelay.sln` passed (2 tests).

Task 1: Ruling: Defer endpoint mapping from scaffold to the feature tasks that create the route extensions — the spec has no product routes during Task 1, and empty placeholder mapping extensions add no behavior; the feature tasks own their routes. If wrong, route wiring could be omitted, so verify all endpoints during final review.
Task 1: fix round 1/5 started (endpoint mapping contract clarified; align canonical host samples)
Task 1: fix round 1/5 (2 addressed, 0 open; endpoint mapping contract and canonical host sample values; commits f8c0536..4843cdc)
Task 1: minor (deferred): fix report summarized two passing test runs but did not preserve the raw console output; command and passing counts are recorded.
Task 1: complete (commits 0fc9710..4843cdc, review clean; one minor deferred)
Task 2: minor (deferred): the pre-implementation TDD run failed while compiling absent APIs, so red evidence did not show behavior assertions failing; the completed focused tests passed.
Task 2: complete (commits 75952d4..7774b9e, spec and quality review approved; one minor deferred)
Task 3: fix round 1/5 (1 addressed, 0 open; publication claim is exclusive under per-session lock, including 16-way concurrency; commits 8e0d51d..22dd835)
Task 3: complete (commits 9be3744..22dd835, review clean)
Task 4: minor (deferred): four MinIO Testcontainers integration cases are authored but could not execute because Docker daemon was unavailable; rerun when Docker is available before declaring live MinIO validation done.
Task 4: minor (deferred): offline policy unit test repeats one public-endpoint assertion.
Task 4: complete (commit c21d69f, spec and quality review approved; live integration blocked by environment, two minors deferred)
Task 4: complete (commit c21d69f, review clean; live integration tests remain environment-blocked; two minors deferred)
Ruling: Add `MediaValidator.ValidateMetadata(fileName, declaredContentType)` in Task 5 and retain signature-checking `Validate(..., header)` for backend-streamed files — browser prepare has no media bytes by design, and its data must go directly to MinIO, so requiring signature bytes at prepare would violate the specified flow. If wrong, unsupported or mismatched browser media could be authorized; completion metadata/size and browser-to-MinIO behavior remain separately checked.
Ruling: Add an expired-token API test in Task 5 — Task 5 explicitly requires missing/invalid/expired route coverage and the spec acceptance requires expired tokens to be rejected; a session unit test alone does not verify the API response. If wrong, expired sessions could be mapped incorrectly at the HTTP boundary.
Ruling: Allow safe retry after a presign-generation failure by releasing `Preparing` to `Created` only before any URL/form fields are returned — the object has not been written and no authorization was given to the browser, so keeping the session permanently unusable hurts recovery without strengthening the one-use completion rule. If wrong, a returned/usable presigned authorization could be duplicated, so the release must only occur when presign generation itself throws before returning.
Task 5: fix round 1/5 started (add expiry route coverage and safe recovery after presign generation failure; controller rulings recorded above)
Task 5: fix round 1/5 (2 addressed, 0 open; expired-token route test and guarded preparation-release retry; commits e012a2a..69d0495)
Task 5: minor (deferred): an OperationCanceledException not tied to RequestAborted is now surfaced as 503; reviewer suggested preserving cancellation semantics.
Task 5: minor (deferred): `git diff --check` noted an extra blank line at the end of the updated plan.
Task 5: complete (commits d379dd3..69d0495, review clean; two minors deferred; Docker-backed MinIO tests remain unavailable in this environment).
Ruling: Keep the optional `Discord:Enabled=false` mode but conditionally require token/application ID only when enabled — the shipped code already offers the flag for HTTP-only test environments, and it must actually function; default remains enabled. If wrong, startup may fail for a deliberate no-bot environment or could allow production without credentials if condition is inverted.
Ruling: Validate configured `DISCORD_APPLICATION_ID` against `GetApplicationInfoAsync().Id` before registering commands — Discord.Net 3.20.1 derives command registration identity from the authenticated REST client, so validation is the supported way to ensure the configured application and token refer to the same app. If wrong, registration could silently target the token-bound app despite an erroneous configured ID.
Ruling: On publication retry after an uncertain Discord send, search recent bot-authored channel messages for the exact media URL and suppress a duplicate; if history cannot be checked, fail closed. The .NET client send path has no exposed idempotency key, and remote acceptance with lost acknowledgement otherwise permits duplicate posts. If wrong, the additional history read requires Read Message History and could delay retries or still miss an older post outside the bounded recent window.
Task 6: fix round 1/5 (3 addressed, 0 open; conditional credentials, authenticated application ID check before registration, bounded publication retry reconciliation; commits 8210d08..cd79887)
Task 6: complete (commits 8210d08..cd79887, scoped review approved; 28 focused tests passed; 59 solution tests passed, 4 MinIO integration tests blocked because Docker daemon unavailable; recent-message reconciliation has a documented 50-message bound)
Ruling: Use browser FormData with the returned presigned POST fields and no custom headers, and set MinIO global MINIO_API_CORS_ALLOW_ORIGIN to PUBLIC_APP_BASE_URL in Task 9 — MinIO SDK 6.0.3 has no bucket CORS API. Its global setting cannot restrict methods/headers per bucket, so preserve this limitation in deployment docs and do not claim per-bucket restrictions.
Task 7: fix round 1/5 (1 addressed, 0 open; rejected drag-and-drop files can no longer reach prepare; commits 06dddff..d6514ad)
Task 7: complete (commits 06dddff..d6514ad, scoped review approved; focused endpoint tests 2/2; browser-to-MinIO and global CORS deployment verification deferred because Docker unavailable and MinIO global setting has no per-bucket method/header control)
Task 8: fix round 1/5 (1 addressed, 0 open; replace unsupported environment marker with documented ShareX inputbox header prompt; commits f34ec53..96507f3)
Task 8: complete (commits f34ec53..96507f3, scoped review approved; focused ShareX tests 7/7)
Task 9: fix round 1/5 (2 addressed, 0 open; resolve IMAGE from environment or dotenv key only, reject blank/placeholders; add preflight test coverage; commits fcde6ea..e5d8cdb)
Task 9: complete (commits fcde6ea..e5d8cdb, scoped review approved; Compose contract test and Compose config passed; deploy script syntax and 9 focused tests passed; Docker build and live stack persistence remain environment-blocked by unavailable Docker Desktop daemon)
Task 10: fix round 1/5 (1 addressed; pin VPS SSH known-host entry rather than trusting ssh-keyscan; commits aae72e7..fd13b67)
Task 10: complete (commits aae72e7..fd13b67, scoped workflow and SSH pin reviews approved; 78 solution tests passed and 4 MinIO integration tests blocked by unavailable Docker; live Actions PR-style run and automated YAML parser remain unavailable)
Final review fix: publish the canonical media URL from IMediaStorage instead of the upload page URL, and treat an empty Discord guild allowlist as unrestricted per documented optional restriction; regression tests passed and final reviewer re-review approved.
