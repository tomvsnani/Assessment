## AC-1: Create short link with valid URL

*   **Test Case**: `Integration_PostLinks_WithValidNewUrl_Returns201AndStoresMapping`
    **Given**: A new, valid, well-formed long URL (e.g., `https://www.example.com/some/path?query=value`).
    **When**: A `POST /links` request is made with `{ "url": "https://www.example.com/some/path?query=value" }`.
    **Then**:
    *   The response status code is 201 Created.
    *   The response body is `application/json` and contains `shortCode` (an 8-character alphanumeric string, per D003) and `shortUrl` (a string containing the base URL of the service and the `shortCode`).
    *   A subsequent `GET /{shortCode}` request with `AllowAutoRedirect = false` returns 302 Found with `Location: https://www.example.com/some/path?query=value`.
    *   A subsequent `GET /links/{shortCode}/stats` request returns 200 OK with `{ "usageCount": 0 }`.
    **Level**: Integration

*   **Test Case**: `Unit_ShortenerService_CreateShortLink_NewUrl_GeneratesCodeAndSaves`
    **Given**: `ShortenerService` configured with a mock `ILinkRepository` that returns `null` for `FindByLongUrlAsync` and a mock `IShortCodeGenerator` that returns a predictable unique code (e.g., "TESTCODE").
    **When**: `ShortenerService.CreateShortLinkAsync("https://new.example.com")` is called.
    **Then**:
    *   `ILinkRepository.FindByLongUrlAsync` is called once with `"https://new.example.com"`.
    *   `IShortCodeGenerator.GenerateCode` is called once.
    *   `ILinkRepository.SaveAsync` is called once with a new `Link` entity containing the provided long URL, the generated short code, and a usage count of 0.
    *   The method returns a `Link` object with `ShortCode` "TESTCODE" and `UsageCount` 0.
    **Level**: Unit

*   **Test Case**: `Unit_EightCharacterAlphanumericCodeGenerator_GenerateCode_ProducesValidFormat`
    **Given**: An `EightCharacterAlphanumericCodeGenerator` instance.
    **When**: `GenerateCode()` is called multiple times.
    **Then**:
    *   Each generated code is an 8-character string.
    *   Each character in the generated code is alphanumeric (a-z, A-Z, 0-9).
    *   A reasonable number of consecutively generated codes are distinct (e.g., test 1000 codes for uniqueness).
    **Level**: Unit

## AC-2: Redirect from short code

*   **Test Case**: `Integration_GetShortCode_ValidCode_RedirectsAndIncrementsCount`
    **Given**: A short link has been successfully created for `https://original.com/path/to/resource` returning short code `XYZ123ABC`. The current usage count for `XYZ123ABC` is `n`.
    **When**: A `GET /XYZ123ABC` request is made to the service with `AllowAutoRedirect = false` on the HTTP client.
    **Then**:
    *   The response status code is 302 Found.
    *   The `Location` header in the response is `https://original.com/path/to/resource`.
    *   A subsequent `GET /links/XYZ123ABC/stats` request returns 200 OK with `{ "usageCount": n + 1 }`.
    **Level**: Integration

*   **Test Case**: `Unit_ShortenerService_RetrieveOriginalUrl_ValidCode_IncrementsCountAndReturnsUrl`
    **Given**: `ShortenerService` configured with a mock `ILinkRepository` that returns a `Link` entity for `"VALIDCODE"` and a mock for `IncrementUsageCountAsync`.
    **When**: `ShortenerService.RetrieveOriginalUrlAsync("VALIDCODE")` is called.
    **Then**:
    *   `ILinkRepository.FindByShortCodeAsync` is called once with `"VALIDCODE"`.
    *   `ILinkRepository.IncrementUsageCountAsync` is called once with `"VALIDCODE"`.
    *   The method returns the `LongUrl` from the retrieved `Link` entity.
    **Level**: Unit

## AC-3: Redirect with unknown short code

*   **Test Case**: `Integration_GetShortCode_UnknownCode_Returns404NotFound`
    **Given**: A short code `UNKNOWNXYZ` that has never been created.
    **When**: A `GET /UNKNOWNXYZ` request is made to the service.
    **Then**:
    *   The response status code is 404 Not Found.
    *   The response body is `application/problem+json` and contains a descriptive error message indicating the short code was not found.
    **Level**: Integration

*   **Test Case**: `Unit_ShortenerService_RetrieveOriginalUrl_UnknownCode_ThrowsLinkNotFoundException`
    **Given**: `ShortenerService` configured with a mock `ILinkRepository` that returns `null` for `FindByShortCodeAsync`.
    **When**: `ShortenerService.RetrieveOriginalUrlAsync("NONEXISTENT")` is called.
    **Then**:
    *   `ILinkRepository.FindByShortCodeAsync` is called once with `"NONEXISTENT"`.
    *   A `LinkNotFoundException` is thrown.
    *   `ILinkRepository.IncrementUsageCountAsync` is *not* called.
    **Level**: Unit

## AC-4: Get usage stats for valid short code

*   **Test Case**: `Integration_GetStats_ValidCode_ReturnsCorrectUsageCount`
    **Given**: A short link with code `STATCODE1` has been created, and the `GET /STATCODE1` endpoint has been successfully called `5` times.
    **When**: A `GET /links/STATCODE1/stats` request is made.
    **Then**:
    *   The response status code is 200 OK.
    *   The response body is `application/json` and contains `{ "usageCount": 5 }`.
    **Level**: Integration

*   **Test Case**: `Unit_ShortenerService_GetUsageStats_ValidCode_ReturnsCorrectCount`
    **Given**: `ShortenerService` configured with a mock `ILinkRepository` that returns a `Link` entity for `"CODEWITHSTATS"` with `UsageCount = 10`.
    **When**: `ShortenerService.GetUsageStatsAsync("CODEWITHSTATS")` is called.
    **Then**:
    *   `ILinkRepository.FindByShortCodeAsync` is called once with `"CODEWITHSTATS"`.
    *   The method returns `10`.
    **Level**: Unit

## AC-5: Get usage stats for unknown short code

*   **Test Case**: `Integration_GetStats_UnknownCode_Returns404NotFound`
    **Given**: A short code `UNKNOWNSTATS` that has never been created.
    **When**: A `GET /links/UNKNOWNSTATS/stats` request is made.
    **Then**:
    *   The response status code is 404 Not Found.
    *   The response body is `application/problem+json` and contains a descriptive error message indicating the short code was not found.
    **Level**: Integration

*   **Test Case**: `Unit_ShortenerService_GetUsageStats_UnknownCode_ThrowsLinkNotFoundException`
    **Given**: `ShortenerService` configured with a mock `ILinkRepository` that returns `null` for `FindByShortCodeAsync`.
    **When**: `ShortenerService.GetUsageStatsAsync("NONEXISTENTSTATS")` is called.
    **Then**:
    *   `ILinkRepository.FindByShortCodeAsync` is called once with `"NONEXISTENTSTATS"`.
    *   A `LinkNotFoundException` is thrown.
    **Level**: Unit

## AC-6: Create short link with invalid URL

*   **Test Case**: `Integration_PostLinks_InvalidUrl_Returns400BadRequest`
    **Given**: An invalid URL string (e.g., `""`, `"not-a-valid-url"`, `"ftp://insecure.site"`).
    **When**: A `POST /links` request is made with `{ "url": "<invalid_url_string>" }`.
    **Then**:
    *   The response status code is 400 Bad Request.
    *   The response body is `application/problem+json` and contains details indicating the URL validation failure (e.g., "The URL field is required" or "The URL field is not a valid absolute URI.").
    *   No short link is created (verified by checking repository state).
    **Level**: Integration

*   **Test Case**: `Unit_ShortenerService_CreateShortLink_InvalidUrl_ThrowsValidationException`
    **Given**: `ShortenerService` instance.
    **When**: `ShortenerService.CreateShortLinkAsync("")` is called.
    **Then**:
    *   An `ArgumentException` (or other appropriate validation exception) is thrown.
    *   Neither `ILinkRepository.FindByLongUrlAsync` nor `ILinkRepository.SaveAsync` are called.
    **Level**: Unit

## AC-7: Health endpoint

*   **Test Case**: `Integration_GetHealthEndpoint_ServiceHealthy_Returns200OK`
    **Given**: The service is running and all configured health checks (initially just the in-memory store) are healthy.
    **When**: A `GET /health` request is made.
    **Then**:
    *   The response status code is 200 OK.
    *   The response body indicates a healthy status (e.g., "Healthy" or similar plain text).
    **Level**: Integration

## AC-8: Create short link for existing long URL

*   **Test Case**: `Integration_PostLinks_ExistingUrl_Returns200OKAndExistingShortLink`
    **Given**: A long URL `https://duplicate.example.com/item`.
    **When**:
    1.  A `POST /links` request is made with `{ "url": "https://duplicate.example.com/item" }` (this first call returns 201 Created with `shortCode1` and `shortUrl1`).
    2.  A second `POST /links` request is made with `{ "url": "https://duplicate.example.com/item" }`.
    **Then**:
    *   The second request returns a 200 OK status code (per D002).
    *   The response body for the second request is `application/json` and contains the *same* `shortCode1` and `shortUrl1` as the first request.
    *   No new short link is created (verified by `InMemoryLinkRepository` containing only one entry for `https://duplicate.example.com/item`).
    **Level**: Integration

*   **Test Case**: `Unit_ShortenerService_CreateShortLink_ExistingUrl_ReturnsExistingLinkWithoutNewGeneration`
    **Given**: `ShortenerService` configured with a mock `ILinkRepository` that returns an existing `Link` entity (e.g., `Link("https://existing.example.com", "EXISTING", 5)`) for `FindByLongUrlAsync`.
    **When**: `ShortenerService.CreateShortLinkAsync("https://existing.example.com")` is called.
    **Then**:
    *   `ILinkRepository.FindByLongUrlAsync` is called once with `"https://existing.example.com"`.
    *   `IShortCodeGenerator.GenerateCode` is *not* called.
    *   `ILinkRepository.SaveAsync` is *not* called.
    *   The existing `Link` entity is returned by the method.
    **Level**: Unit

## Not covered by acceptance criteria

*   **Thread safety of `InMemoryLinkRepository`**
    *   **Suggested Test**: `Unit_InMemoryLinkRepository_ConcurrentOperations_MaintainsConsistency`
        **Given**: An `InMemoryLinkRepository` instance.
        **When**: Multiple tasks/threads concurrently invoke `SaveAsync`, `FindByShortCodeAsync`, `FindByLongUrlAsync`, and `IncrementUsageCountAsync` with various data.
        **Then**: No exceptions are thrown, and a final state verification confirms data consistency and correctness (e.g., all saved links are present, usage counts are accurate).
    *   **Level**: Unit/Concurrency

*   **Robustness of short code generation in collision scenario**
    *   **Suggested Test**: `Unit_ShortenerService_CreateShortLink_CodeCollisionHandledByRetry`
        **Given**: `ShortenerService` configured with a mock `IShortCodeGenerator` that returns a *pre-existing* code for its first `GenerateCode()` call (causing a simulated collision with an already saved link), but a unique code for subsequent calls.
        **When**: `ShortenerService.CreateShortLinkAsync("https://collision.example.com")` is called for a new URL.
        **Then**: The `ILinkRepository.SaveAsync` (or equivalent persistence logic) is retried, `IShortCodeGenerator.GenerateCode` is called again, and the link is successfully stored with a unique short code.
    *   **Level**: Unit

*   **Structured Logging Compliance**
    *   **Suggested Test**: `Integration_Logging_AdheresToPiiPolicyAndIsStructured`
        **Given**: The `Shortener.Api` service is running with configured structured logging.
        **When**: Various API endpoints are called (e.g., `POST /links`, `GET /{code}`, `GET /health`).
        **Then**:
        *   The emitted logs are in a structured format (e.g., JSON).
        *   Relevant context is present (e.g., HTTP method, path, status code, short code, original URL where appropriate).
        *   No Personally Identifiable Information (PII) such as client IP addresses or full User-Agent strings are present in the logs.
    *   **Level**: Integration (by inspecting log output)

*   **OpenAPI/Swagger Documentation Generation**
    *   **Suggested Test**: `Integration_SwaggerJson_IsGeneratedAndValid`
        **Given**: The `Shortener.Api` service is running.
        **When**: A `GET /swagger/v1/swagger.json` request is made.
        **Then**: The response status code is 200 OK, and the response body contains a valid JSON OpenAPI document describing all exposed API endpoints, their expected request/response schemas, and HTTP status codes.
    *   **Level**: Integration

*   **Graceful Shutdown**
    *   **Suggested Test**: `Integration_Service_PerformsGracefulShutdown`
        **Given**: The `Shortener.Api` service is running and currently processing a simulated long-running request (e.g., a `POST /links` call that has a configurable artificial delay).
        **When**: A shutdown signal is sent to the application host.
        **Then**: The service completes the outstanding request (verified by the request eventually returning a successful response) before terminating, and no data is lost or corrupted.
    *   **Level**: Integration/Stress

## Reviewer checklist

1.  **Code Structure**: Are the domain entity (`Link`), ports (`ILinkRepository`, `IShortCodeGenerator`), and infrastructure implementations (`InMemoryLinkRepository`, `EightCharacterAlphanumericCodeGenerator`) correctly separated into `Shortener.Core` and `Shortener.Infrastructure` projects as per the design? (Yes/No)
2.  **Dependency Injection**: Is `Program.cs` correctly configuring Dependency Injection to register `ShortenerService` with its dependencies (`InMemoryLinkRepository` and `EightCharacterAlphanumericCodeGenerator`)? (Yes/No)
3.  **Short Code Generation (D003)**: Does `EightCharacterAlphanumericCodeGenerator` reliably produce 8-character, case-sensitive alphanumeric codes using a cryptographically secure random number generator? (Yes/No)
4.  **Duplicate URL Handling (D002)**: Does the `POST /links` endpoint correctly detect and return the *existing* short code and URL (200 OK) when the same long URL is submitted multiple times? (Yes/No)
5.  **Input URL Validation (AC-6)**: Is there robust validation for input URLs on `POST /links`, ensuring only well-formed `http(s)` URIs are accepted, and rejecting invalid ones with a 400 Bad Request and Problem Details? (Yes/No)
6.  **Error Responses**: Are all API error responses (e.g., 400 Bad Request, 404 Not Found) consistently formatted using ASP.NET Core Problem Details (RFC 7807)? (Yes/No)
7.  **Usage Count Increment**: Does `GET /{code}` reliably increment the usage count for the corresponding short link as a side effect of the redirect? (Yes/No)
8.  **Logging Compliance**: Does the structured logging implementation strictly adhere to `PiiInLogsPolicy` (avoiding client IPs, user agents) and `NoSecretsPolicy` (no secrets in logs)? (Yes/No)
9.  **OpenAPI Documentation**: Is OpenAPI/Swagger documentation automatically generated and accessible, accurately describing all API endpoints, models, and operations? (Yes/No)
10. **Test Coverage**: Do the implemented unit and integration tests provide comprehensive coverage for all acceptance criteria and critical aspects identified in the design? (Yes/No)