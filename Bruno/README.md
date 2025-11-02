# Bruno API Tests

This directory contains API tests for the Outbox application using [Bruno](https://www.usebruno.com/), an open-source API testing tool.

## Running Tests Locally

### Prerequisites
- Node.js 20 or later
- .NET 9.0 SDK
- Bruno CLI (`npm install -g @usebruno/cli`)

### Steps to Run Tests

1. **Build and run the API:**
   ```bash
   # From the repository root
   dotnet restore SampleStack.sln
   dotnet build SampleStack.sln --configuration Release
   
   # Install EF Core tools if not already installed
   dotnet tool install --global dotnet-ef
   
   # Apply database migrations
   cd SampleStack.Outbox/Outbox
   dotnet ef database update --configuration Release
   
   # Start the API
   ASPNETCORE_URLS=http://localhost:5000 dotnet run --configuration Release --no-launch-profile
   ```

2. **Run Bruno tests (in a separate terminal):**
   ```bash
   # From the Bruno/Outbox directory
   cd Bruno/Outbox
   bru run . --env-var baseUrl=http://localhost:5000 -r
   ```

## GitHub Actions

The API tests are automatically run on GitHub Actions when:
- Code is pushed to the `main` or `develop` branches
- A pull request is opened against `main` or `develop`
- The workflow is manually triggered

### Workflow: `.github/workflows/bruno-api-tests.yml`

The workflow:
1. Checks out the code
2. Sets up .NET 9.0
3. Restores dependencies and builds the solution
4. Applies database migrations
5. Starts the API in the background
6. Installs Bruno CLI
7. Runs the Bruno tests
8. Uploads test results as artifacts

### Viewing Test Results

After the workflow runs, you can:
- View the test results in the workflow run summary
- Download the test results artifact (`bruno-test-results`) which contains a JUnit XML report

## Test Collection Structure

```
Bruno/Outbox/
├── bruno.json          # Collection configuration
├── collection.bru      # Collection metadata
└── Package/            # Package API tests
    ├── folder.bru
    ├── post -api-Package.bru              # Create package test
    ├── post -api-Package-update.bru       # Update package test
    └── get -api-Package--trackingCode.bru # Get package by tracking code test
```

## Adding New Tests

1. Open the collection in the Bruno desktop application
2. Add new requests/tests
3. The tests will automatically be picked up by the CLI
4. Commit the new `.bru` files to the repository

## Environment Variables

Tests use the following environment variables:
- `baseUrl`: The base URL of the API (e.g., `http://localhost:5000`)

In GitHub Actions, this is set to `http://localhost:5000` by default.
