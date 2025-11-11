using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Outbox.Infrastructure.Persistence;
using Outbox.Infrastructure.Repository;
using Outbox.Model;
using Xunit;

namespace Outbox.Tests.Infrastructure.Repository;

[Trait("Category", "Unit")]
public class PackageRepositoryTests : IDisposable
{
    private readonly PackageDbContext _dbContext;
    private readonly PackageRepository _repository;

    public PackageRepositoryTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<PackageDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new PackageDbContext(options);

        // Create system under test
        _repository = new PackageRepository(_dbContext);
    }

    private static Package CreateTestPackage(string trackingCode)
    {
        var package = new Package(
            TrackingCode: trackingCode,
            ParcelShopId: Guid.NewGuid(),
            SenderId: Guid.NewGuid(),
            OriginAddressId: Guid.NewGuid(),
            DestinationAddressId: Guid.NewGuid(),
            WeightKg: 2.5m
        )
        {
            Id = Guid.NewGuid()
        };
        
        return package;
    }
    
    [Fact]
    public async Task GetByTrackingCodeAsync_ShouldReturnPackage_WhenExists()
    {
        // Arrange
        const string trackingCode = "CTT-9Z-12345678";
        var package = CreateTestPackage(trackingCode);
        
        _dbContext.Packages.Add(package);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _repository.GetByTrackingCodeAsync(trackingCode);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(trackingCode, result.TrackingCode);
        Assert.Equal(package.Id, result.Id);
    }

    [Fact]
    public async Task GetByTrackingCodeAsync_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        const string trackingCode = "CTT-9Z-99999999";

        // Act
        var result = await _repository.GetByTrackingCodeAsync(trackingCode);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByTrackingCodeAsync_ShouldReturnAsNoTracking_WhenCalled()
    {
        // Arrange
        const string trackingCode = "CTT-9Z-12345678";
        var package = CreateTestPackage(trackingCode);
        
        _dbContext.Packages.Add(package);
        await _dbContext.SaveChangesAsync();
        
        // Act
        var result = await _repository.GetByTrackingCodeAsync(trackingCode);

        // Assert
        Assert.NotNull(result);
        var entry = _dbContext.Entry(result);
        Assert.Equal(EntityState.Detached, entry.State);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnPackage_WhenExists()
    {
        // Arrange
        var packageId = Guid.NewGuid();
        var package = CreateTestPackage("CTT-9Z-12345678") with
        {
            Id = packageId
        };

        _dbContext.Packages.Add(package);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdAsync(packageId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(packageId, result.Id);
        Assert.Equal(package.TrackingCode, result.TrackingCode);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        var packageId = Guid.NewGuid();

        // Act
        var result = await _repository.GetByIdAsync(packageId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_ShouldAddPackageToContext()
    {
        // Arrange
        var package = CreateTestPackage("CTT-9Z-12345678");

        // Act
        var result = await _repository.CreateAsync(package);

        // Assert
        Assert.Equal(package, result);
        var entry = _dbContext.Entry(package);
        Assert.Equal(EntityState.Added, entry.State);
    }

    [Fact]
    public async Task CreateAsync_ShouldNotSaveChanges_WhenCalled()
    {
        // Arrange
        var package = CreateTestPackage("CTT-9Z-12345678");

        // Act
        await _repository.CreateAsync(package);

        // Assert - package should be in Added state but not saved yet
        var packageInDb = await _dbContext.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == package.Id);
        Assert.Null(packageInDb); // Not saved yet
    }

    [Fact]
    public async Task UpdateAsync_ShouldMarkPackageAsModified()
    {
        // Arrange
        var package = CreateTestPackage("CTT-9Z-12345678") with
        {
            Id = Guid.NewGuid(),
            CurrentStatus = PackageStatus.Created
        };

        _dbContext.Packages.Add(package);
        await _dbContext.SaveChangesAsync();
        _dbContext.Entry(package).State = EntityState.Detached;

        var updatedPackage = package with
        {
            CurrentStatus = PackageStatus.InTransit,
            CurrentHubId = Guid.NewGuid()
        };

        // Act
        await _repository.UpdateAsync(updatedPackage);

        // Assert
        var entry = _dbContext.Entry(updatedPackage);
        Assert.Equal(EntityState.Modified, entry.State);
    }

    [Fact]
    public async Task UpdateAsync_ShouldNotSaveChanges_WhenCalled()
    {
        // Arrange
        var package = CreateTestPackage("CTT-9Z-12345678") with
        {
            Id = Guid.NewGuid(),
            CurrentStatus = PackageStatus.Created
        };

        _dbContext.Packages.Add(package);
        await _dbContext.SaveChangesAsync();
        _dbContext.Entry(package).State = EntityState.Detached;

        var updatedPackage = package with
        {
            CurrentStatus = PackageStatus.InTransit
        };

        // Act
        await _repository.UpdateAsync(updatedPackage);

        // Assert - package should be modified but not saved yet
        var packageInDb = await _dbContext.Packages.AsNoTracking().FirstAsync(p => p.Id == package.Id);
        Assert.Equal(PackageStatus.Created, packageInDb.CurrentStatus); // Still old status
    }

    [Fact]
    public async Task CreateAsync_ShouldPreserveAllPackageProperties()
    {
        // Arrange
        var parcelShopId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var originAddressId = Guid.NewGuid();
        var destinationAddressId = Guid.NewGuid();
        var trackingCode = "CTT-9Z-12345678";
        var weightKg = 5.5m;

        var package = new Package(
            TrackingCode: trackingCode,
            ParcelShopId: parcelShopId,
            SenderId: senderId,
            OriginAddressId: originAddressId,
            DestinationAddressId: destinationAddressId,
            WeightKg: weightKg
        )
        {
            Id = Guid.NewGuid()
        };

        // Act
        await _repository.CreateAsync(package);
        await _dbContext.SaveChangesAsync();

        // Assert
        var savedPackage = await _dbContext.Packages.AsNoTracking().FirstAsync();
        Assert.Equal(trackingCode, savedPackage.TrackingCode);
        Assert.Equal(parcelShopId, savedPackage.ParcelShopId);
        Assert.Equal(senderId, savedPackage.SenderId);
        Assert.Equal(originAddressId, savedPackage.OriginAddressId);
        Assert.Equal(destinationAddressId, savedPackage.DestinationAddressId);
        Assert.Equal(weightKg, savedPackage.WeightKg);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
    }
}

