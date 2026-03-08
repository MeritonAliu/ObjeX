using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using ObjeX.Core.Models;

namespace ObjeX.Infrastructure.Data;

public class ObjeXDbContext(DbContextOptions<ObjeXDbContext> options) : IdentityDbContext<User>(options)
{
    public DbSet<Bucket> Buckets { get; set; } = null!;
    public DbSet<BlobObject> BlobObjects { get; set; } = null!;
    public DbSet<ApiKey> ApiKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<Bucket>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).IsRequired();
        });

        modelBuilder.Entity<BlobObject>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => new { e.BucketName, e.Key }).IsUnique();
            entity.Property(e => e.BucketName).IsRequired();
            entity.Property(e => e.Key).IsRequired();
            entity.Property(e => e.ETag).IsRequired();

            entity.HasOne(e => e.Bucket)
                .WithMany(b => b.Objects)
                .HasForeignKey(e => e.BucketName)
                .HasPrincipalKey(b => b.Name);
        });

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Key).ValueGeneratedNever();
            entity.HasIndex(e => e.Key).IsUnique();
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}