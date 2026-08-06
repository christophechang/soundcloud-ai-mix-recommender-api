# MixLab Maps (library-map job + endpoints) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Queue, claim, store, and serve per-upload library-map JSON — the API leg between MixLab's `mixlab --map` (shipped, v1.12.0) and mixlab-web's constellation overlay.

**Architecture:** A separate lightweight maps queue, never touching the run manifest/index contracts: `maps/index.json` holds job entries; the engine payload is stored **verbatim** (bytes in, bytes out — the API never deserializes it) at `maps/{uploadId}.json`. Five thin use cases behind `Core.Contracts` interfaces, one new controller on the existing `api/mixlab` route + `MixLab:ApiSecret` bearer gate, blob repository on the existing `IMixLabBlobGateway`. Claim semantics mirror runs (oldest queued, stale-lease requeue via `MixLabOptions.ClaimLeaseMinutes`). Latest result per upload wins — a re-request refreshes; staleness is the client's judgment (the payload embeds `catalog_tracks`), a deliberate simplification of the spec's "(uploadId, catalog snapshot)" cache key, recorded here.

**Tech Stack:** .NET 10, NUnit + FluentAssertions (hand-rolled Stub/Spy/Fake doubles — no mocking library), Azure blob via `IMixLabBlobGateway`, StyleCop/CA via `Soltech.ruleset`.

## Global Constraints

- Layering: interfaces + outcome-union results in `Changsta.Ai.Core.Contracts/MixLab/`; domain records in `Changsta.Ai.Core/Domain/MixLab/`; use cases in `Changsta.Ai.Core.BusinessProcesses/MixLab/`; blob code in `Changsta.Ai.Infrastructure.Services.Azure/MixLab/`; controller + DI in `Changsta.Ai.Interface.Api`. `Core.Contracts` references `Core` only.
- Match file-local style exactly: `string`/`int` aliases, `_camelCase` private fields, `sealed record`/`internal sealed class`, `ConfigureAwait(false)` on every await, existing namespace style.
- JSON via `MixLabJsonOptions.Options` for index/domain documents. The map **payload** is never (de)serialized by the API — raw bytes verbatim.
- Outcome-union convention for results: `{Verb}{Feature}Result` sealed class + nested outcome enum, constructed by use cases, mapped to status codes in the controller.
- Controllers stay thin; no new DTO layer — serialize domain records directly (camelCase) like `MixLabRunsController` does.
- Tests: deterministic, no live Azure; use cases tested through the real blob repository against `FakeMixLabBlobGateway` (+ `FakeTimeProvider`); controllers tested with hand-rolled nested Stub/Spy classes + reflection attribute assertions — mirror `ClaimMixLabRunUseCaseTests.cs` and `MixLabUploadsControllerTests.cs`.
- Gate: `dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental` then `dotnet test soundcloud-ai-mix-recommender-api.sln --no-build` — zero warnings-as-errors regressions.
- Conventional commits, no Co-Authored-By.

---

### Task 1: Domain, contracts, and blob paths

**Files:**
- Create: `Changsta.Ai.Core/Domain/MixLab/MixLabMapJob.cs`, `Changsta.Ai.Core/Domain/MixLab/MixLabMapStatus.cs`
- Create: `Changsta.Ai.Core.Contracts/MixLab/IMixLabMapRepository.cs`, `IRequestMixLabMapUseCase.cs`, `IClaimMixLabMapUseCase.cs`, `ICompleteMixLabMapUseCase.cs`, `IFailMixLabMapUseCase.cs`, `IGetMixLabMapUseCase.cs`, `RequestMixLabMapResult.cs`, `GetMixLabMapResult.cs`
- Modify: `Changsta.Ai.Infrastructure.Services.Azure/MixLab/MixLabBlobPaths.cs`
- Test: none yet (pure declarations; compilation is the check — repo convention keeps behavior tests with the implementations in Tasks 2–3)

**Interfaces (exact shapes later tasks depend on):**

```csharp
// MixLabMapStatus.cs — mirrors MixLabRunStatus style (camelCase enum via MixLabJsonOptions)
public enum MixLabMapStatus { Queued, Running, Succeeded, Failed }

// MixLabMapJob.cs — maps/index.json entry; sealed record, MixLabRun style
public sealed record MixLabMapJob
{
    public int SchemaVersion { get; init; } = 1;
    public required string UploadId { get; init; }
    public required MixLabMapStatus Status { get; init; }
    public required DateTimeOffset RequestedAt { get; init; }
    public DateTimeOffset? ClaimedAt { get; init; }
    public string? WorkerId { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? Error { get; init; }
}

// IMixLabMapRepository.cs
Task<MixLabMapJob> RequestAsync(string uploadId, CancellationToken cancellationToken);
Task<MixLabMapJob?> TryClaimOldestQueuedAsync(string workerId, TimeSpan staleLease, CancellationToken cancellationToken);
Task CompleteAsync(string uploadId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
Task FailAsync(string uploadId, string error, CancellationToken cancellationToken);
Task<MixLabMapJob?> GetJobAsync(string uploadId, CancellationToken cancellationToken);
Task<byte[]?> OpenPayloadAsync(string uploadId, CancellationToken cancellationToken);

// RequestMixLabMapResult.cs — outcome union, {Verb}{Feature}Result convention
public sealed class RequestMixLabMapResult
{
    public enum RequestOutcome { Accepted, UnknownUpload, NoUploadsAvailable }
    public required RequestOutcome Outcome { get; init; }
    public MixLabMapJob? Job { get; init; }
}

// GetMixLabMapResult.cs
public sealed class GetMixLabMapResult
{
    public enum GetOutcome { Found, NotFound }
    public required GetOutcome Outcome { get; init; }
    public MixLabMapJob? Job { get; init; }
    /// Raw engine payload bytes when Status == Succeeded; null otherwise.
    public byte[]? Payload { get; init; }
}

// Use case interfaces (one file each, matching sibling style)
IRequestMixLabMapUseCase:  Task<RequestMixLabMapResult> RequestAsync(string uploadId, CancellationToken ct);
IClaimMixLabMapUseCase:    Task<MixLabMapJob?> ClaimAsync(string workerId, CancellationToken ct);
ICompleteMixLabMapUseCase: Task<bool> CompleteAsync(string uploadId, ReadOnlyMemory<byte> payload, CancellationToken ct); // false = unknown/not-running job
IFailMixLabMapUseCase:     Task<bool> FailAsync(string uploadId, string error, CancellationToken ct);
IGetMixLabMapUseCase:      Task<GetMixLabMapResult> GetAsync(string uploadId, CancellationToken ct);
```

`MixLabBlobPaths` additions: `public const string MapsIndex = "maps/index.json";` and `public static string MapPayload(string uploadId) => $"maps/{uploadId}.json";` — style-matching the existing members.

- [ ] **Step 1:** Write all files above, doc comments in the terse style of their siblings (`IMixLabRunRepository` etc.).
- [ ] **Step 2:** `dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental` → compiles clean, zero new warnings.
- [ ] **Step 3:** Commit — `feat(mixlab-maps): domain, contracts, and blob paths for library-map jobs`

---

### Task 2: Blob repository

**Files:**
- Modify: `Changsta.Ai.Infrastructure.Services.Azure/MixLab/IMixLabBlobGateway.cs` + `MixLabBlobGateway.cs` (one new method)
- Modify: `Changsta.Ai.Tests.Unit/MixLab/FakeMixLabBlobGateway.cs` (implement it)
- Create: `Changsta.Ai.Infrastructure.Services.Azure/MixLab/BlobMixLabMapRepository.cs`
- Test: `Changsta.Ai.Tests.Unit/MixLab/BlobMixLabMapRepositoryTests.cs`

**Interfaces:**
- Consumes: Task 1 contracts; `IMixLabBlobGateway`, `MixLabJsonOptions`, `MixLabConcurrencyException`, `TimeProvider`.
- Produces: `internal sealed class BlobMixLabMapRepository : IMixLabMapRepository`, ctor `(IMixLabBlobGateway gateway, TimeProvider timeProvider, ILogger<BlobMixLabMapRepository> logger)`; gateway gains `Task<string> WriteUnconditionalAsync(string blobPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)` (plain overwrite: no `IfMatch`/`IfNoneMatch` conditions — needed because `WriteStreamAsync` is create-only and map payloads are refreshed in place).

Behavior spec:
- `RequestAsync`: mutate-index-with-retry (mirror `BlobMixLabUploadRepository`'s `MutateIndexWithRetryAsync` pattern, `MaxWriteAttempts = 3`): if an entry for `uploadId` exists with `Queued`/`Running` → return it unchanged (idempotent); otherwise upsert an entry `{Status = Queued, RequestedAt = timeProvider.GetUtcNow(), ClaimedAt/WorkerId/CompletedAt/Error = null}` (a `Succeeded`/`Failed` entry is replaced — that's the refresh path). Index ordering: most-recently-requested first, like uploads.
- `TryClaimOldestQueuedAsync`: mirror the runs repository — first requeue any `Running` entry whose `ClaimedAt` is older than `staleLease` (back to `Queued`, clear `ClaimedAt`/`WorkerId`), then claim the **oldest** `Queued` by `RequestedAt` (set `Running`, `ClaimedAt = now`, `WorkerId`), all inside the same mutate-with-retry loop; return the claimed job or null.
- `CompleteAsync`: write payload bytes verbatim via `WriteUnconditionalAsync(MixLabBlobPaths.MapPayload(uploadId), payload, ct)` **first**, then mutate index entry → `Succeeded`, `CompletedAt = now`, `Error = null`. Unknown uploadId in index → throw `MixLabInvalidRunStateException`-style? No: keep repository dumb — upsert a `Succeeded` entry if missing (the use case layer decides legality).
- `FailAsync`: index entry → `Failed`, `CompletedAt = now`, `Error = error` (upsert if missing).
- `GetJobAsync`: read index, find entry, null when absent. `OpenPayloadAsync`: `gateway.ReadAsync(MapPayload(uploadId))` → `Content` or null.

- [ ] **Step 1: Write the failing tests** — `BlobMixLabMapRepositoryTests.cs`, `[TestFixture] internal sealed class`, `BuildSut()` helper returning `(BlobMixLabMapRepository, FakeMixLabBlobGateway, FakeTimeProvider)` mirroring `ClaimMixLabRunUseCaseTests`. Cover, with names in the `Method_condition_expected` convention:
  - `RequestAsync_new_upload_creates_queued_entry`
  - `RequestAsync_queued_entry_is_idempotent` (same job back, index unchanged)
  - `RequestAsync_succeeded_entry_requeues_for_refresh`
  - `TryClaimOldestQueuedAsync_claims_oldest_by_requestedAt_and_marks_running`
  - `TryClaimOldestQueuedAsync_requeues_stale_running_before_claiming` (advance `FakeTimeProvider` past the lease)
  - `TryClaimOldestQueuedAsync_returns_null_when_nothing_queued`
  - `CompleteAsync_stores_payload_verbatim_and_marks_succeeded` (assert stored bytes byte-equal, entry status + `CompletedAt`)
  - `CompleteAsync_survives_index_write_conflict` (use `ForcedConflictsRemaining = 1`)
  - `FailAsync_marks_failed_with_error`
  - `GetJobAsync_and_OpenPayloadAsync_return_null_when_absent`
- [ ] **Step 2:** Run — `dotnet test soundcloud-ai-mix-recommender-api.sln --filter FullyQualifiedName~BlobMixLabMapRepositoryTests` → FAIL (types missing).
- [ ] **Step 3:** Implement gateway method (in `MixLabBlobGateway`: `UploadAsync` with no `BlobRequestConditions`, after `CreateIfNotExistsAsync`, returning the new ETag — mirror `WriteAsync`'s body minus conditions; in `FakeMixLabBlobGateway`: unconditional dictionary upsert honoring `ForcedConflictsRemaining` = 0 consumption, i.e. unconditional writes do NOT consume forced conflicts) and the repository per the behavior spec.
- [ ] **Step 4:** Build + run the filtered tests → PASS; run the full suite once — no collateral damage.
- [ ] **Step 5:** Commit — `feat(mixlab-maps): blob-backed map job repository with claim leasing`

---

### Task 3: Use cases

**Files:**
- Create in `Changsta.Ai.Core.BusinessProcesses/MixLab/`: `RequestMixLabMapUseCase.cs`, `ClaimMixLabMapUseCase.cs`, `CompleteMixLabMapUseCase.cs`, `FailMixLabMapUseCase.cs`, `GetMixLabMapUseCase.cs`
- Test: `Changsta.Ai.Tests.Unit/MixLab/RequestMixLabMapUseCaseTests.cs`, `ClaimMixLabMapUseCaseTests.cs`, `CompleteMixLabMapUseCaseTests.cs`, `GetMixLabMapUseCaseTests.cs` (Fail is two lines — cover inside Complete's fixture file or its own, implementer's call)

**Interfaces:**
- Consumes: Task 1 contracts, Task 2 repository, `IMixLabUploadRepository` (upload-id validation), `MixLabOptions` (claim lease).
- Produces: implementations registered in Task 4.

Behavior spec:
- `RequestMixLabMapUseCase` ctor `(IMixLabMapRepository maps, IMixLabUploadRepository uploads)`: resolve `"latest"` via `uploads.GetLatestIdAsync` (null → `NoUploadsAvailable`); other ids must exist in `uploads.GetIndexAsync` (else `UnknownUpload`) — mirror `EnqueueMixLabRunUseCase`'s resolution logic exactly; then `maps.RequestAsync` → `Accepted` with the job.
- `ClaimMixLabMapUseCase` ctor `(IMixLabMapRepository maps, MixLabOptions options)`: normalize/default `workerId` the way `ClaimMixLabRunUseCase` does; `maps.TryClaimOldestQueuedAsync(workerId, TimeSpan.FromMinutes(options.ClaimLeaseMinutes), ct)`.
- `CompleteMixLabMapUseCase` / `FailMixLabMapUseCase` ctor `(IMixLabMapRepository maps)`: `GetJobAsync` first — return false unless the job exists and `Status == Running` (guards a rogue/late worker from resurrecting jobs); then delegate.
- `GetMixLabMapUseCase` ctor `(IMixLabMapRepository maps, IMixLabUploadRepository uploads)`: resolve `"latest"` like Request; job null → `NotFound`; else `Found` with the job and, when `Status == Succeeded`, the payload from `OpenPayloadAsync`.

- [ ] **Step 1: Write the failing tests** through the real `BlobMixLabMapRepository` + `FakeMixLabBlobGateway` (repo convention — no repository stubbing), with a hand-rolled `StubMixLabUploadRepository` (nested private class) for the upload-index dependency. Cover per use case: happy path, `"latest"` resolution, unknown upload, the Complete/Fail not-running guard, Get returning payload bytes only when succeeded.
- [ ] **Step 2:** Filtered run → FAIL. **Step 3:** Implement. **Step 4:** Filtered run → PASS, then full suite.
- [ ] **Step 5:** Commit — `feat(mixlab-maps): request/claim/complete/fail/get use cases`

---

### Task 4: Controller, DI, changelog, gate

**Files:**
- Create: `Changsta.Ai.Interface.Api/Controllers/MixLabMapsController.cs`
- Modify: `Changsta.Ai.Interface.Api/MixLab/MixLabServiceCollectionExtensions.cs`
- Modify: `CHANGELOG.md`
- Test: `Changsta.Ai.Tests.Unit/MixLab/MixLabMapsControllerTests.cs`

**Interfaces:**
- Produces the wire surface (all `[BearerSecret("MixLab:ApiSecret")]`, `[Route("api/mixlab")]`):
  - `POST maps` body `{uploadId}` (raw `JsonElement` parsing, `MixLabRunsController` style) → 202 job (camelCase) | 404 `UnknownUpload` | 404 `NoUploadsAvailable` (message distinguishes) | 400 missing uploadId.
  - `POST maps/claim` body `{workerId}` → 200 job | 204.
  - `POST maps/{uploadId}/result` — reads the raw request body to bytes (payload verbatim; `[RequestSizeLimit(32 * 1024 * 1024)]`) → 204 | 404 when the guard refuses.
  - `POST maps/{uploadId}/fail` body `{error}` → 204 | 404.
  - `GET maps/{uploadId}` → 200 `{job, payload}` where `payload` is the verbatim engine JSON embedded as raw JSON (write it with `JsonDocument.Parse(bytes)` → `RootElement.Clone()` so the response nests it unescaped), `payload: null` unless succeeded | 404.

- [ ] **Step 1: Write the failing tests** — mirror `MixLabUploadsControllerTests`: reflection assertions for `RouteAttribute`, `BearerSecretAttribute._configurationKey`, `RequestSizeLimitAttribute` on the result endpoint; nested Stub/Spy use-case doubles; status-code mapping per outcome; the GET test asserts the payload round-trips byte-meaning-identical (parse both sides, compare `JsonElement`s) and arrives nested unescaped.
- [ ] **Step 2:** Filtered run → FAIL. **Step 3:** Implement controller + DI lines under a `// Map use cases (Milestone B)` banner in `AddMixLabServices`, `AddScoped` each of the five use cases. Check `MixLabCorsTests.cs` — if controllers are enumerated there, add this one per its pattern.
- [ ] **Step 4:** `dotnet build --no-incremental` + full `dotnet test --no-build` → green, zero new warnings.
- [ ] **Step 5: Changelog** — new top `## v<next-minor>` section per house style: summary line ending with the breaking-or-not call-out ("New endpoints only; no changes to existing routes, DTOs, status codes or config."), `### Fixes`-style bolded bullets under a `### Added` heading if precedent exists, else the file's nearest equivalent; end with the test-count footer.
- [ ] **Step 6:** Commit — `feat(mixlab-maps): maps controller, DI wiring, changelog`

---

## Out of scope

Worker-side claiming/execution (MixLab repo — next plan) and the web overlay. The endpoints ship dormant until the worker learns to claim map jobs.
