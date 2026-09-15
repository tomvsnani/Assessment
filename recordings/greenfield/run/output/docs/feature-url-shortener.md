# File: docs/feature-url-shortener.md
# URL Shortener Service

This document describes the URL Shortener service, including its API, functionality, and configuration.

## Overview

The URL Shortener service provides functionality to:
*   Shorten long URLs into unique, unguessable codes.
*   Redirect users from a short code to the original long URL.
*   Retrieve usage statistics for short links.

It is implemented as a .NET 9 web service with a JSON API and uses an in-memory data store for initial deployment, designed for future integration with PostgreSQL.

## API Endpoints

All endpoints respond with standard HTTP status codes and use [RFC 7807 Problem Details](https://tools.ietf.org/html/rfc7807) for error responses (400 Bad Request, 404 Not Found, 500 Internal Server Error).

### 1. `POST /links`

Creates a new short URL for a given long URL.

*   If the long URL has already been shortened, the existing short link details are returned (per D002).
*   Short codes are 8-character alphanumeric, case-sensitive, and unguessable (per D003).

**Request (Header):**
`Content-Type: application/json`

**Request (Body):**
```json
{
  "url": "string" // The long URL to shorten, e.g., "https://example.com/very/long/path/to/resource"
}
```

**Responses:**

| Status Code | Description                                                                   | Body (application/json)                                      |
| :---------- | :---------------------------------------------------------------------------- | :----------------------------------------------------------- |
| `201 Created` | New short link created successfully.                                          | `ShortLinkResponse` (see below)                              |
| `200 OK`    | Existing short link returned for a previously shortened URL (per D002).       | `ShortLinkResponse` (see below)                              |
| `400 Bad Request` | The provided `url` is invalid or malformed.                                   | RFC 7807 Problem Details                                     |
| `500 Internal Server Error` | An unexpected server error occurred.                            | RFC 7807 Problem Details                                     |

**`ShortLinkResponse` Body:**
```json
{
  "shortCode": "string", // The generated or existing short code (e.g., "aBcD1eFg")
  "shortUrl": "string",  // The full short URL (e.g., "https://short.svc/aBcD1eFg")
  "usageCount": 0        // The current usage count for the link
}
```

**Example:**

**Request:**
```bash
curl -X POST "https://short.svc/links" \
     -H "Content-Type: application/json" \
     -d '{ "url": "https://docs.microsoft.com/en-us/dotnet/api/system.uri.trycreate?view=net-9.0" }'
```

**Response (201 Created):**
```json
{
  "shortCode": "Hk7PqR2s",
  "shortUrl": "https://short.svc/Hk7PqR2s",
  "usageCount": 0
}
```

### 2. `GET /{code}`

Redirects the client to the original long URL associated with the short code. The usage count for the link is incremented with each successful redirect.

**Request (Path Parameter):**
`code`: The 8-character short code (e.g., `Hk7PqR2s`).

**Responses:**

| Status Code | Description                                          | Headers                                                                 |
| :---------- | :--------------------------------------------------- | :---------------------------------------------------------------------- |
| `302 Found` | Redirects to the `LongUrl`.                          | `Location: https://docs.microsoft.com/en-us/dotnet/api/system.uri...` |
| `404 Not Found` | The provided `code` does not correspond to any known short link. | RFC 7807 Problem Details                                                |
| `500 Internal Server Error` | An unexpected server error occurred. | RFC 7807 Problem Details                                                |

**Example:**

**Request:**
```bash
curl -v "https://short.svc/Hk7PqR2s"
```

**Response (302 Found):**
```http
HTTP/1.1 302 Found
Location: https://docs.microsoft.com/en-us/dotnet/api/system.uri.trycreate?view=net-9.0
```

### 3. `GET /links/{code}/stats`

Retrieves the usage statistics for a specific short link.

**Request (Path Parameter):**
`code`: The 8-character short code (e.g., `Hk7PqR2s`).

**Responses:**

| Status Code | Description                                          | Body (application/json)         |
| :---------- | :--------------------------------------------------- | :------------------------------ |
| `200 OK`    | Returns the usage count for the short link.          | `ShortLinkResponse` (see above) |
| `404 Not Found` | The provided `code` does not correspond to any known short link. | RFC 7807 Problem Details        |
| `500 Internal Server Error` | An unexpected server error occurred. | RFC 7807 Problem Details        |

**Example:**

**Request:**
```bash
curl "https://short.svc/links/Hk7PqR2s/stats"
```

**Response (200 OK):**
```json
{
  "shortCode": "Hk7PqR2s",
  "shortUrl": "https://short.svc/Hk7PqR2s",
  "usageCount": 5
}
```

### 4. `GET /health`

Provides a basic health check endpoint for load balancers and monitoring systems.

**Request:** None.

**Responses:**

| Status Code | Description           | Body           |
| :---------- | :-------------------- | :------------- |
| `200 OK`    | Service is operational. | Empty body |

**Example:**

**Request:**
```bash
curl "https://short.svc/health"
```

**Response (200 OK):**
```http
HTTP/1.1 200 OK
```

## Configuration

The service uses [Serilog](https://serilog.net/) for structured logging. Logging configuration is managed via `appsettings.json` (or environment variables).

**Example `appsettings.json` for logging:**
```json
{
  "Serilog": {
    "Using": ["Serilog.Sinks.Console"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" }
      // Additional sinks for Splunk, etc. can be configured here
    ],
    "Enrich": ["FromLogContext", "WithMachineName", "WithProcessId", "WithThreadId"],
    "Properties": {
      "Application": "UrlShortenerService"
    }
  }
}
```

## Error Handling

The service implements custom exception handling to provide consistent and machine-readable error responses using [RFC 7807 Problem Details](https://tools.ietf.org/html/rfc7807) for `400 Bad Request`, `404 Not Found`, and `500 Internal Server Error` scenarios.
*   **Invalid URL**: `InvalidUrlException` results in a `400 Bad Request`.
*   **Link Not Found**: `LinkNotFoundException` results in a `404 Not Found`.
*   **Unhandled Exceptions**: Any other unhandled exception results in a `500 Internal Server Error`.
