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

# File: docs/runbook-url-shortener.md
# URL Shortener Service Runbook

This runbook provides operational guidance for the URL Shortener service, covering health signals, common failure modes, and remediation steps.

## Service Overview

The URL Shortener is a .NET 9 web service that provides short link creation, redirection, and usage statistics. It uses an in-memory data store, meaning all data is lost if the service restarts. This is intended for development and non-persistent environments; a PostgreSQL backend is planned for production.

## Health Checks

### `GET /health`

*   **Endpoint**: `/health`
*   **Purpose**: Provides a basic liveness check for load balancers and container orchestrators (e.g., Kubernetes).
*   **Expected Response**: `200 OK` with an empty body if the service is running and responsive.
*   **Troubleshooting `200 OK`**: A `200 OK` indicates the service process is alive and able to respond to HTTP requests. It does not necessarily guarantee full functionality (e.g., connectivity to external dependencies like a database).
*   **Troubleshooting Non-`200 OK`**: If this endpoint returns anything other than `200 OK` (e.g., `503 Service Unavailable`, `500 Internal Server Error`, or connection refused), it indicates a critical issue with the service itself. Check service logs immediately for startup failures, unhandled exceptions, or port conflicts. Consider restarting the service.

## Logging

The service uses [Serilog](https://serilog.net/) for structured logging. Logs are emitted to the console by default and can be configured via `appsettings.json` or environment variables to send to external sinks like Splunk.

**Key Logging Principles:**
*   **Structured**: Logs are in a structured format (e.g., JSON) for easy machine parsing and searching in log aggregation systems.
*   **No PII**: No personally identifiable information (PII) is logged (e.g., full client IP addresses or user agent strings). Long URLs are logged as part of business operations.
*   **Contextual**: Logs include contextual information such as `ShortCode`, `LongUrl`, `UsageCount`, `Path`, and `Message` to aid in diagnostics.

## Common Failure Modes and Remediation

### 1. Issue: Invalid Long URL Submitted

*   **Symptoms**: When attempting to `POST /links`, the API returns a `400 Bad Request` response with problem details indicating an invalid URL.
*   **Identifying Log Line(s)**:
    ```
    { "@t":"<timestamp>", "@mt":"Attempted to shorten an invalid URL: {LongUrl}", "@l":"Warning", "LongUrl":"<invalid-url>", ... }
    ```
    (The exact format may vary based on Serilog configuration).
*   **Remediation**: The user provided a malformed or invalid URL. Instruct the user to verify the URL is a complete, absolute URI with either `http://` or `https://` scheme. Example of invalid URLs: `www.example.com`, `ftp://bad.com`, empty string.

### 2. Issue: Short Code Not Found

*   **Symptoms**: When attempting to `GET /{code}` or `GET /links/{code}/stats`, the API returns a `404 Not Found` response with problem details.
*   **Identifying Log Line(s)**:
    ```
    { "@t":"<timestamp>", "@mt":"Short code {ShortCode} not found for retrieval", "@l":"Warning", "ShortCode":"<code-attempted>", ... }
    ```
    or
    ```
    { "@t":"<timestamp>", "@mt":"Short code {ShortCode} not found for stats", "@l":"Warning", "ShortCode":"<code-attempted>", ... }
    ```
*   **Remediation**:
    1.  **Check for typos**: Ensure the short code in the request matches an existing one exactly (codes are case-sensitive).
    2.  **Verify creation**: Confirm that the short link was successfully created via a `POST /links` request. In the current in-memory implementation, remember that all links are lost if the service restarts.
    3.  **Data Consistency**: If running with a persistent store (future), investigate potential data synchronization issues.

### 3. Issue: Unhandled Internal Server Error

*   **Symptoms**: Any API call returns a `500 Internal Server Error` response with generic problem details (`"detail": "An unexpected error occurred."`).
*   **Identifying Log Line(s)**:
    ```
    { "@t":"<timestamp>", "@mt":"An unhandled exception occurred: {Path}", "@l":"Error", "@x":"<exception-details>", "Path":"<request-path>", ... }
    ```
*   **Remediation**:
    1.  **Examine Logs**: This is a critical issue. Immediately review the detailed error logs (including stack traces) from the service to pinpoint the root cause. Look for exceptions related to dependencies, null references, or unhandled states.
    2.  **Resource Exhaustion**: Check system resources (CPU, memory, disk I/O) on the host where the service is running. Resource exhaustion can lead to unexpected crashes.
    3.  **Restart**: If the error is intermittent or appears to be a transient issue, a restart of the service instance may temporarily resolve the problem. However, always aim to address the underlying cause.
    4.  **Escalate**: If the error is persistent or its cause cannot be quickly identified, escalate to the development team with detailed log evidence.

### 4. Issue: High Latency or Unresponsiveness

*   **Symptoms**: API requests take an unusually long time to complete or time out. The `/health` endpoint might still return `200 OK` initially, but the application is slow.
*   **Identifying Log Line(s)**: (Specific to future persistent data store, but generally look for database connection timeouts, slow query warnings, or external service call timeouts in logs).
*   **Remediation**:
    1.  **Monitor Dependencies**: While the current implementation uses in-memory storage, for future persistent storage (e.g., PostgreSQL), check the health and performance of the database or any other external dependencies.
    2.  **Resource Utilization**: Monitor CPU, memory, and network utilization of the service host. High utilization can lead to performance degradation.
    3.  **Scale Out**: If load is high and resources are saturated, consider scaling out the service instances.
    4.  **Application Logs**: Look for any warnings or errors in the application logs that might indicate bottlenecks or repeated failures.

## Rollback Strategy

As a greenfield service, a rollback typically involves deploying a previously functional version of the application. The specific steps depend on the deployment platform (e.g., Kubernetes, virtual machines) but generally include:

1.  **Identify Last Known Good Version**: Determine the last deployed version that was stable and functional.
2.  **Deploy Previous Version**: Use your deployment tooling to roll back the service to the identified previous version. This usually means deploying the previous container image or application package.
3.  **Monitor**: Closely monitor the service health and logs after rollback to ensure stability. If the issue persists, further investigation into environmental factors or the previous version itself might be necessary.

**Note**: Due to the in-memory nature of the current data store, rolling back to an older version (or restarting the service) will result in the loss of all shortened links created since the service started. This data loss aspect will change when a persistent data store is introduced.