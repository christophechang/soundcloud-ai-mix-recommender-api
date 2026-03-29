# AGENTS.md

Repo-level instructions for coding agents working in `soundcloud-ai-mix-recommender-api`.

## Purpose
- This repository is a layered .NET 10 Web API that recommends SoundCloud DJ mixes using catalogue data plus AI reasoning with strict server-side validation.
- Prefer small, low-risk changes that preserve the current API contracts, validation rules, and deployment shape.
- Optimise for correctness, traceability, and minimal diff size over cleverness.

## Working Style
- Inspect the codebase before changing code. Verify assumptions from the real repo instead of inferring architecture.
- Follow existing patterns in the touched project. Do not introduce new abstractions unless the current design clearly requires them.
- Keep changes tightly scoped to the requested task. Do not refactor unrelated areas.
- Do not add dependencies, new services, or infrastructure changes without explicit approval.
- For non-trivial changes, explain the plan first and wait for confirmation before editing files.

## Solution Shape
- `Changsta.Ai.Interface.Api/`: ASP.NET Core API entrypoint, DI, middleware, controllers, app settings.
- `Changsta.Ai.Core/`: domain models and DTOs.
- `Changsta.Ai.Core.Contracts/`: interfaces for AI, catalogue, and use case boundaries.
- `Changsta.Ai.Core.BusinessProcesses/`: orchestration and use case logic.
- `Changsta.Ai.Infrastructure.Services.Ai/`: OpenAI integration, prompt construction, response validation, retry logic.
- `Changsta.Ai.Infrastructure.Services.SoundCloud/`: RSS ingestion and tracklist parsing.
- `Changsta.Ai.Infrastructure.Services.Azure/`: blob-backed catalogue persistence and related configuration.
- `Changsta.Ai.Tests.Unit/`: NUnit and FluentAssertions unit tests.
- `infra/`: Bicep templates and environment parameters for Azure deployment.

## Architectural Guardrails
- Keep controllers thin. Request handling and orchestration belong in use cases or services, not controllers.
- Keep `Core.Contracts` free of infrastructure concerns.
- Keep `Core.BusinessProcesses` focused on orchestration and business rules, not transport or SDK details.
- Keep external integrations inside the infrastructure projects.
- Register new dependencies through existing DI patterns in `Program.cs` or project-level service extension methods.
- Preserve the current separation between creative AI output and server-validated evidence.

## Behaviour-Sensitive Areas
- Treat `OpenAiMixRecommender` validation, prompt rules, retry logic, and JSON contract enforcement as behaviour-sensitive. Make minimal, well-tested changes there.
- Treat SoundCloud RSS parsing and tracklist extraction as behaviour-sensitive because small parsing changes can alter catalogue quality.
- Treat API response DTOs and controller routes as public contracts. Do not change response shape, property names, or route structure unless explicitly requested.
- Treat rate limiting, CORS, health checks, and telemetry wiring in `Program.cs` as operational behaviour. Do not change them casually.
- Treat `infra/` changes as production-impacting. Keep environment names, app settings keys, and resource naming conventions stable unless the task requires a coordinated change.

## Code Standards
- `Soltech.ruleset` is the source of truth for analyzer behaviour and StyleCop enforcement.
- Build-breaking analyzer issues should be fixed, not suppressed, unless there is a clear repo-established exception.
- Follow the existing C# style in the touched file:
- file-scoped vs block namespaces should match the local project/file pattern
- use the built-in type aliases such as `string` and `int`
- keep private fields as `_camelCase`
- preserve existing async and `ConfigureAwait(false)` usage patterns where already present
- avoid broad refactors or stylistic churn
- Do not add XML documentation to untouched code just for consistency unless a rule requires it.

## Testing And Verification
- Preferred verification commands:
- `dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental`
- `dotnet test soundcloud-ai-mix-recommender-api.sln --no-build`
- Run relevant tests after changes. Run the full test suite when changes affect shared orchestration, validation, parsing, contracts, or DI wiring.
- Add or update unit tests when behaviour changes.
- Keep tests deterministic. Do not depend on real OpenAI, SoundCloud, Azure, or other live services in unit tests.
- Use existing test helpers and fixture patterns in `Changsta.Ai.Tests.Unit/`.

## Configuration And Secrets
- Do not commit secrets, keys, connection strings, or environment-specific values.
- Preserve existing configuration names such as `OpenAI:*`, `SoundCloud:*`, `Azure:BlobCatalog:*`, `ApplicationInsights:*`, and `RateLimiting:*` unless the task explicitly requires coordinated changes.
- Prefer user-secrets, local settings, or deployment configuration over hardcoded values.

## Documentation
- Keep documentation aligned with the current repo state.
- When updating docs, prefer concrete commands, file paths, and project names that exist in this repository.
- If behaviour changes, update README or related docs when user-facing setup, API behaviour, or deployment steps are affected.

## Commits
- Use conventional commit messages.
- Do not add `Co-Authored-By` trailers.
- Keep commits focused on the requested work only.
