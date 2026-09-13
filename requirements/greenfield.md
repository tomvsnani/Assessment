---
id: REQ-GF-1
title: URL shortener service, first version
---

We need a URL shortener service for internal links. Engineers paste a long URL and get a short
one back; anyone who opens the short link is redirected to the original. We also want to see how
many times each short link was used.

It must be a .NET 9 web service with a JSON API, and it has to be reliable enough to put behind
our internal load balancer: health endpoints, sensible behaviour when the database is slow, and
logs we can search in Splunk. Start with an in-memory store so it runs on a laptop, but structure
it so PostgreSQL can be plugged in without rewriting the endpoints.

Requests:

- `POST /links` with `{ "url": "https://..." }` returns the short code and the short URL.
- `GET /{code}` redirects to the original URL.
- `GET /links/{code}/stats` returns how many times the link was followed.

Custom aliases and expiry would be nice but are not required for the first version. Links must
not be guessable from one another. Unit tests and an OpenAPI description are expected.
