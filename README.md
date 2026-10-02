# Payment Gateway System

A payment gateway platform built with .NET 8, providing virtual card issuance, account balance management, ISO 8583 payment protocol processing, and multi-channel payment integration. The system uses a clean architecture with domain-driven design, backed by PostgreSQL and integrated with Kafka for event streaming.

## Table of Contents

- [Payment Integration](#payment-integration)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Services](#services)
- [API Documentation](#api-documentation)
- [Swagger / OpenAPI](#swagger--openapi)
- [ISO 8583 TCP Interface](#iso-8583-tcp-interface)
- [Configuration](#configuration)
- [Nginx Proxy Configuration](#nginx-proxy-configuration)
- [Development](#development)
- [Observability (RED & USE)](#observability-red--use)
- [Networking](#networking)
- [Kubernetes Deployment](#kubernetes-deployment)
- [Terraform (AKS)](#terraform-aks)
- [Git Workflow & CI/CD](#git-workflow--cicd)
- [Troubleshooting](#troubleshooting)
- [License](#license)

## Payment Integration

The system integrates with Nigerian payment infrastructure across multiple channels:

**NIBSS Payment Channels:**
- **NIP** — Real-time interbank transfers (NIBSS Instant Payment)
- **NQR** — QR code-based merchant payments (static and dynamic)
- **e-BillsPay** — Electronic bill payment processing
- **mCash/USSD** — Mobile payments via USSD for feature phones
- **Direct Debit** — Mandate-based recurring/scheduled payments
- **GAPS** — Bulk/batch payment processing for corporates

**Card Switch Networks:**
- **Interswitch** — Verve card authorization, capture, and reversal via ISO 8583
- **Cardify** — Visa/Mastercard authorization with scheme-specific message formatting

**Cross-Cutting Services:**
- **Payment Orchestration** — Central routing, transaction reference assignment, and workflow coordination
- **Fraud & Risk Engine** — Real-time scoring (0-100) with configurable block/review thresholds and 200ms p95 SLA
- **Saga Orchestrator** — Distributed transaction management with compensation and exponential backoff
- **Idempotency Guard** — Duplicate request detection with 24-hour TTL (PostgreSQL-backed)
- **Circuit Breakers** — Per-channel failure detection with automatic recovery (custom implementation)
- **Durable Processor** — Kafka consumer with at-least-once delivery and dead-letter routing
- **PCI Boundary** — AES-256 tokenization, service allowlist, TLS 1.2+ enforcement
- **Immutable Audit Store** — SHA-256 hash-chained append-only audit trail
- **Dispute Service** — Chargeback lifecycle management with network-mandated timeframe validation

## Architecture

```
                         Host Machine
┌──────────────────────────────────────────────────────────────────────────┐
│                                                                          │
│  Client ──► :443 (HTTPS) ──► ┌───────────┐ ──► :8080 ──► REST API        │
│                               │   nginx   │                              │
│  Client ──► :9443 (TLS)  ──► │  (proxy)  │ ──► :9090 ──► TCP/8583        │
│                               └───────────┘                              │
│                                     │                                    │
│                            cardmgmt-network                              │
│                                     │                                    │
│         ┌──────────┬────────────────┼────────────────┬──────────┐        │
│         │          │                │                │          │        │
│   ┌───────────┐ ┌───────────┐ ┌───────────┐ ┌───────────┐ ┌───────┐      │
│   │ PostgreSQL│ │   Kafka   │ │    App    │ │   Redis   │ │ WAHA  │      │
│   │   :5432   │ │   :9092   │ │ 8080/9090 │ │   :6379   │ │ :3000 │      │
│   └───────────┘ └───────────┘ └───────────┘ └───────────┘ └───────┘      │
└──────────────────────────────────────────────────────────────────────────┘
```

All external traffic to the application flows through nginx, which terminates TLS and proxies requests to the internal services. The application ports are not published to the host — only nginx, PostgreSQL (for developer tooling), Kafka, Redis, and WAHA are directly accessible.

## Project Structure

```
payment_gateway/
├── src/
│   ├── CardManagement.Api/            # ASP.NET Core Web API (controllers, startup, Swagger)
│   │   └── Controllers/
│   │       ├── CardsController.cs     # Virtual card issuance
│   │       ├── AccountsController.cs  # Account balance queries
│   │       ├── PaymentsController.cs  # Payment initiation and status inquiry
│   │       └── DisputesController.cs  # Dispute lifecycle management
│   ├── CardManagement.Application/    # Application layer (ports, DTOs, use cases)
│   │   ├── Ports/                     # Interface definitions (IPaymentOrchestrator, IChannelAdapter, etc.)
│   │   └── DTOs/                      # Data transfer objects
│   ├── CardManagement.Domain/         # Domain layer (entities, value objects, enums)
│   │   ├── Entities/                  # PaymentRequest, SagaState, AuditEntry, DisputeRecord, etc.
│   │   ├── Enums/                     # PaymentChannel, PaymentStatus, PaymentTransactionType
│   │   └── ValueObjects/              # Money, IdempotencyRecord, ProcessorType
│   └── CardManagement.Infrastructure/ # Infrastructure implementations
│       ├── Channels/
│       │   ├── Nibss/                 # NIP, NQR, EBillsPay, MCash, DirectDebit, GAPS adapters
│       │   └── CardSwitch/            # Interswitch and Cardify ISO 8583 adapters
│       ├── Orchestration/             # PaymentOrchestrator (routing, saga, fraud check)
│       ├── Fraud/                     # FraudRiskEngine with scoring rules
│       ├── Saga/                      # Saga orchestrator with compensation logic
│       ├── Resilience/                # Circuit breaker registry and health checks
│       ├── Security/                  # PCI boundary (tokenization, encryption, allowlist)
│       ├── Audit/                     # Immutable audit store with hash chain
│       ├── Idempotency/               # Idempotency guard with cleanup service
│       ├── Durable/                   # Kafka durable processor (at-least-once)
│       ├── Disputes/                  # Dispute service with Kafka event publishing
│       ├── Networking/                # OutboundConnectionPool (persistent TCP/TLS)
│       └── Persistence/               # EF Core DbContext, migrations, repositories
├── tests/
│   ├── CardManagement.Tests/          # xUnit + FsCheck property-based tests
│   │   ├── Domain/                    # Domain logic tests
│   │   ├── Infrastructure/            # Unit tests (adapters, services, circuit breakers)
│   │   ├── Integration/               # API integration tests (WebApplicationFactory)
│   │   ├── ValueObjects/              # Value object property tests
│   │   └── ArchitectureTests.cs       # Architecture constraint tests
│   ├── CardManagement.PlatformServices.UnitTests/      # Platform services unit + property tests
│   └── CardManagement.PlatformServices.IntegrationTests/ # Platform services integration tests
├── .github/
│   ├── workflows/
│   │   ├── ci-dev.yml                 # CI for PRs to dev (build + test gate)
│   │   ├── ci-staging.yml            # CI for PRs to staging (test + Docker build)
│   │   └── ci-prod.yml               # CI for PRs to prod (test + ACR push + AKS deploy)
│   ├── branch-protection.md           # Branch protection setup guide
│   └── CODEOWNERS                     # Required reviewers by path
├── k8s/
│   ├── kustomization.yaml             # Kustomize root
│   ├── deploy.sh                      # Deployment helper script
│   ├── namespace.yaml                 # cardmgmt namespace
│   ├── secrets.yaml                   # Secrets (postgres, grafana, TLS)
│   ├── configmaps.yaml                # ConfigMaps (nginx, prometheus, grafana)
│   ├── pvcs.yaml                      # Persistent volume claims
│   ├── postgres.yaml                  # PostgreSQL deployment
│   ├── kafka.yaml                     # Kafka deployment
│   ├── redis.yaml                     # Redis deployment
│   ├── waha.yaml                      # WAHA deployment
│   ├── app.yaml                       # App deployment (2 replicas)
│   ├── nginx.yaml                     # Nginx LoadBalancer
│   ├── prometheus.yaml                # Prometheus deployment
│   └── grafana.yaml                   # Grafana deployment
├── terraform/
│   ├── main.tf                        # Providers (azurerm, kubernetes, helm)
│   ├── variables.tf                   # Input variables
│   ├── resource_group.tf              # Azure resource group
│   ├── network.tf                     # VNet, subnets, private DNS
│   ├── acr.tf                         # Azure Container Registry
│   ├── aks.tf                         # AKS cluster + Log Analytics
│   ├── postgres.tf                    # Azure PostgreSQL Flexible Server
│   ├── redis.tf                       # Azure Cache for Redis
│   ├── k8s_workloads.tf              # K8s deployments, services, ingress
│   ├── observability.tf               # kube-prometheus-stack + dashboards
│   ├── outputs.tf                     # Terraform outputs
│   └── terraform.tfvars.example       # Example variable values
├── observability/
│   ├── prometheus/
│   │   └── prometheus.yml             # Prometheus scrape config
│   └── grafana/
│       ├── grafana.ini                # Dark theme, anonymous access
│       ├── provisioning/              # Datasource + dashboard provisioning
│       └── dashboards/
│           └── red-use-dashboard.json # RED & USE Grafana dashboard
├── nginx/
│   ├── nginx.conf                     # Nginx reverse proxy configuration
│   ├── generate-certs.sh             # Self-signed TLS certificate generator
│   └── certs/                         # Generated certificates (gitignored)
├── docker-compose.yml                 # Full stack orchestration (local dev)
├── Dockerfile                         # Multi-stage .NET build
├── CardManagement.sln                 # Solution file
└── Directory.Build.props              # Shared MSBuild properties (C# 12)
```

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/) and [Docker Compose](https://docs.docker.com/compose/install/) (v2+)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for local development without Docker)
- OpenSSL (for certificate generation)
- bash shell (for running the cert generation script)

## Quick Start

### 1. Generate TLS Certificates

```bash
bash nginx/generate-certs.sh
```

This creates self-signed certificates in `nginx/certs/` valid for 365 days with SAN entries for `localhost` and `127.0.0.1`.

### 2. Start the Stack

```bash
docker compose up --build
```

This brings up all services in order (PostgreSQL → Kafka → Redis → WAHA → App → Nginx), respecting health check dependencies.

### 3. Verify

```bash
# Check the HTTPS endpoint
curl -k https://localhost/health

# Check that internal ports are not exposed
curl http://localhost:8080/health  # Should fail — port not published
```

### 4. Open Swagger UI

Navigate to: **https://localhost/swagger**

The interactive Swagger UI provides full API documentation, request/response schemas, and a "Try it out" interface for testing endpoints directly from the browser.

The OpenAPI JSON spec is available at: `https://localhost/swagger/v1/swagger.json`

## Services

| Service | Image | Ports (Host) | Purpose |
|---------|-------|------|---------|
| nginx | nginx:alpine | 443, 9443 | TLS termination and reverse proxy |
| card-management-app | Custom (.NET 8) | None (internal only) | REST API + ISO 8583 TCP listener |
| postgres | postgres:16 | 5432 | Persistent storage |
| kafka | confluentinc/cp-kafka:7.6.0 | 9092 | Event streaming |
| redis | redis:7-alpine | 6379 | API key cache and read model staleness tracking |
| waha | devlikeapro/waha:latest | 3000 | WhatsApp messaging via self-hosted WAHA API |
| prometheus | prom/prometheus:v2.53.0 | 9090 | Metrics collection and storage |
| grafana | grafana/grafana:11.1.0 | 3001 | Metrics visualization (RED & USE dashboards) |

---

## API Documentation

All endpoints are accessed via HTTPS through the nginx proxy at `https://localhost`. The API uses JSON for request and response bodies.

### Base URL

```
https://localhost
```

> Note: Since the proxy uses a self-signed certificate, clients must trust the certificate or disable TLS verification (e.g., `curl -k`).

---

### Health Check

Check service availability including PostgreSQL, Kafka, and payment channel circuit breaker states.

```
GET /health
```

#### Success Response — `200 OK`

All dependencies and payment channels are healthy:

```json
{
  "status": "Healthy",
  "entries": {
    "postgresql": {
      "status": "Healthy",
      "description": null,
      "duration": "00:00:00.0123456"
    },
    "kafka": {
      "status": "Healthy",
      "description": null,
      "duration": "00:00:00.0234567"
    },
    "payment-channels": {
      "status": "Healthy",
      "description": "All payment channels operational",
      "duration": "00:00:00.0010000"
    }
  }
}
```

#### Degraded Response — `200 OK` (with degraded status)

One or more payment channels have their circuit breaker open:

```json
{
  "status": "Degraded",
  "entries": {
    "postgresql": {
      "status": "Healthy",
      "description": null,
      "duration": "00:00:00.0051234"
    },
    "payment-channels": {
      "status": "Degraded",
      "description": "Payment channels degraded: NIP circuit breaker(s) open",
      "duration": "00:00:00.0005000"
    }
  }
}
```

#### Unhealthy Response — `503 Service Unavailable`

A critical dependency is down:

```json
{
  "status": "Unhealthy",
  "entries": {
    "postgresql": {
      "status": "Unhealthy",
      "description": "Failed to connect to PostgreSQL",
      "duration": "00:00:05.0000000"
    },
    "kafka": {
      "status": "Healthy",
      "description": null,
      "duration": "00:00:00.0180000"
    }
  }
}
```

---

### Issue Virtual Card

Issues a new virtual card with an HSM-secured PAN, CVV2, and calculated expiry date. The card is associated with a specified account and published as a domain event to Kafka.

```
POST /api/cards/issue
```

#### Request Headers

| Header | Value |
|--------|-------|
| Content-Type | application/json |

#### Request Body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `cardScheme` | string (enum) | Yes | Card network. One of: `Verve`, `Visa`, `Mastercard` |
| `binPrefix` | string | Yes | BIN prefix digits (1-8 numeric characters) |
| `panLength` | integer | Yes | Desired PAN length (16-19) |
| `accountId` | string (UUID) | Yes | Account ID to associate the card with |
| `validityMonths` | integer | Yes | Validity period in months (1-60) |

#### Example Request

```bash
curl -k -X POST https://localhost/api/cards/issue \
  -H "Content-Type: application/json" \
  -d '{
    "cardScheme": "Visa",
    "binPrefix": "411111",
    "panLength": 16,
    "accountId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "validityMonths": 24
  }'
```

#### Success Response — `201 Created`

Card issued successfully. The PAN is masked for security — only the last 4 digits are returned.

```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "panMasked": "****1234",
  "expiryDate": "03/27",
  "status": "Active"
}
```

**Response Headers:**
```
Location: /api/cards/issue?id=a1b2c3d4-e5f6-7890-abcd-ef1234567890
```

#### Error Response — `400 Bad Request` (Invalid BIN Prefix)

BIN prefix contains non-digit characters or is out of valid length range:

```json
{
  "error": "BIN prefix must contain only digits."
}
```

#### Error Response — `400 Bad Request` (Invalid PAN Length)

PAN length is outside the 16-19 range:

```json
{
  "error": "PAN length must be between 16 and 19. Got 12."
}
```

#### Error Response — `400 Bad Request` (BIN Prefix Too Long)

BIN prefix length exceeds PAN length:

```json
{
  "error": "BIN prefix length must be less than PAN length."
}
```

#### Error Response — `400 Bad Request` (Service Failure)

HSM or infrastructure failure during card generation:

```json
{
  "error": "HSM service unavailable: failed to generate PAN",
  "code": "HSM_UNAVAILABLE"
}
```

#### Error Response — `400 Bad Request` (Model Validation)

Missing or malformed request body fields:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "AccountId": ["The AccountId field is required."],
    "BinPrefix": ["The BinPrefix field is required."]
  }
}
```

---

### Get Account Balance

Retrieves the current balance for a specified account.

```
GET /api/accounts/{accountId}/balance
```

#### Path Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `accountId` | GUID | The unique identifier of the account |

#### Example Request

```bash
curl -k https://localhost/api/accounts/3fa85f64-5717-4562-b3fc-2c963f66afa6/balance
```

#### Success Response — `200 OK`

Account found and balance retrieved:

```json
{
  "accountId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "balance": 1500000,
  "currency": "NGN"
}
```

> Note: `balance` is in the smallest currency unit (e.g., kobo for NGN, cents for USD). A balance of `1500000` in NGN equals ₦15,000.00.

#### Error Response — `404 Not Found`

Account does not exist:

```json
{
  "error": "Account not found",
  "code": "ACCOUNT_NOT_FOUND"
}
```

#### Error Response — `404 Not Found` (Ledger Error)

Account exists but ledger entries are inaccessible:

```json
{
  "error": "Unable to compute balance: ledger entries unavailable",
  "code": "LEDGER_ERROR"
}
```

---

### Initiate Payment

Routes a payment request through fraud checking, idempotency validation, and the appropriate channel adapter.

```
POST /api/payments
```

#### Request Headers

| Header | Value | Required |
|--------|-------|----------|
| Content-Type | application/json | Yes |
| X-Idempotency-Key | Unique client-generated key | Yes |

#### Request Body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `transactionType` | string (enum) | Yes | One of: `InterbankTransfer`, `QRPayment`, `BillPayment`, `USSDPayment`, `RecurringDebit`, `BulkPayment`, `CardAuthorization` |
| `amount` | integer (long) | Yes | Amount in smallest currency unit (e.g., kobo) |
| `currencyCode` | string | Yes | ISO 4217 currency code (e.g., `NGN`) |
| `sourceAccount` | string | Yes | Source account identifier or tokenized card PAN |
| `destinationAccount` | string | Yes | Destination account identifier |
| `channel` | string (enum) | Yes | One of: `NIP`, `NQR`, `EBillsPay`, `MCash`, `DirectDebit`, `GAPS`, `Interswitch`, `Cardify` |

#### Example Request

```bash
curl -k -X POST https://localhost/api/payments \
  -H "Content-Type: application/json" \
  -H "X-Idempotency-Key: $(uuidgen)" \
  -d '{
    "transactionType": "InterbankTransfer",
    "amount": 500000,
    "currencyCode": "NGN",
    "sourceAccount": "058:1234567890",
    "destinationAccount": "044:9876543210",
    "channel": "NIP"
  }'
```

#### Success Response — `201 Created`

```json
{
  "transactionReference": "TXN-abc123def456",
  "status": "Completed",
  "success": true,
  "errorMessage": null,
  "processorReference": "NIBSS-REF-789"
}
```

#### Error Response — `400 Bad Request` (Missing Idempotency Key)

```json
{
  "error": "X-Idempotency-Key header is required."
}
```

---

### Get Payment Status

```
GET /api/payments/{transactionReference}
```

Returns the current status of a payment by its transaction reference.

#### Success Response — `200 OK`

```json
{
  "transactionReference": "TXN-abc123def456",
  "status": "Completed",
  "channel": "NIP",
  "lastStateChangeUtc": "2026-06-24T10:30:00Z",
  "processorReference": "NIBSS-REF-789",
  "found": true
}
```

---

### Get Payment Status by Idempotency Key

```
GET /api/payments/by-key/{idempotencyKey}
```

Returns payment status using the original idempotency key.

---

### Raise Dispute

Creates a dispute against a previously completed transaction.

```
POST /api/disputes
```

#### Request Body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `transactionReference` | string | Yes | Reference of the disputed transaction |
| `reasonCode` | string | Yes | Dispute reason (e.g., `UNAUTHORIZED`, `DUPLICATE`, `INCORRECT_AMOUNT`) |
| `amount` | object | Yes | `{ "amount": 50000, "currencyCode": "NGN" }` |
| `evidence` | string | No | Supporting evidence text |

#### Success Response — `201 Created`

```json
{
  "id": "d1e2f3a4-b5c6-7890-abcd-ef1234567890",
  "status": "Opened",
  "success": true,
  "errorMessage": null
}
```

---

### Resolve Dispute

```
PUT /api/disputes/{id}/resolve
```

#### Request Body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `decision` | string (enum) | Yes | `InFavour` or `Against` |
| `notes` | string | No | Resolution notes |

Resolution in favour automatically initiates a credit to the cardholder. Resolution against closes the dispute record.

---

### Get Dispute

```
GET /api/disputes/{id}
```

Returns dispute details including current lifecycle status.

---

### Error Response Format

All error responses follow a consistent structure:

#### Domain/Application Errors

Returned by business logic when operations fail:

```json
{
  "error": "Human-readable error description",
  "code": "MACHINE_READABLE_ERROR_CODE"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `error` | string | Human-readable error message |
| `code` | string (nullable) | Machine-readable error code for programmatic handling |

#### Known Error Codes

| Code | Description | Endpoint |
|------|-------------|----------|
| `HSM_UNAVAILABLE` | Hardware Security Module is unreachable | POST /api/cards/issue |
| `HSM_OPERATION_FAILED` | HSM operation (PAN/CVV generation) failed | POST /api/cards/issue |
| `ACCOUNT_NOT_FOUND` | Account ID does not exist | GET /api/accounts/{id}/balance |
| `LEDGER_ERROR` | Balance calculation failed | GET /api/accounts/{id}/balance |
| `CARD_LIMIT_EXCEEDED` | Account has reached maximum card count | POST /api/cards/issue |
| `CHANNEL_UNAVAILABLE` | Payment channel circuit breaker is open | POST /api/payments |
| `PCI_ACCESS_DENIED` | Service not authorized to access cardholder data | POST /api/payments |
| `INVALID_TOKEN` | Cardholder data token could not be resolved | POST /api/payments |
| `SYSTEM_ERROR` | Unexpected internal error during processing | POST /api/payments |

#### Validation Errors (ASP.NET Model Binding)

Returned when the request body fails model validation:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "FieldName": ["Error message for this field"]
  }
}
```

#### Infrastructure Errors

| HTTP Status | Cause |
|-------------|-------|
| `502 Bad Gateway` | App service is unreachable (nginx cannot proxy) |
| `503 Service Unavailable` | Health check failing (app is unhealthy) |
| `504 Gateway Timeout` | App service timed out responding |

---

## Swagger / OpenAPI

The API includes interactive documentation powered by Swashbuckle (Swagger):

| Resource | URL |
|----------|-----|
| Swagger UI | https://localhost/swagger |
| OpenAPI JSON | https://localhost/swagger/v1/swagger.json |

Swagger UI provides:
- Interactive "Try it out" functionality for all endpoints
- Request/response schema documentation
- Enum value listings (e.g., CardScheme options)
- XML documentation comments from source code

---

## ISO 8583 TCP Interface

The application listens on an internal TCP port (9090) for ISO 8583 payment messages. This is exposed through nginx on port **9443** with TLS termination.

Connect with any TLS-capable TCP client:

```bash
openssl s_client -connect localhost:9443
```

The TCP interface handles:
- Transaction authorization requests
- Transaction reversal requests
- Network management messages (sign-on, echo, sign-off)

Additionally, the Interswitch and Cardify adapters maintain persistent outbound TCP connections to card switch networks for ISO 8583 authorization, capture, and reversal messages. These connections use the `OutboundConnectionPool` with automatic reconnection (exponential backoff) and optional TLS.

---

## Configuration

The application is configured via environment variables in `docker-compose.yml` and JSON configuration in `appsettings.json`:

### Core Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `DATABASE__CONNECTION_STRING` | PostgreSQL connection string | `Host=postgres;Port=5432;...` |
| `TCP__LISTENER_PORT` | ISO 8583 TCP listener port | `9090` |
| `HSM__ENDPOINT` | HSM service endpoint | `http://hsm-mock:8080` |
| `HSM__SLOT_ID` | HSM slot identifier | — |
| `HSM__PIN` | HSM authentication PIN | — |
| `HSM__AUTH_TIMEOUT_SECONDS` | HSM auth operation timeout | — |
| `HSM__OPERATION_TIMEOUT_SECONDS` | HSM crypto operation timeout | — |
| `KAFKA__BOOTSTRAP_SERVERS` | Kafka broker addresses | `kafka:9092` |
| `KAFKA__TOPIC_PREFIX` | Prefix for Kafka event topics | `cardmgmt.events` |
| `KAFKA__CLIENT_ID` | Kafka producer client ID | `card-management-api` |
| `DeveloperPortal__ApiKeyCache__RedisConnectionString` | Redis connection for API key cache | `redis:6379` |
| `Notifications__WhatsApp__BaseUrl` | WAHA WhatsApp API base URL | `http://waha:3000` |

### Payment Integration Configuration (appsettings.json)

Payment channels and services are configured in `appsettings.json` sections:

| Section | Description |
|---------|-------------|
| `NIP` | NIBSS Instant Payment (BaseUrl, ApiKey, TimeoutMs, StatusInquiryDelayMs) |
| `NQR` | NIBSS Quick Response (BaseUrl, ApiKey, DynamicQrExpiryMinutes) |
| `EBillsPay` | e-BillsPay (BaseUrl, ApiKey, BillerDirectoryCacheTtlMinutes) |
| `MCash` | mCash/USSD (BaseUrl, ApiKey, SessionTimeoutMs) |
| `DirectDebit` | Direct Debit (BaseUrl, ApiKey, MaxRetries, RetryIntervalHours) |
| `GAPS` | Bulk Payments (BaseUrl, ApiKey, MaxBatchSize, StatusPollIntervalMs) |
| `Interswitch` | Verve card switch (Host, Port, TimeoutMs, ReconnectDelayMs, TlsEnabled, TerminalId, MerchantId) |
| `Cardify` | Visa/Mastercard switch (Host, Port, TimeoutMs, ReconnectDelayMs, TlsEnabled, TerminalId, MerchantId) |
| `CircuitBreaker` | Resilience (FailureThreshold, OpenDurationSeconds, HalfOpenProbeCount) |
| `FraudRisk` | Risk engine (BlockThreshold, ReviewThreshold, FallbackPolicy, MaxEvaluationTimeMs) |
| `DurableProcessor` | Kafka consumer (ConsumerGroup, Topics, MaxRetries, InitialRetryDelayMs, DeadLetterTopicSuffix) |
| `PciSecurity` | PCI boundary (EncryptionKey, AllowedServices) |
| `Dispute` | Disputes (MaxDisputeWindowDays, KafkaTopic, ChannelTimeframeDays per channel) |
| `ReconciliationWorkerPool` | Worker pool (Concurrency, Capacity) |
| `WebhookDelivery` | Webhook delivery (Concurrency, BaseDelayMs, BackoffMultiplier, MaxRetries, SuspensionThreshold) |
| `NotificationWorkerPool` | Notification dispatch (Concurrency, Capacity) |
| `Notifications` | Email (SMTP), SMS (API), WhatsApp (WAHA BaseUrl, Session) |
| `DeveloperPortal` | API key cache (RedisConnectionString, TTL), request log retention, key/subscription limits |
| `AdminConsole` | Command expiry, read model staleness threshold |
| `AdminReadModelKafkaConsumer` | Read model projector Kafka consumer group and topics |

Development overrides are in `appsettings.Development.json` with sandbox URLs, relaxed timeouts, and TLS disabled for local testing.

---

## Nginx Proxy Configuration

The nginx configuration (`nginx/nginx.conf`) handles two traffic types:

- **HTTP block** — Listens on port 443 with SSL, proxies to the app on port 8080. Injects `X-Forwarded-For`, `X-Forwarded-Proto`, and `Host` headers.
- **Stream block** — Listens on port 9443 with SSL, proxies raw TCP to the app on port 9090 (layer 4, no header manipulation).

TLS is configured with protocols TLSv1.2 and TLSv1.3 only.

---

## Development

### Building Locally (without Docker)

```bash
dotnet restore
dotnet build -c Release
```

### Running Locally

```bash
# Start infrastructure only
docker compose up postgres kafka redis waha -d

# Run the app
cd src/CardManagement.Api
dotnet run
```

The app listens on `http://localhost:8080` when running locally outside Docker. Swagger UI is available at `http://localhost:8080/swagger`.

### Running Tests

The project uses xUnit with FsCheck for property-based testing:

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"

# Run a specific test category
dotnet test --filter "FullyQualifiedName~ValueObjects"

# Run payment integration tests
dotnet test --filter "FullyQualifiedName~Integration"
```

Test categories:
- **Domain** — Domain entity and service logic
- **Infrastructure** — Unit tests for adapters (Interswitch, Cardify, NIP, etc.), services (Dispute, Fraud, Saga), circuit breakers, idempotency guard, audit store
- **Integration** — API endpoint tests using WebApplicationFactory (payments, disputes, health checks)
- **ValueObjects** — Property-based tests for BinRange, PAN, Money, and other value objects
- **ArchitectureTests** — Enforces dependency rules between layers (Domain has no infrastructure references, etc.)

### Regenerating Certificates

Certificates expire after 365 days. To regenerate:

```bash
bash nginx/generate-certs.sh
docker compose restart nginx
```

---

## Observability (RED & USE)

The system uses OpenTelemetry for instrumentation, Prometheus for metrics collection, and Grafana for visualization. Two complementary methodologies are implemented:

- **RED** (Rate, Errors, Duration) — for request-driven services (the API layer)
- **USE** (Utilization, Saturation, Errors) — for resource-driven components (thread pool, memory, connections)

### Accessing Dashboards

| Resource | URL | Credentials |
|----------|-----|-------------|
| Grafana | http://localhost:3001 | admin / admin |
| Prometheus | http://localhost:9090 | — |
| App Metrics (raw) | http://localhost:8080/metrics (internal) | — |

Grafana loads with a pre-provisioned **Card Management — RED & USE Dashboard** as the home dashboard, using dark theme.

### RED Metrics (Request-Driven)

| Metric | What it measures | PromQL |
|--------|------------------|--------|
| Rate | Requests per second by endpoint | `sum(rate(http_server_request_duration_seconds_count[5m])) by (http_route)` |
| Errors | 5xx error rate | `sum(rate(...{http_response_status_code=~"5.."}[5m]))` |
| Duration | p95/p99 latency | `histogram_quantile(0.95, sum(rate(..._bucket[5m])) by (le, http_route))` |

### USE Metrics (Resource-Driven)

| Resource | Utilization | Saturation | Errors |
|----------|-------------|------------|--------|
| .NET Heap | `gc_heap_size_bytes` | GC frequency (Gen2 collections) | OOM exceptions |
| Thread Pool | Active thread count | Queue length (pending work items) | Thread starvation |
| HTTP Connections | Active outbound requests | Connection pool exhaustion | Timeouts / 5xx |

### Dashboard Panels

The pre-provisioned Grafana dashboard includes:

1. **Request Rate** — Time series of req/s by endpoint
2. **Error Rate (5xx)** — Server errors per second
3. **Request Duration (p95/p99)** — Latency percentiles
4. **Error Rate %** — Stat panel with thresholds (green/yellow/red)
5. **Total Throughput** — Aggregate req/s gauge
6. **Average Latency** — Stat with threshold coloring
7. **Active Requests** — In-flight request gauge
8. **Heap Memory** — .NET GC heap utilization over time
9. **Thread Pool** — Queue length + thread count (saturation indicator)
10. **GC Collections** — Gen0/Gen1/Gen2 collection rates
11. **Outbound HTTP Rate** — Calls to WAHA, SMS, webhooks by host
12. **Outbound HTTP Duration** — p95 latency for external calls
13. **Outbound HTTP Errors** — Failed outbound requests
14. **HTTP Status Codes (Stacked)** — 2xx/4xx/5xx breakdown
15. **Latency Heatmap (p50/p90/p99)** — Percentile overlay

### File Structure

```
observability/
├── prometheus/
│   └── prometheus.yml          # Scrape config (5s interval)
└── grafana/
    ├── grafana.ini             # Dark theme, anonymous access
    ├── provisioning/
    │   ├── datasources/
    │   │   └── prometheus.yml  # Auto-provision Prometheus datasource
    │   └── dashboards/
    │       └── dashboards.yml  # Dashboard file provider config
    └── dashboards/
        └── red-use-dashboard.json  # Pre-built RED & USE dashboard
```

---

## Networking

All services communicate over the `cardmgmt-network` Docker bridge network. Service discovery uses Docker DNS (container names as hostnames).

| From | To | Protocol | Port |
|------|-----|----------|------|
| nginx | card-management-app | HTTP | 8080 |
| nginx | card-management-app | TCP | 9090 |
| card-management-app | postgres | TCP | 5432 |
| card-management-app | kafka | TCP | 9092 |
| card-management-app | redis | TCP | 6379 |
| card-management-app | waha | HTTP | 3000 |
| card-management-app | hsm-mock | HTTP | 8080 |
| prometheus | card-management-app | HTTP | 8080 |
| grafana | prometheus | HTTP | 9090 |

---

## Kubernetes Deployment

The `k8s/` directory contains production-ready Kubernetes manifests equivalent to the docker-compose setup. Uses Kustomize for composition.

### Structure

```
k8s/
├── kustomization.yaml    # Kustomize root — ties all manifests together
├── deploy.sh             # Helper script (build, apply, delete, status)
├── namespace.yaml        # cardmgmt namespace
├── secrets.yaml          # PostgreSQL creds, Grafana creds, TLS cert
├── configmaps.yaml       # nginx.conf, prometheus.yml, grafana provisioning
├── pvcs.yaml             # Persistent volume claims (postgres, redis, kafka, prometheus, grafana)
├── postgres.yaml         # PostgreSQL Deployment + Service
├── kafka.yaml            # Kafka (KRaft mode) Deployment + Service
├── redis.yaml            # Redis Deployment + Service
├── waha.yaml             # WAHA WhatsApp Deployment + Service
├── app.yaml              # Card Management App Deployment (2 replicas) + Service
├── nginx.yaml            # Nginx Deployment (2 replicas) + LoadBalancer Service
├── prometheus.yaml       # Prometheus Deployment + Service
└── grafana.yaml          # Grafana Deployment + Service
```

### Quick Start

```bash
# 1. Build the app image (if not using a registry)
./k8s/deploy.sh build

# 2. Apply all manifests
./k8s/deploy.sh apply

# 3. Check status
./k8s/deploy.sh status
```

### Port Forwarding (local access)

```bash
# HTTPS API (via nginx)
kubectl -n cardmgmt port-forward svc/nginx 443:443

# Grafana dashboards
kubectl -n cardmgmt port-forward svc/grafana 3001:3000

# Prometheus UI
kubectl -n cardmgmt port-forward svc/prometheus 9090:9090
```

### Key Differences from docker-compose

| Aspect | docker-compose | Kubernetes |
|--------|---------------|------------|
| App replicas | 1 | 2 (scalable via HPA) |
| Nginx replicas | 1 | 2 |
| Ingress | Published ports | LoadBalancer Service |
| Secrets | Inline env vars | K8s Secrets (base64) |
| Storage | Docker volumes | PersistentVolumeClaims |
| Config files | Bind mounts | ConfigMaps |
| TLS certs | File mount | kubernetes.io/tls Secret |
| Health checks | Docker HEALTHCHECK | readiness + liveness probes |

### Cleanup

```bash
./k8s/deploy.sh delete
```

---

## Terraform (AKS)

The `terraform/` directory provisions the full stack on Azure Kubernetes Service with managed backing services:

- **AKS Cluster** — 2-5 node auto-scaling pool (Standard_D4s_v3)
- **Azure Database for PostgreSQL** — Flexible Server v16 (VNet-integrated)
- **Azure Cache for Redis** — Managed Redis with TLS
- **Azure Container Registry** — Private Docker image hosting
- **Kafka** — Bitnami Helm chart (in-cluster, KRaft mode)
- **WAHA** — K8s Deployment
- **kube-prometheus-stack** — Prometheus + Grafana with RED/USE dashboards
- **NGINX Ingress Controller** — TLS termination and routing

```bash
cd terraform
cp terraform.tfvars.example terraform.tfvars
# Edit terraform.tfvars
terraform init
terraform plan -out=tfplan
terraform apply tfplan
```

See [`terraform/README.md`](terraform/README.md) for full setup instructions and architecture details.

---

## Git Workflow & CI/CD

The project uses a three-tier branch strategy with automated CI gates at each level.

### Branch Flow

```
feature/* ──PR──► dev ──PR──► staging ──PR──► prod
                   │              │               │
              build + test   build + test     build + test
              (all tests)    + docker build   + push to ACR
                                              + deploy to AKS
```

### Rules

| Branch | Accepts PRs from | CI Gate | Direct Push |
|--------|-----------------|---------|-------------|
| `dev` | Any feature branch | Build + all tests must pass | ❌ Blocked |
| `staging` | `dev` only | Build + tests + Docker image build | ❌ Blocked |
| `prod` | `staging` only | Build + tests + push image + deploy to AKS | ❌ Blocked |

### Developer Workflow

```bash
# 1. Create a feature branch from dev
git checkout dev
git pull origin dev
git checkout -b feature/my-change

# 2. Make changes, commit, push
git add .
git commit -m "feat: add new endpoint"
git push -u origin feature/my-change

# 3. Open PR to dev — CI runs tests automatically
#    If tests pass → PR can be merged
#    If tests fail → PR is blocked until fixed

# 4. After merging to dev, open PR from dev → staging
# 5. After staging validation, open PR from staging → prod
```

### CI Workflows

| Workflow | Trigger | Steps |
|----------|---------|-------|
| `ci-dev.yml` | PR → `dev` | Restore → Build → Unit tests → Integration tests |
| `ci-staging.yml` | PR → `staging` | Validate source (must be `dev`) → Build → All tests → Docker build |
| `ci-prod.yml` | PR → `prod` | Validate source (must be `staging`) → Build → All tests → Push to ACR → Deploy to AKS |

### Required GitHub Secrets

| Secret | Used in | Purpose |
|--------|---------|---------|
| `ACR_USERNAME` | ci-prod | Azure Container Registry username |
| `ACR_PASSWORD` | ci-prod | Azure Container Registry password |
| `AZURE_CREDENTIALS` | ci-prod | Service principal JSON for AKS deployment |

### Branch Protection Setup

See [`.github/branch-protection.md`](.github/branch-protection.md) for full configuration instructions and GitHub CLI commands to set up branch protection rules.

---

## Troubleshooting

### nginx fails to start

Ensure certificates exist. Run `bash nginx/generate-certs.sh` before starting the stack.

### App health check fails

Check that PostgreSQL and Kafka are healthy first:

```bash
docker compose ps
docker compose logs card-management-app
```

### TLS certificate warnings

The certificates are self-signed. Use `-k` with curl or configure your client to trust `nginx/certs/selfsigned.crt`.

### Port conflicts

If ports 443, 3000, 3001, 5432, 6379, 9090, 9092, or 9443 are already in use, either stop the conflicting service or modify the port mappings in `docker-compose.yml`.

### Swagger UI not loading

Ensure the app is healthy and nginx is proxying correctly:

```bash
curl -k https://localhost/swagger/v1/swagger.json
```

If this returns a JSON document, the API docs are working — try clearing your browser cache.

---

## License

Proprietary — internal use only.
