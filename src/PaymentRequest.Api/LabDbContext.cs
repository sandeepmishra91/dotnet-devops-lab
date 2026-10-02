using Microsoft.EntityFrameworkCore;

namespace PaymentRequest.Api;

public sealed class PaymentRecord
{
    public Guid Id { get; set; }
    public string MerchantReference { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public string Status { get; set; } = "Recorded";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class LabDbContext(DbContextOptions<LabDbContext> options) : DbContext(options)
{
    public DbSet<PaymentRecord> PaymentRequests => Set<PaymentRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PaymentRecord>();
        entity.HasKey(x => x.Id);
        entity.Property(x => x.MerchantReference).HasMaxLength(80).IsRequired();
        entity.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        entity.Property(x => x.Amount).HasPrecision(19, 2);
        entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
        entity.HasIndex(x => new { x.MerchantReference, x.IdempotencyKey }).IsUnique();
    }
}
