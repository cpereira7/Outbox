# Outbox Pattern Sample Project

Simple sample that demonstrates the Transactional Outbox pattern using PostgreSQL.

## Overview

This project implements the Transactional Outbox pattern to ensure reliable message delivery in a distributed system. It uses PostgreSQL with native optimistic concurrency control and includes a simple background worker that processes outbox messages.

## What this project shows

- How to implement a transactional outbox to decouple database changes and message publishing
- A simple background worker that processes messages from the outbox
- Using optimistic concurrency to avoid duplicate processing

## Project layout

The repository includes the main API project and a test project; relevant folders include:

- `Outbox/` — API, infrastructure, model and background processor
- `Outbox.Tests/` — unit tests and integration tests using Testcontainers.

## More details

For implementation details and concurrency flow, see [OPTIMISTIC_CONCURRENCY_FLOW.md](OPTIMISTIC_CONCURRENCY_FLOW.md).

## License

See [LICENSE](LICENSE) for license details.
