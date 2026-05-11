# Automation Lifecycle

## 1. Overview

This document captures the intended operating model for production monitoring, production bug handling, QA deployment, production deployment approval, smoke testing, and release promotion for the SoundCloud AI Mix Recommender API.

The intended loop is:

1. The production log monitor checks live API behaviour using production logs and production endpoint evidence.
2. When it finds a production API problem, it creates a GitHub issue for the relevant 400 or 500 error.
3. The bug-fix worker picks up one eligible issue, creates a bugfix branch from `develop`, opens a PR into `develop`, and updates the issue.
4. Christophe reviews and merges the PR.
5. A post-merge worker detects merged PRs into `develop`.
6. QA deployment is triggered from the merged commit SHA using the existing `Deploy QA (manual)` workflow.
7. QA smoke tests run against the affected endpoint first, then a small regression sweep.
8. If QA passes, production deployment may be triggered from the same merged commit SHA.
9. Production deployment must pause at the GitHub `prod` environment approval gate until Christophe approves it.
10. After production deployment completes, production smoke tests run.
11. If production passes, the linked issue is closed.
12. Promotion to `main`, tagging, and GitHub release creation happen as a separate step after production verification.

## 2. Non-negotiable safety rules

- The agent must never merge pull requests.
- The agent must never push directly to `main` or `develop`.
- The agent must never force-push.
- The agent must never modify secrets, repo settings, branch protection, GitHub Actions settings, deployment settings, or Azure resources.
- The agent must never deploy production unless the GitHub `prod` environment approval gate exists.
- The agent must not perform automatic rollback.
- If production fails, the agent must alert Christophe, update or create issues with evidence, and stop.
- Human review remains required before code enters `develop`.
- Production deployment approval remains human-controlled.

## 3. Production monitor job

The production monitor checks production logs and other production endpoint evidence to detect real API failures. Its job is to surface production-facing 400 and 500 errors as actionable GitHub issues.

- It creates issues for qualifying 400 and 500 errors.
- Issues should include enough evidence for diagnosis, such as endpoint, timestamp, request context where safe, response details, exception summary, correlation data, or log excerpts.
- It should avoid creating duplicate noisy issues where possible, for example by checking for an existing open issue for the same failure shape.
- These monitor-created issues are the preferred input for the bug-fix worker.

## 4. Bug-fix worker

The bug-fix worker runs on a schedule and handles at most one eligible issue per run.

- It only targets issues created by the production monitor or issues clearly marked as production or API bugs.
- It branches from `develop`.
- It creates a dedicated bugfix branch.
- It makes the smallest safe fix that addresses the diagnosed problem.
- It runs the repo validation gate before opening a PR. Current repo validation commands are:
  - `dotnet restore soundcloud-ai-mix-recommender-api.sln`
  - `dotnet build soundcloud-ai-mix-recommender-api.sln --configuration Release --no-restore`
  - `dotnet test Changsta.Ai.Tests.Unit/Changsta.Ai.Tests.Unit.csproj --configuration Release --no-build --verbosity normal`
- If validation passes, it opens a PR into `develop`.
- It updates the issue with the suspected cause, fix summary, tests run, and PR link.
- If the fix is unclear or risky, it comments with findings, marks the issue `needs-human`, and stops.
- It must not make speculative code changes.

## 5. PR approval and merge

Approval alone is not enough for automation to continue.

- The agent should only continue after the PR has actually been merged into `develop`.
- The merged commit SHA is the source of truth for downstream deployment.
- If a PR is approved but not merged, the post-merge worker does nothing.

## 6. Post-merge deployment worker

This worker is responsible for moving an approved bug fix from merged code in `develop` into QA verification.

- It may poll GitHub.
- Polling is acceptable for this project.
- A low-frequency schedule is preferred, such as every 30 minutes.
- It looks for merged PRs into `develop`.
- It only handles PRs linked to production bug issues.
- It only handles PRs that have not already been processed for QA.
- It handles at most one merged PR per run.
- It uses issue comments or labels as idempotency markers.
- It triggers QA deployment from the merge commit SHA using the existing `Deploy QA (manual)` workflow.
- It waits for deployment completion.
- It runs targeted and regression smoke tests.
- It comments QA results back to the linked issue.

## 7. QA testing

QA testing should prove the fix first, then check for obvious regressions.

- First test the endpoint affected by the issue or PR.
- Then run a small regression smoke test across other important API endpoints.
- If QA fails:
  - update the linked issue with evidence
  - create new regression issues if unrelated endpoints fail
  - mark the issue `needs-human` or `qa-failed`
  - alert Christophe
  - stop
- No production deployment should happen if QA fails.

## 8. Production deployment

Production deployment must use the existing GitHub Actions workflow and remain human-gated.

- Production deployment is performed through the existing `Deploy Prod (manual)` GitHub Actions workflow.
- The prod workflow must use the GitHub `prod` environment.
- The `prod` environment must require Christophe's approval.
- The agent may trigger the workflow only if that approval gate exists.
- GitHub Actions must pause at the prod gate until Christophe approves.
- The agent must wait for deployment completion before testing.

## 9. Production smoke testing

Production smoke testing happens only after the production deployment has fully completed.

- After production deployment completes, the agent tests the affected endpoint first.
- Then it runs regression smoke tests across other important API endpoints.
- If production fails:
  - no rollback
  - no retry deployment
  - no code change
  - no infrastructure change
  - update the issue with evidence
  - create regression issues if needed
  - mark the issue `prod-failed` or `needs-human`
  - alert Christophe
  - stop
- If production passes:
  - comment success
  - close the linked issue

## 10. Main, tags, and releases

`main` should represent verified production state.

- Promotion to `main`, tagging, and GitHub release creation are separate from the bug-fix worker.
- Preferred initial model:
  - after production smoke passes, create a promotion PR from `develop` to `main`, or use a clearly documented manual promotion step
  - tag and release only after production verification
- The agent must not push directly to `main` unless explicitly approved in a future workflow change.

This matches the repo's current release direction in `CLAUDE.md`, where release handling is a distinct flow involving `develop`, `main`, version tags, and GitHub releases.

## 11. Idempotency markers

The automation should avoid repeating the same stage for the same commit or linked production issue.

- It may use labels or issue comments such as:
  - `automation: in-progress`
  - `automation: pr-ready`
  - `automation: qa-deploy-started`
  - `automation: qa-passed`
  - `automation: qa-failed`
  - `automation: prod-deploy-started`
  - `automation: prod-passed`
  - `automation: prod-failed`
  - `automation: needs-human`
- Exact labels may follow existing repo conventions.

## 12. Current GitHub identity model

- Git SSH runs as `mr-robot-chang` using the `github-mr-robot` SSH alias.
- GitHub CLI and API access in the OpenClaw runtime run as `mr-robot-chang`.
- Repos are accessed with write permission.
- Christophe remains the human reviewer and production approver.

## 13. Current implementation status

Current known status based on this repo and current operating assumptions:

- The production monitor exists and creates issues.
- The bug-fix worker exists or is being introduced to create PRs into `develop`.
- A post-merge QA deployment worker is not yet implemented, unless confirmed elsewhere outside this repo.
- The production deployment gate must be confirmed as requiring Christophe's approval before the agent is allowed to trigger production.
- Automatic rollback is explicitly out of scope.

## Notes on Current Repo State

This document uses current repo terminology and workflow names:

- Primary integration branch: `develop`
- Production branch: `main`
- CI workflow: `CI Build (publish artifact)`
- QA deployment workflow: `Deploy QA (manual)`
- Production deployment workflow: `Deploy Prod (manual)`
- Current README deployment model: Dev auto-deploys from `develop`, while QA and Prod are manual

The document intentionally defines the operating model without over-specifying implementation details such as exact polling queries, exact smoke test payloads, or exact issue label automation, so those details can evolve without changing the safety model.
