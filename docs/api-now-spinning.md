# API Contract: GET /api/catalog/now-spinning

## Purpose

Returns one pre-scored mix for the current time slot plus a schedule of upcoming slots. All scoring and slot assignment happens at ingest time and is aggressively cached. The endpoint does a pool lookup and a seeded draw — no scoring at request time.

---

## Endpoint

```
GET /api/catalog/now-spinning
```

### Query Parameters

| Param | Type | Required | Description |
|---|---|---|---|
| `utcOffsetMinutes` | integer | No | Client's UTC offset in minutes, **positive = east of UTC**. Client sends `-new Date().getTimezoneOffset()`. Defaults to `0` (UTC) if omitted. |
| `moodLean` | string | No | `darker` \| `warmer` \| `slower` \| `faster` |
| `skip` | string | No | Comma-separated mix IDs to exclude from the pick |
| `schedule` | integer | No | Number of upcoming hourly slots to include. Default `4`. |

> **Note on `utcOffsetMinutes` sign:** JS's `Date.prototype.getTimezoneOffset()` returns `local − UTC`, so UTC+5 returns `-300`. The client must negate: `-new Date().getTimezoneOffset()`. The server interprets positive values as east (ahead) of UTC.

### Deriving local hour

```
localHour = floor(utcNow + utcOffsetMinutes) mod 24
```

The server resolves slot and day-of-week from this local hour.

---

### Response

```json
{
  "now": "2026-05-16T22:14:00Z",
  "dayBucket": "friday",
  "slot": {
    "key": "primetime",
    "label": "primetime"
  },
  "mix": {
    "id": "terminal-velocity",
    "title": "TERMINAL VELOCITY",
    "url": "https://soundcloud.com/changsta/terminal-velocity",
    "genre": "Breaks",
    "energy": "peak",
    "bpm": 137,
    "moods": ["driving", "rave", "heavy"],
    "publishedAt": "2026-01-15T00:00:00Z",
    "duration": 1878
  },
  "schedule": [
    {
      "at": "2026-05-16T23:00:00Z",
      "slot": { "key": "primetime", "label": "primetime" },
      "dayBucket": "friday",
      "mix": { "id": "...", "title": "...", "url": "...", "genre": "...", "energy": "...", "bpm": 137, "moods": [], "publishedAt": "...", "duration": 3600 }
    },
    {
      "at": "2026-05-17T00:00:00Z",
      "slot": { "key": "dead", "label": "dead of night" },
      "dayBucket": "saturday",
      "mix": { "id": "...", "title": "...", "url": "...", "genre": "...", "energy": "...", "bpm": 172, "moods": [], "publishedAt": "...", "duration": 3600 }
    }
  ]
}
```

**Field notes:**
- `mix.bpm` — `round((BpmMin + BpmMax) / 2)`. **Omitted** if both are null.
- `mix.duration` — integer seconds, parsed from stored `HH:MM:SS`. **Omitted** if null.
- `dayBucket` — one of `weeknight | friday | saturday | sunday`. Present at response root and on each `schedule[]` entry. Client reads this; never re-derives it from `now`.
- `schedule[].at` — floored to the hour, in UTC.
- The current `now` mix is never repeated in `schedule`.

---

## Error / Fallback Responses

| Condition | Status | Body |
|---|---|---|
| Invalid `moodLean` value | 400 | `{ "error": "invalid moodLean" }` |
| `skip` exhausts pool (lean still matches) | 200 | Return best available ignoring skip, add `"skipsIgnored": true` |
| `moodLean` exhausts pool (no mixes tagged with that lean) | 200 | Return best available ignoring lean, add `"leanIgnored": true` |
| Both lean and skip exhaust pool | 200 | Return best available ignoring both, add `"leanIgnored": true, "skipsIgnored": true` |
| Slot pool empty after adjacent-slot fallback | 503 | `{ "error": "no mixes available" }` |

When the slot pool is empty (no mixes assigned at ingest), the server tries the adjacent slot and adds `"poolFallback": true` to the response. If the adjacent slot is also empty, return 503.

---

## Slot Definitions

Six non-overlapping windows based on local hour (derived from `utcOffsetMinutes`). Boundaries use half-open intervals `[start, end)`.

| Key | Hours | Label |
|---|---|---|
| `dead` | `[0, 4)` | dead of night |
| `comedown` | `[4, 8)` | comedown |
| `morning` | `[8, 12)` | morning |
| `afternoon` | `[12, 17)` | afternoon |
| `earlyeve` | `[17, 21)` | evening |
| `primetime` | `[21, 24)` | primetime |

---

## Seeded Draw (Shared Programme)

Same hour + same slot + same skip state = same pick for every listener. This preserves the shared-broadcast metaphor.

**Seed:** `floor(utcNow, 1hr) + hash(sortedSkipIds)`

- `floor(utcNow, 1hr)` — Unix timestamp of the start of the current UTC hour in milliseconds
- `hash(sortedSkipIds)` — deterministic hash of the sorted skip ID list (empty list = 0)
- Skip one mix → skip list changes → seed changes → next candidate in the pool

Behaviour: user A with no skips gets candidate 1. User A skips once, gets candidate 2. User B with no skips also gets candidate 1. Rotation is deterministic within the hour.

---

## Day-of-Week Buckets

| Bucket | Days (JS `getDay()`) |
|---|---|
| `sunday` | 0 |
| `weeknight` | 1–4 |
| `friday` | 5 |
| `saturday` | 6 |

Day is derived from the listener's **local** date (after applying `utcOffsetMinutes`), not UTC date.

---

## Ingest: Slot Assignment

At ingest, score every mix against every slot and tag it with the slots it fits. Pre-build 6 slots × 4 day buckets = **24 pools**. Pools are invalidated on catalogue update only.

### Slot Scoring Weights

`Warmth` range: −1.0 (dark/cold) to +1.0 (warm/light). Mixes with null `Warmth` are treated as `0.0` for scoring.

`BPM` used for scoring: `round((BpmMin + BpmMax) / 2)`. Mixes where both are null are excluded from all pools.

| Slot | BPM target | Warmth target | Matching energy values |
|---|---|---|---|
| `dead` | 172 | −0.6 | `peak`, `high` |
| `comedown` | 110 | +0.4 | `chilled`, `low-mid`, `low` |
| `morning` | 122 | +0.5 | `low-mid`, `mid`, `chilled`, `low` |
| `afternoon` | 125 | +0.3 | `mid`, `journey` |
| `earlyeve` | 128 | 0.0 | `mid`, `mid-peak`, `journey`, `mid-high` |
| `primetime` | 138 | −0.3 | `peak`, `high`, `mid-peak`, `mid-high` |

**BPM score:** `max(0, 8 − |mix.bpm − targetBpm| / 6)` → 0–8 points

**Warmth score:** `max(0, 4 − |mix.warmth − targetWarmth| / 0.25)` → 0–4 points

**Energy score:** `+5` if `mix.energy` exactly matches any value in the slot's energy list

> **Warning:** Energy matching is exact string equality. Verify that the values used in mix metadata (`Mix.Energy`) match the strings in this table exactly. Mismatches silently score 0 for energy.

**Total:** BPM + warmth + energy. **Tag mix to slot if total ≥ slot minimum threshold.**

Threshold: top 40% of scores per slot, with a hard floor of ≥ 3 points. This ensures small catalogues don't assign garbage to pools.

A mix can be tagged to multiple slots.

### Day-of-Week BPM Adjustment

Apply to `targetBpm` when computing per-pool tags. This means a mix may appear in some day buckets of a slot but not others.

| Bucket | BPM adjustment |
|---|---|
| `sunday` | −10 |
| `weeknight` | 0 |
| `friday` | +4 |
| `saturday` | +8 |

---

## Ingest: Mood Lean Tags

At ingest, tag each mix with which mood leans it supports. `slower` and `faster` are relative to the slot's BPM target (after day-of-week adjustment), so they are tagged **per slot per day bucket**, not globally.

| Lean | Tag condition |
|---|---|
| `darker` | `mix.warmth < −0.3` AND `mix.energy` in `[peak, high, mid-peak]` |
| `warmer` | `mix.warmth > 0.3` |
| `slower` | `mix.bpm < slot.targetBpm − 10` |
| `faster` | `mix.bpm > slot.targetBpm + 10` |

A mix can carry multiple lean tags. Example: a 120 BPM mix is `faster` in `comedown` (target 110) and `slower` in `primetime` (target 138).

---

## Request-Time Logic

1. Parse `utcOffsetMinutes` → derive local hour and local date → look up slot and day bucket
2. Pull pre-tagged pool for `(slot, dayBucket)`
3. If `moodLean` set → filter pool to mixes tagged with that lean
4. If `skip` set → exclude those IDs
5. If pool empty after filters:
   - If only skips caused exhaustion: ignore skip, set `skipsIgnored: true`
   - If lean caused exhaustion: ignore lean (and skip), set `leanIgnored: true` (and `skipsIgnored: true` if skip was set)
   - If slot pool itself is empty: try adjacent slot, set `poolFallback: true`; if also empty, return 503
6. **Seeded draw** — pick one mix using `seed = floor(utcNow, 1hr) + hash(sortedSkipIds)`. Same seed = same pick.
7. For `schedule`: for each of the next N hours floored to the hour, repeat steps 1–6 independently. Pre-seed skip tracking with the `now` mix ID to ensure it is never repeated in the schedule. Track schedule picks across slots to avoid intra-schedule repeats.

---

## Caching Strategy

- Ingest pools: pre-built and aggressively cached. Invalidate on catalogue update only.
- Endpoint response: **not cached at CDN edge**. Response varies by `utcOffsetMinutes`, `moodLean`, and `skip`. The Cloudflare Worker proxy sets `Cache-Control: no-store`.

---

## Client-Side Skip State

- Skip list is held **in-memory only** (`_skipIds` array or equivalent).
- No `sessionStorage` or `localStorage` in v1.
- Page reload resets skip state → fresh pick for the current hour. This is intentional — matches the radio metaphor (tune back in, start fresh).
- API is stateless: no skip memory between requests.

---

## Mix Fields in Response

| Field | Type | Notes |
|---|---|---|
| `id` | string | Skip list accumulation, copy seed |
| `title` | string | Display, title sizing class |
| `url` | string | SoundCloud embed |
| `genre` | string | Genre colour (mapped client-side) |
| `energy` | string | Meta display |
| `bpm` | integer | Meta display. Omitted if both `BpmMin` and `BpmMax` are null. |
| `moods` | string[] | Meta display (first 3) |
| `publishedAt` | ISO 8601 datetime | Recency display |
| `duration` | integer (seconds) | Meta display. Omitted if source duration is null. |
