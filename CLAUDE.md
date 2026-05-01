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

## Releasing a Version

When asked to "release this version":

1. Update `CHANGELOG.md`
   - Determine the next version number from the latest changelog entry and existing tags.
   - Add a new top entry for that version.
   - Summarise the current changes clearly and briefly.

2. Commit and push to `develop`
   - Run relevant verification first.
   - Commit with a conventional commit message.
   - Push `develop`.

3. Wait for CI
   - Get the successful CI Build run for the pushed `develop` commit.
   - Use that CI build SHA for deployment.

4. Merge to `main`
   - Update local `main`.
   - Merge `develop` into `main`.
   - Push `main`.

5. Create and push a version tag
   - Create an annotated tag for the new version.
   - Push the tag.

6. Deploy to QA and Prod
   - Trigger both GitHub Actions deploy workflows using the successful CI build SHA:
     `gh workflow run "Deploy QA (manual)" --repo christophechang/soundcloud-ai-mix-recommender-api --field ref=<sha>`
     `gh workflow run "Deploy Prod (manual)" --repo christophechang/soundcloud-ai-mix-recommender-api --field ref=<sha>`

7. Create the GitHub release
   - Create a GitHub release for the pushed tag.
   - Use the changelog entry for the release notes.

For direct requests to deploy only QA, Prod, or both without saying "release this version", use the latest successful CI Build SHA and trigger only the requested deployment workflow(s).
