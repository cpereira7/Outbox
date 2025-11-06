# Outbox Pattern Sample Project

Sample project demonstrating the Outbox pattern implementation with PostgreSQL and optimistic concurrency control.

## Overview

This project implements the Transactional Outbox pattern to ensure reliable message delivery in a distributed system. It uses PostgreSQL with native optimistic concurrency control via the `xmin` system column.

## Key Features

- **Transactional Outbox Pattern**: Ensures atomicity between database writes and message publishing
- **PostgreSQL Database**: Production-ready relational database with advanced features
- **Optimistic Concurrency**: Uses PostgreSQL's `xmin` for conflict detection
- **Docker Support**: Easy local development with containerized PostgreSQL
- **Comprehensive Tests**: 32 passing unit tests
- **Background Processing**: Hosted service for processing outbox messages

## Quick Start

### 1. Start PostgreSQL

Using Docker in WSL:

```bash
wsl docker compose up -d
```

### 2. Apply Database Migrations

```bash
cd SampleStack.Outbox\Outbox
dotnet ef database update
```

### 3. Run the Application

```bash
dotnet run
```

The API will be available at `https://localhost:5001` (or the port specified in launchSettings.json).

### 4. Run Tests

```bash
dotnet test
```

## Architecture

### Outbox Pattern Flow

1. **Create/Update Package** → Saves Package + OutboxMessage in single transaction
2. **Background Processor** → Polls for unprocessed OutboxMessages
3. **Process Message** → Calls notification service and marks message as processed
4. **Optimistic Concurrency** → Prevents duplicate processing using `xmin`

### Database Schema

- **Packages**: Core package entities
- **OutboxMessages**: Transactional outbox for reliable message delivery
- **__EFMigrationsHistory**: Entity Framework migration tracking

## Technologies

- **.NET 9.0**
- **Entity Framework Core 9.0.8**
- **PostgreSQL 16** (Alpine Linux)
- **Npgsql** (PostgreSQL provider for EF Core)
- **xUnit** (Testing framework)
- **Docker & Docker Compose**

## Project Structure

``` text
SampleStack.Outbox/
├── Outbox/                    # Main API project
│   ├── Api/                   # Controllers and DTOs
│   ├── Infrastructure/        # Database and messaging infrastructure
│   │   ├── PackageQueue/      # Outbox message handling
│   │   ├── Persistence/       # DbContext configuration
│   │   ├── Processor/         # Background message processor
│   │   └── Repository/        # Data access layer
│   ├── Model/                 # Domain entities
│   ├── Service/               # Business logic
│   └── Migrations/            # EF Core migrations
└── Outbox.Tests/              # Unit tests
```

## Configuration

Database connection string in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=outbox_db;Username=outbox_user;Password=outbox_pass"
  }
}
```

⚠️ **Note**: Change credentials for production use!

## API Endpoints

- `POST /api/Package` - Create a new package
- `GET /api/Package/{trackingCode}` - Get package by tracking code
- `POST /api/Package/update` - Update package status

## Optimistic Concurrency

The `OutboxMessage` entity uses PostgreSQL's `xmin` system column for optimistic concurrency control. This prevents:

- Duplicate message processing
- Lost updates
- Race conditions in distributed scenarios

See [OPTIMISTIC_CONCURRENCY_FLOW.md](OPTIMISTIC_CONCURRENCY_FLOW.md) for detailed explanation.

## License

See [LICENSE](LICENSE) file for details.
