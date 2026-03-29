# Claude.md — Coding Rules & Workflow

## Purpose
- Use this file as the Claude-specific working guide for this repository.
- Treat `AGENTS.md` and the codebase itself as the primary source for repo shape and task boundaries.

## Commits
- Use conventional commit messages.
- Do NOT add `Co-Authored-By` trailers to commit messages.

## Project Shape
- `Changsta.Ai.Interface.Api/`: API entrypoint, controllers, middleware, DI, app configuration.
- `Changsta.Ai.Core/`: domain models and DTOs.
- `Changsta.Ai.Core.Contracts/`: interfaces that define boundaries between layers.
- `Changsta.Ai.Core.BusinessProcesses/`: orchestration and use case logic.
- `Changsta.Ai.Infrastructure.Services.Ai/`: OpenAI integration, prompt building, validation, retry logic.
- `Changsta.Ai.Infrastructure.Services.SoundCloud/`: RSS ingestion and parsing.
- `Changsta.Ai.Infrastructure.Services.Azure/`: blob catalogue persistence and Azure wiring.
- `Changsta.Ai.Tests.Unit/`: NUnit and FluentAssertions tests.
- `infra/`: Bicep deployment templates and parameters.

## Code Conventions
- `Soltech.ruleset` is the source of truth for StyleCop and Microsoft CA analyzer behaviour.
- Do not duplicate or override analyzer intent in code unless there is a clear repo-established exception.
- Fix StyleCop build errors instead of suppressing them unless there is explicit justification.
- Follow the style already present in the touched file or project:
- use built-in aliases such as `string` and `int`
- keep private fields as `_camelCase`
- preserve existing namespace style for the local file
- preserve current async and `ConfigureAwait(false)` patterns where already established
- avoid unnecessary formatting churn

## Architectural Expectations
- Keep controllers thin and push orchestration into use cases or services.
- Keep `Core.Contracts` free from infrastructure details.
- Keep infrastructure-specific code inside infrastructure projects.
- Preserve the separation between AI-generated `reason` text and server-validated `why` evidence.
- Treat API routes, DTO property names, config keys, and infra naming patterns as stable contracts unless the task explicitly requires changing them.

## Build & Test Workflow

### Commands
```bash
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build
```

### Rules
1. Run relevant tests after every change.
2. Run the full test suite when changes affect shared orchestration, validation, parsing, contracts, or DI wiring.
3. New behaviour should come with new or updated unit tests.
4. Keep tests deterministic and avoid live OpenAI, SoundCloud, or Azure dependencies.
5. Prefer existing test patterns and helpers in `Changsta.Ai.Tests.Unit/`.

## Additional Constraints
- `InternalsVisibleTo("Changsta.Ai.Tests.Unit")` is configured in the AI project. Prefer `internal` helpers for logic that only needs test visibility.
- Do not add XML doc comments to untouched code unless an analyzer requires it.
- Do not change secrets, connection strings, or environment-specific values in tracked files.
