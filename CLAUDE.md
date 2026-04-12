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