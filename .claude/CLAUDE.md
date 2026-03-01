# Claude.md — Coding Rules & Workflow

## Code Conventions

**`Soltech.ruleset` is the source of truth for all code style and quality rules.**
Do not deviate from rules defined there. The ruleset enforces StyleCop.Analyzers and Microsoft CA rules at build time.

### Key enforced rules (Action="Error" — build will fail)

#### Spacing & Punctuation (SA1001–SA1028)
- SA1001: Commas must be followed by a space
- SA1002: Semicolons must be followed by a space
- SA1003: Symbols must be spaced correctly
- SA1008: Opening parenthesis must not be preceded by a space
- SA1009: Closing parenthesis must be spaced correctly
- SA1012/SA1013: Opening/closing brace must be spaced correctly
- SA1025: Code must not contain multiple whitespace in a row
- SA1027: Use tabs/spaces correctly (no mixed indentation)
- SA1028: No trailing whitespace

#### Readability (SA1100–SA1137)
- SA1121: Use built-in type aliases (`string` not `String`, `int` not `Int32`)
- SA1122: Use `string.Empty` not `""`
- SA1129: Do not use `new` keyword for default value expressions
- SA1131: Use readable conditions (literals on right side)
- SA1135: Using directives must be qualified
- SA1136: Enum values must be on separate lines

#### Ordering (SA1200–SA1217)
- SA1200: Using directives placement — disabled (`None`), can be inside or outside namespace
- SA1201: Elements must appear in correct order (fields, constructors, properties, methods)
- SA1202: Elements must be ordered by access (public before private)
- SA1203: Constants before fields
- SA1204: Static elements before instance elements
- SA1208: System using directives must be first, then alphabetical
- SA1210: Using directives must be ordered alphabetically — **System.* first, then Changsta.* before Microsoft.***

#### Naming (SA1300–SA1313)
- SA1300: Element names must begin with upper-case letter
- SA1303/SA1304: Constants/readonly fields must be Pascal case
- SA1306: Field names must begin with lower-case letter
- SA1309: Field names must not begin with underscore — disabled (`None`); use `_camelCase` for private fields (SX1309 enforces underscore prefix instead)
- SX1309: Private fields must begin with underscore (`_fieldName`)

#### Access Modifiers (SA1400–SA1413)
- SA1400: Access modifiers must be declared
- SA1401: Fields must be private
- SA1402: File may only contain a single type
- SA1413: Use trailing comma in multi-line initializers

#### Layout (SA1500–SA1520)
- SA1500: Braces for multi-line statements must not share line
- SA1501: Statement must not be on a single line
- SA1503: Braces must not be omitted
- SA1513: Closing brace must be followed by blank line
- SA1516: Elements must be separated by blank line (Warning)

#### Documentation (SA1600–SA1651)
- SA1600: Element documentation required — disabled (`None`) for most elements
- SA1633: File header required — disabled (`None`)
- SA1642: Constructor summary documentation must begin with standard text
- SA1643: Destructor summary documentation must begin with standard text

### Disabled rules (Action="None" — explicitly off)
- SA1000, SA1011, SA1101, SA1200, SA1309, SA1600, SA1602, SA1633, SA1652

### CA rules (Action="Warning")
All Microsoft.Analyzers.ManagedCodeAnalysis rules (CA1001–CA2242) are set to Warning.
Treat warnings as signals to fix; do not suppress without cause.

---

## Build & Test Workflow

### Commands
```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build
```

### Rules
1. **Run unit tests after every change.** Tests must pass before any task is considered complete.
2. **New features require new unit tests.** Any added functionality must have corresponding test coverage.
3. **Changes to existing features require updated unit tests.** Modify tests to reflect changed behaviour; do not leave stale tests in place.
4. **Tests live in** `Changsta.Ai.Tests.Unit/` — specifically `Recommenders/OpenAiMixRecommenderValidationTests.cs` for AI validation logic.
5. **Test framework:** NUnit 4.3.2 + FluentAssertions.
6. **Build must produce zero warnings.** Fix all warnings before considering a task complete. Never leave warnings unresolved.
   - Use `Assert.That(...)` constraint model in NUnit tests — never `CollectionAssert` or other classic-model assertions (NUnit2049).
   - `AD0001` in the test project is suppressed via `<NoWarn>` in the csproj — this is a known StyleCop 1.1.118 bug with C# `record` declarations and is not a code issue.

---

## Additional Constraints
- StyleCop errors block the build — fix them, never suppress without explicit justification.
- `InternalsVisibleTo("Changsta.Ai.Tests.Unit")` is configured in the AI project — use `internal static` for methods that only need test access.
- Do not add XML doc comments to methods you did not author unless SA1600 or related rules require it.