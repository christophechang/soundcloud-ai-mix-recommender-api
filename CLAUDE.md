Project layout:
- `Changsta.Ai.Interface.Api/` — controllers, middleware, DI, app config
- `Changsta.Ai.Core/` — domain models and DTOs
- `Changsta.Ai.Core.Contracts/` — layer boundary interfaces
- `Changsta.Ai.Core.BusinessProcesses/` — orchestration and use case logic
- `Changsta.Ai.Infrastructure.Services.Ai/` — OpenAI integration, prompt building, retry
- `Changsta.Ai.Infrastructure.Services.SoundCloud/` — RSS ingestion and parsing
- `Changsta.Ai.Infrastructure.Services.Azure/` — blob persistence and Azure wiring
- `Changsta.Ai.Tests.Unit/` — NUnit + FluentAssertions
- `infra/` — Bicep templates

`Soltech.ruleset` is source of truth for StyleCop and CA analyzers. Fix build errors instead of suppressing. Match style in the file being touched: `string`/`int` aliases, `_camelCase` private fields, existing namespace style, existing `ConfigureAwait(false)` patterns.

Controllers stay thin — push orchestration into use cases. Keep `Core.Contracts` free of infrastructure details. Keep infrastructure code inside infrastructure projects. Preserve the separation between AI-generated `reason` text and server-validated `why` evidence. Treat API routes, DTO names, config keys, and infra naming as stable contracts unless the task requires changing them.

`InternalsVisibleTo("Changsta.Ai.Tests.Unit")` is set in the AI project — prefer `internal` for test-only helpers. No XML doc comments on untouched code unless an analyzer requires it. No changes to secrets, connection strings, or env-specific values in tracked files.

Tests must be deterministic — no live OpenAI, SoundCloud, or Azure calls. Run relevant tests after every change; run the full suite when touching shared orchestration, validation, contracts, or DI wiring. New behaviour needs new or updated tests. Follow existing patterns in `Changsta.Ai.Tests.Unit/`.

dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build

## Dependency Updates

Dependabot (`.github/dependabot.yml`) opens weekly PRs for NuGet and GitHub Actions, grouped
(Microsoft.Extensions.*, Azure.*, OpenTelemetry.*, test deps). To react to a Dependabot PR: let CI
run (it builds, tests, and runs `dotnet list package --vulnerable` which fails on known
advisories), review the changelog for breaking changes, then merge into `develop` like any other
PR. Security-advisory bumps take priority. The CI vulnerability audit also fails any PR that
introduces a vulnerable transitive package, so keep transitive pins current.

## Releasing a version

When the operator says **"deploy release"** (or the legacy phrase **"release this version"**), run the
user-level `deploy-release` skill (changelog → verify → push `develop` → CI → merge `main` → annotated tag →
deploy → GitHub release → back to `develop`). Repo specifics:

- Wait for the successful `develop` CI Build and capture its commit SHA — deployments are pinned to that SHA.
- Deploy QA and Prod via both manual workflows:
  `gh workflow run "Deploy QA (manual)" --repo christophechang/soundcloud-ai-mix-recommender-api --field ref=<sha>`
  `gh workflow run "Deploy Prod (manual)" --repo christophechang/soundcloud-ai-mix-recommender-api --field ref=<sha>`

For direct requests to deploy only QA, Prod, or both without a release, use the latest successful CI Build
SHA and trigger only the requested deployment workflow(s).
