---
id: REQ-BF-1
title: Move click counting off the redirect path
---

Production incident follow-up. Under load, `GET /{code}` latency climbs because every redirect
does a synchronous `UPDATE links SET clicks = clicks + 1` before responding, and hot links
serialise on that row. The redirect must not wait for analytics.

Change the service so that a redirect records the click as an **event** that is processed
asynchronously, and stats are served from an aggregate that the event consumer maintains. The
event must not be lost if the process dies after the redirect was served — use a transactional
outbox in the same database as the link, drained by a background publisher to a Kafka topic
(`clicks`). A consumer (`Shortener.Analytics`) reads the topic and maintains per-code totals.

Constraints:

- The redirect path may not make any network call other than the cache/repository read it
  already makes; the click write must be local and non-blocking from the caller's point of view.
- `GET /links/{code}/stats` keeps its contract but may be eventually consistent; document the lag.
- Everything must still run with no Docker via in-memory implementations of the outbox, the
  topic and the consumer, and the existing tests must keep passing.
- Existing rows' `clicks` totals must not be lost when the new aggregate takes over.
- Do not change `POST /links`.
