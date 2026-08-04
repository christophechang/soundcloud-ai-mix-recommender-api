# Changsta Radio Stations API Handover

## Purpose

This handover defines the API-side requirements for Changsta Radio stations.

IMPORTANT : You must use SOLID and DRY pricinciples using clean architecture. You must also must review this against the current archetecture to see if you can find any risks.

The API owns all radio station business logic. The frontend should be treated as a dumb consumer of API data. The frontend must not duplicate, infer, or reimplement scheduling rules.

This handover covers the API only.

Frontend work should happen later, after the API behaviour and response contract are agreed.

---

## Core product goal

Changsta Radio should feel like a set of real underground radio stations.

The API should provide a shared daily radio schedule where all users see the same scheduled mix for the same station and hour.

The user experience is not personalised per session. The API decides the schedule. The frontend displays it.

Each station has one featured mix per hour.

If a user is already playing a mix when the hour changes, playback should not be interrupted. The API only needs to provide the currently scheduled content for each station and hour. The frontend can decide how to display that a new mix is available.

---

## API responsibility boundary

The API is responsible for:

```text
Station definitions
Station metadata
Station genre ownership
Default station selection
Daily schedule generation
Hourly station slot selection
Repeat avoidance
Cooldown rules
Daypart suitability
Energy, mood and BPM scoring
Genre clustering avoidance
Artist/title repetition avoidance
Fallback behaviour
Schedule validation
Audit/debug explanation data
Serving a stable response contract to the frontend
```

The API is not responsible for:

```text
Frontend layout
Frontend animation
Frontend station selector UI
User preference storage in the browser
Playback interruption behaviour
Visual tuner design
```

The frontend must receive enough data from the API to render the current station experience without applying business rules.

---

## Stations

The API must support three editorial stations.

### Station 1: Touchdown FM

This is the flagship/default station.

Musical group:

```text
UK Bass / Garage / Breaks
```

Genres:

```text
UK Bass
Breakbeat
UKG
Hip-Hop
Hardcore
```

Identity:

```text
UK underground, broken rhythm, bass pressure, garage, breaks, hip-hop, hardcore, soundsystem energy.
```

Branding notes:

```text
Station name: Touchdown FM
Frequency: to be selected, should feel like a plausible UK pirate or FM radio frequency around the 100 FM area.
```

Touchdown FM must be the default station when no user preference exists.

### Station 2: House & Electronica station

Final station name is not locked yet.

Genres:

```text
House
Deep-House
Electronica
Techno
Disco
Funk
```

Identity:

```text
Deep, musical, melodic, warm, hypnotic, club-facing at the right time of day.
```

Important notes:

```text
Techno belongs in this station.
Techno must not automatically mean peak time.
There are only one or two Techno mixes, so the scheduler must not repeatedly over-select them.
```

### Station 3: Jungle & D&B station

Final station name is not locked yet.

Genres:

```text
Jungle
DNB
```

Identity:

```text
Jungle, drum and bass, rave energy, rollers, breaks, atmospherics, high-energy movement.
```

---

## Radio branding and frequencies

Each station must have:

```text
A station id
A display name
A fictional FM frequency
A short description
A longer identity or flavour description where useful
```

Branding should be inspired by classic UK underground and pirate radio culture, but must not copy real station names or trademarks.

Examples of inspiration:

```text
Rinse FM
Kool FM
Kiss FM
Flex FM
London pirate radio culture
```

The station names should be parody, homage, or original naming only.

Touchdown FM is locked for the UK Bass / Garage / Breaks station.

The other two station names and all station frequencies should be easy to change later.

---

## Schedule model

The API must provide:

```text
3 stations
24 hourly slots per station
72 scheduled station slots per day
```

Each station has one scheduled mix per hour.

All users should receive the same scheduled mix for the same station and hour.

The schedule should be generated or served as a shared station schedule, not chosen independently per request or per user session.

The API should support the current product behaviour:

```text
At the hour boundary, a new scheduled mix becomes available.
The currently playing mix is not forcibly cut off.
```

The API does not need to manage playback. It only needs to expose the current and scheduled station data clearly enough for the frontend to render the experience.

---

## Metadata available for scheduling

The scheduler must constrain itself to existing mix metadata.

Known metadata includes:

```text
genre
bpm
moodWeight
energy
artist / track or artist / title metadata
```

Energy exists as text. The exact values must be discovered from the existing data or code.

Do not assume the values are definitely:

```text
low
mid
peak
```

Requirements:

```text
Energy values must be discovered from existing data or code.
Unknown or unexpected energy values must not break scheduling.
Unknown energy should be treated neutrally.
Unknown energy should be surfaced in audit or debug output.
```

Do not invent new metadata categories unless they already exist.

---

## Genre ownership

For v1, each genre belongs to exactly one station.

There should be no automatic crossover between stations in v1.

Station mapping:

### Touchdown FM

```text
UK Bass
Breakbeat
UKG
Hip-Hop
Hardcore
```

### House & Electronica station

```text
House
Deep-House
Electronica
Techno
Disco
Funk
```

### Jungle & D&B station

```text
Jungle
DNB
```

A mix is station-eligible based on its genre.

Genre determines station eligibility. Genre must not strongly determine time-of-day placement.

Examples:

```text
Techno must not automatically mean peak time.
DNB must not automatically mean peak time.
House must not automatically mean daytime.
```

Daypart suitability should be driven primarily by energy, then moodWeight, then BPM.

---

## Default station

The API must identify Touchdown FM as the default station.

Requirement:

```text
Default station: Touchdown FM
```

This default should be visible in the API response contract so the frontend does not have to infer it.

If user preference is handled by the frontend, the frontend may override the default locally. The API should still expose the default station clearly.

---

## Daypart behaviour

The scheduler should prefer a natural daily energy curve.

Broad daypart direction:

```text
Earlier day: lower or mid energy, warmer, slower relative to station
Daytime: balanced, accessible, mid energy
Evening/peak: higher energy, stronger selections, faster relative to station
Late night: darker, deeper, hypnotic, not necessarily lower energy
Overnight: less harsh, more atmospheric, darker or deeper
```

Claude Code should inspect whether the existing API already has daypart concepts or time-based radio rules.

If suitable daypart concepts already exist, align with them.

If no suitable model exists, propose a simple daypart model before implementation.

The daypart model must be station-relative.

Examples:

```text
A slower daytime Jungle/D&B selection may still have a much higher BPM than a peak-time House selection.
A House mix can be peak because of energy and mood, not because it is the absolute fastest mix.
```

---

## Scoring principles

The scheduler should use soft scoring, not rigid deterministic selection.

Selection should prioritise:

```text
1. Station identity fit
2. Daypart energy fit
3. Daypart mood fit
4. Daypart BPM fit
5. Freshness and repeat avoidance
6. Underplayed mix preference
7. Flow and variety
8. Artist/title repetition avoidance
```

Energy should be the strongest daypart signal.

MoodWeight should influence warmth/darkness.

BPM should influence relative tempo, but should not dominate.

Genre should mainly decide station eligibility and lightly support variety within a station.

The API should not always pick the highest-scoring mix if that creates repetitive behaviour. A controlled, auditable, weighted choice from strong candidates is preferred where appropriate.

---

## Quality versus fairness

The scheduler should prioritise perceived station quality over perfect mathematical fairness.

Do not make weak or badly fitting mixes appear equally often only to satisfy fairness.

However, the scheduler must avoid the same perfect-fit mixes dominating the station.

Goal:

```text
Best-feeling station first.
Fair rotation second.
No obvious repetition.
No dead slots.
```

---

## Repeat and cooldown requirements

Claude Code should inspect the existing data model and current behaviour, then propose the exact cooldown rules.

Business requirements:

```text
A mix must not repeat on the same station on the same day.
A station should never return an empty slot.
Cooldown and repeat rules must reduce obvious repetition across days.
Cooldown should be strong enough to avoid fatigue but not so strict that smaller stations fail to fill schedules.
```

Important locked rule:

```text
Same mix twice on the same station on the same day is not allowed.
```

If the catalogue cannot satisfy a perfect schedule, the API must still return a full schedule, but any compromised rule must be clearly reported in audit or validation output.

---

## Same-hour cross-station rule

The same mix should not appear in the same hour across different stations if avoidable.

Example to avoid:

```text
10:00 Touchdown FM: Mix A
10:00 House & Electronica: Mix A
```

This matters because a user switching stations at the same time should not see the same mix.

This should be a strong rule, but not one that prevents a complete schedule.

If unavoidable, the conflict must be auditable.

Note: because v1 uses strict genre ownership, same-hour cross-station duplicates should normally be impossible unless the existing data or station eligibility rules allow a mix to appear in more than one station.

---

## Genre balancing inside stations

Genre balancing should be soft.

Do not force artificial equal distribution.

For example:

```text
House will naturally dominate the House & Electronica station because there are more House mixes.
DNB will naturally dominate the Jungle & D&B station because there are more DNB mixes than Jungle mixes.
```

The scheduler should discourage obvious clustering, not enforce rigid genre quotas.

Requirements:

```text
Avoid too many consecutive mixes from the same genre where alternatives exist.
Apply soft penalties for recent same-genre selections.
Do not overuse tiny genres for the sake of balance.
Do not let Techno become over-selected because the scheduler is trying to create variety.
```

Small genres should add colour, not become a scheduling burden.

---

## Artist/title repetition

Use artist/title metadata only to reduce perceived repetition.

Requirements:

```text
Avoid scheduling the same artist too close together where alternatives exist.
Avoid exact same title/track repetition.
Treat artist/title repetition as a soft rule unless the exact same mix is involved.
```

Do not overfit this rule, because DJ mixes may contain many artists and the metadata may represent the uploaded mix rather than every track inside it.

---

## Fallback behaviour

The API must always produce a full schedule.

No empty station slots.

If constraints cannot all be satisfied, relax rules in a controlled order.

Suggested fallback order:

```text
1. Relax BPM fit
2. Relax mood fit
3. Relax energy fit
4. Relax genre clustering penalty
5. Relax artist/title repetition penalty
6. Relax same-hour cross-station avoidance
7. Relax cooldown/repeat rules only as an absolute last resort
```

Same-station same-day repeat must remain forbidden unless the existing catalogue literally makes a full schedule impossible.

If that happens, it must be clearly reported.

---

## Validation requirements

After generating a schedule, validate it as a complete schedule, not only as individual slots.

The validation pass should check:

```text
Every station has 24 slots.
Every slot has a scheduled mix.
Every scheduled mix belongs to the correct station genre mapping.
No same mix appears twice on the same station on the same day.
Same-hour cross-station duplicates are avoided where possible.
Daypart fit is broadly respected.
Tiny genres are not over-selected.
No genre clustering looks obviously broken.
No artist/title clustering looks obviously broken.
Any fallback or rule relaxation is recorded.
```

Validation should produce useful diagnostic output.

---

## Audit and explanation requirements

Each scheduled slot should be explainable.

For each selected mix, expose enough information to understand why it was selected.

Audit output should include, conceptually:

```text
station
hour
mix id/title
genre
bpm
moodWeight
energy
score or ranking information
positive reasons
penalties applied
fallbacks or relaxed rules
warnings
```

Useful example reasons:

```text
Good fit for peak-time energy.
Strong station genre match.
Not recently scheduled.
Underplayed compared with similar station candidates.
Selected despite weaker mood fit because better candidates were on cooldown.
Unknown energy value treated as neutral.
```

The audit does not need to be public user-facing output, but it must be available for debugging and test assertions.

---

## API response contract requirements

The API response should provide enough information for the frontend to render the radio tuner and current station state without applying scheduling logic.

The contract should include, conceptually:

```text
generatedAtUtc
scheduleDate
timezone
defaultStationId
currentHour
stations
```

Each station should include:

```text
station id
display name
frequency
short description
genre group or flavour text
is default flag
current slot
today's slots where needed
```

Each current slot should include:

```text
hour
scheduled mix
whether this is the current hour
```

Each mix should include the fields already needed by the existing frontend plus the radio metadata needed for display.

Conceptual shape only:

```json
{
  "generatedAtUtc": "2026-05-21T00:05:00Z",
  "scheduleDate": "2026-05-21",
  "timezone": "Europe/London",
  "currentHour": 14,
  "defaultStationId": "touchdown-fm",
  "stations": [
    {
      "id": "touchdown-fm",
      "name": "Touchdown FM",
      "frequency": "100.x FM",
      "description": "UK Bass, Garage, Breaks, Hip-Hop and Hardcore",
      "isDefault": true,
      "currentSlot": {
        "hour": 14,
        "mix": {
          "id": "...",
          "title": "...",
          "artist": "...",
          "genre": "UK Bass",
          "bpm": 138,
          "moodWeight": -0.2,
          "energy": "mid"
        }
      }
    }
  ]
}
```

Do not treat this JSON as final. Claude Code should align the final contract with existing API conventions.

Contract principle:

```text
The frontend receives station metadata, current scheduled mixes and enough display information.
The frontend does not infer scheduling, dayparts, cooldowns, station mapping or default station rules.
```

---

## Unit test requirements

Write business-rule-level unit tests.

Each rule should be independently testable.

### Station mapping tests

Verify:

```text
House maps to House & Electronica.
Deep-House maps to House & Electronica.
Electronica maps to House & Electronica.
Techno maps to House & Electronica.
Disco maps to House & Electronica.
Funk maps to House & Electronica.

UK Bass maps to Touchdown FM.
Breakbeat maps to Touchdown FM.
UKG maps to Touchdown FM.
Hip-Hop maps to Touchdown FM.
Hardcore maps to Touchdown FM.

Jungle maps to Jungle & D&B.
DNB maps to Jungle & D&B.
```

### Default station tests

Verify:

```text
Touchdown FM is identified as the default station.
The API response exposes the default station id.
```

### Energy handling tests

Verify:

```text
Known energy values are mapped correctly after discovery.
Unknown energy values do not throw errors.
Unknown energy values are treated neutrally.
Unknown energy values produce an audit warning.
```

### Daypart scoring tests

Verify:

```text
Lower/mid energy is preferred earlier in the day.
Peak/higher energy is preferred around peak time.
Late-night selection can prefer darker/deeper mixes.
BPM influence is relative to station.
Energy has more influence than BPM.
```

### Cooldown and repetition tests

Verify:

```text
Same mix cannot appear twice on the same station in the same day.
Recently played mixes are penalised or excluded according to the selected cooldown rules.
Cooldown behaviour does not prevent a full schedule being generated.
Small station pools still produce complete schedules.
```

### Same-hour cross-station tests

Verify:

```text
The same mix is not scheduled in the same hour on multiple stations where alternatives exist.
If unavoidable, the conflict is audited.
```

### Genre balancing tests

Verify:

```text
Same-genre clustering is penalised.
Tiny genres are not over-selected for balance.
Techno does not repeatedly occupy peak slots simply because it is Techno.
House can dominate House & Electronica naturally without being treated as a failure.
DNB can dominate Jungle & D&B naturally without being treated as a failure.
```

### Artist/title repetition tests

Verify:

```text
Same artist/title close together receives a penalty.
Exact same mix repetition is handled more strictly than artist repetition.
Artist/title penalties do not prevent schedule completion.
```

### Fallback tests

Verify:

```text
Fallback rules are applied in the correct order.
The scheduler always returns 24 slots per station.
No slot is empty.
Relaxed rules are audited.
Same-station same-day duplicate remains forbidden unless absolutely unavoidable.
```

### Validation tests

Verify:

```text
A valid schedule passes validation.
A schedule with missing slots fails validation.
A schedule with incorrect genre/station mapping fails validation.
A schedule with same-station same-day duplicates fails validation.
A schedule with avoidable same-hour cross-station duplicates is flagged.
```

### Audit tests

Verify:

```text
Each scheduled slot includes explanation data.
Penalties are visible.
Fallbacks are visible.
Unknown metadata warnings are visible.
```

### Contract tests

Verify:

```text
The API response includes all three stations.
The API response identifies Touchdown FM as default.
The API response includes station id, name, frequency and description.
The API response includes the current scheduled slot for each station.
The frontend does not need to infer station mapping from genres.
```

---

## Non-goals for this API change

Do not build personal per-user scheduling.

Do not create five variant streams.

Do not create a separate Hip-Hop station.

Do not add automatic genre crossover in v1.

Do not make genre stereotypes drive time-of-day scheduling.

Do not assume energy values without checking the actual data.

Do not optimise for perfect fairness at the expense of station quality.

Do not define frontend visual design.

Do not make the frontend responsible for scheduling rules.

Do not interrupt user playback at the hour boundary.

---

## Final API acceptance criteria

The API change is successful when:

```text
There are three editorial stations.
Touchdown FM is the flagship/default station.
Each station has 24 scheduled hourly mixes.
All users receive the same scheduled mix for the same station/hour.
Every scheduled mix belongs to the correct station.
Energy, moodWeight and BPM influence daypart selection.
Same-station same-day repeats are prevented.
Same-hour cross-station duplicates are avoided where possible.
The API always produces a complete schedule.
Fallbacks are controlled and auditable.
The scheduler feels varied without becoming random noise.
The scheduler favours perceived station quality over perfect mathematical fairness.
The API exposes a stable response contract for the frontend.
The frontend does not need to know or implement scheduling rules.
Each business rule has meaningful unit test coverage.
```
