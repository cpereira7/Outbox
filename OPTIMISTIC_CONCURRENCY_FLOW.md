# Optimistic Concurrency Flow with PostgreSQL xmin

## Scenario: Two Processes Try to Update the Same OutboxMessage

```
Time    Process A                           Process B                           Database (xmin)
────────────────────────────────────────────────────────────────────────────────────────────────
T0      Message exists                                                          xmin = 1000
        Id: abc-123
        IsCompleted: false
────────────────────────────────────────────────────────────────────────────────────────────────
T1      Read message                        Read message                        xmin = 1000
        xmin_A = 1000                       xmin_B = 1000
────────────────────────────────────────────────────────────────────────────────────────────────
T2      message.IsCompleted = true          (Processing...)                     xmin = 1000
        message.ProcessedAt = Now()
────────────────────────────────────────────────────────────────────────────────────────────────
T3      SaveChangesAsync()                  (Still processing...)               xmin = 1001
        ✅ SUCCESS
        UPDATE executed:
        WHERE Id = 'abc-123' 
        AND xmin = 1000
        
        PostgreSQL updates xmin → 1001
────────────────────────────────────────────────────────────────────────────────────────────────
T4      (Completed)                         message.IsCanceled = true           xmin = 1001
                                            SaveChangesAsync()
────────────────────────────────────────────────────────────────────────────────────────────────
T5                                          ❌ DbUpdateConcurrencyException     xmin = 1001
                                            UPDATE attempted:
                                            WHERE Id = 'abc-123' 
                                            AND xmin = 1000  ← Stale!
                                            
                                            0 rows affected
                                            Exception thrown
────────────────────────────────────────────────────────────────────────────────────────────────
T6                                          catch (DbUpdateConcurrencyException)
                                            {
                                              // Reload entity
                                              // Or retry logic
                                              // Or log conflict
                                            }
────────────────────────────────────────────────────────────────────────────────────────────────
```

## Code Flow

### Process A (Succeeds)
```csharp
// 1. Load entity
var message = await context.OutboxMessages
    .FirstOrDefaultAsync(m => m.Id == messageId);

// xmin is automatically tracked by EF Core
// Let's say xmin = 1000 when loaded

// 2. Modify entity
message.IsCompleted = true;
message.ProcessedAt = DateTimeOffset.UtcNow;

// 3. Save changes
await context.SaveChangesAsync();

// EF Core generates SQL:
// UPDATE "OutboxMessages"
// SET "IsCompleted" = true, 
//     "ProcessedAt" = '2025-11-05 23:42:35+00'
// WHERE "Id" = 'abc-123' 
//   AND "xmin" = 1000;  ← Concurrency check

// PostgreSQL executes update and changes xmin to 1001
// 1 row affected → Success!
```

### Process B (Fails - Concurrency Conflict)
```csharp
// 1. Load entity (same as Process A, earlier)
var message = await context.OutboxMessages
    .FirstOrDefaultAsync(m => m.Id == messageId);

// xmin = 1000 (same as Process A initially)

// 2. Modify entity
message.IsCanceled = true;

// 3. Save changes (but Process A already updated it)
try 
{
    await context.SaveChangesAsync();
    
    // EF Core generates SQL:
    // UPDATE "OutboxMessages"
    // SET "IsCanceled" = true
    // WHERE "Id" = 'abc-123' 
    //   AND "xmin" = 1000;  ← But xmin is now 1001!
    
    // PostgreSQL executes: 0 rows affected
    // EF Core throws DbUpdateConcurrencyException
}
catch (DbUpdateConcurrencyException ex)
{
    // Handle conflict
    _logger.LogWarning(
        "Concurrency conflict detected for message {MessageId}. " +
        "Another process may have already processed it.",
        messageId);
    
    // Option 1: Reload and retry
    await context.Entry(message).ReloadAsync();
    if (!message.IsCompleted) {
        message.IsCanceled = true;
        await context.SaveChangesAsync();
    }
    
    // Option 2: Just log and continue
    // The other process already handled it
    
    // Option 3: Throw and let caller decide
    throw;
}
```

## Actual Implementation in PackageEventQueueProcessor

```csharp
public class PackageEventQueueProcessor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PackageDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            // Get unprocessed messages
            var messages = await context.OutboxMessages
                .Where(m => m.ProcessedAt == null && !m.IsCanceled)
                .OrderBy(m => m.OccurredAt)
                .Take(10)
                .ToListAsync(stoppingToken);

            foreach (var message in messages)
            {
                try
                {
                    // Process the message
                    await notificationService.SendNotificationAsync(message.Payload);
                    
                    // Mark as processed
                    message.ProcessedAt = DateTimeOffset.UtcNow;
                    message.IsCompleted = true;
                    
                    // Save with optimistic concurrency check
                    await context.SaveChangesAsync(stoppingToken);
                    
                    // ✅ If this succeeds, we have exclusive ownership
                    // The xmin check prevents duplicate processing
                }
                catch (DbUpdateConcurrencyException)
                {
                    // ✅ Another instance already processed this message
                    // This is actually a success case - the message was processed
                    _logger.LogInformation(
                        "Message {MessageId} was already processed by another instance",
                        message.Id);
                    
                    // Reload to get latest state
                    await context.Entry(message).ReloadAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    // ❌ Actual error - mark as canceled
                    _logger.LogError(ex, "Error processing message {MessageId}", message.Id);
                    
                    message.IsCanceled = true;
                    await context.SaveChangesAsync(stoppingToken);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
```

## Benefits of xmin Approach

### ✅ Automatic Detection
```sql
-- PostgreSQL automatically updates xmin on every UPDATE
UPDATE "OutboxMessages" SET "IsCompleted" = true WHERE "Id" = 'abc-123';
-- xmin changes from 1000 → 1001 automatically
```

### ✅ No Additional Storage
```sql
-- xmin is a system column - no extra bytes needed
\d+ "OutboxMessages"
                                              Table "public.OutboxMessages"
   Column    |           Type           | Storage | Stats target | Description
-------------+--------------------------+---------+--------------+-------------
 Id          | uuid                     |         |              |
 ...         | ...                      |         |              |
 xmin        | xid                      |         |              | (system column)
```

### ✅ Transaction-Safe
```sql
-- Two transactions trying to update the same row:

-- Transaction 1:
BEGIN;
UPDATE "OutboxMessages" SET "IsCompleted" = true 
WHERE "Id" = 'abc-123' AND "xmin" = 1000;
-- Locks the row, xmin will be 1001 when committed
COMMIT;

-- Transaction 2 (happening concurrently):
BEGIN;
UPDATE "OutboxMessages" SET "IsCanceled" = true 
WHERE "Id" = 'abc-123' AND "xmin" = 1000;
-- Waits for Transaction 1's lock...
-- After Transaction 1 commits, xmin is 1001
-- WHERE clause fails (xmin != 1000)
-- 0 rows affected
ROLLBACK;
```

## Comparison with Other Approaches

### ❌ Without Optimistic Concurrency
```csharp
// DANGEROUS - No concurrency control
var message = await context.OutboxMessages.FindAsync(id);
message.IsCompleted = true;
await context.SaveChangesAsync();

// Problem: If two processes do this simultaneously:
// - Both read IsCompleted = false
// - Both set IsCompleted = true
// - Both save
// - Message gets processed TWICE! ❌
```

### ✅ With Timestamp RowVersion (SQL Server)
```csharp
// SQL Server approach with ROWVERSION
[Timestamp]
public byte[] RowVersion { get; set; }

// Generates:
CREATE TABLE OutboxMessages (
    ...
    RowVersion ROWVERSION NOT NULL
);

// Extra 8 bytes per row
// Automatic management by SQL Server
```

### ✅ With xmin (PostgreSQL)
```csharp
// PostgreSQL approach with xmin
[ConcurrencyCheck]
[DatabaseGenerated(DatabaseGeneratedOption.Computed)]
[Column("xmin")]
public uint RowVersion { get; set; }

// Fluent API configuration:
modelBuilder.Entity<OutboxMessage>()
    .Property(e => e.RowVersion)
    .HasColumnName("xmin")
    .HasColumnType("xid")
    .IsConcurrencyToken()
    .ValueGeneratedOnAddOrUpdate();

// Maps to system column xmin
// 0 bytes overhead
// Already exists in every row
// Automatic management by PostgreSQL
```

## Real-World Scenarios

### Scenario 1: Multiple Application Instances
```
Instance A (Server 1)    Instance B (Server 2)    Instance C (Server 3)
        |                        |                        |
        |--- Query messages -----|------------------------|
        |                        |                        |
        v                        v                        v
   [Msg 1, 2, 3]           [Msg 1, 2, 3]           [Msg 1, 2, 3]
        |                        |                        |
   Process Msg 1           Process Msg 1           Process Msg 2
        |                        |                        |
        v                        v                        v
    UPDATE (xmin)            UPDATE (xmin)           UPDATE (xmin)
        |                        |                        |
    ✅ Success              ❌ Conflict              ✅ Success
    xmin changes            0 rows updated          xmin changes
        |                        |                        |
        |                   Logs & continues              |
        |                        |                        |
   Process Msg 3                                     Process Msg 3
        |                                                  |
    ✅ Success                                        ❌ Conflict
```

Result: Each message processed exactly once!

### Scenario 2: Retry Logic
```csharp
async Task ProcessMessageWithRetry(Guid messageId, int maxRetries = 3)
{
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        using var context = CreateDbContext();
        var message = await context.OutboxMessages.FindAsync(messageId);
        
        if (message.IsCompleted)
        {
            // Already processed by another instance
            return;
        }
        
        try
        {
            await ProcessMessage(message);
            message.IsCompleted = true;
            message.ProcessedAt = DateTimeOffset.UtcNow;
            
            await context.SaveChangesAsync();
            return; // Success!
        }
        catch (DbUpdateConcurrencyException)
        {
            if (attempt == maxRetries)
                throw;
            
            // Reload and retry
            await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));
            continue;
        }
    }
}
```

## Summary

The PostgreSQL `xmin` approach provides:
- ✅ **Automatic** concurrency control
- ✅ **Zero overhead** (system column)
- ✅ **Transaction-safe** updates
- ✅ **Prevents duplicates** in distributed scenarios
- ✅ **Simple to implement** with EF Core
- ✅ **Production-proven** reliability

Perfect for the Outbox pattern where multiple instances might try to process the same message!

