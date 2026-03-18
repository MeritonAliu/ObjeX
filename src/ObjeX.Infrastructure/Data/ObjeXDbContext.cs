using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using ObjeX.Core.Models;

namespace ObjeX.Infrastructure.Data;

public class ObjeXDbContext(DbContextOptions<ObjeXDbContext> options) : IdentityDbContext<User>(options)
{
    public DbSet<Bucket> Buckets { get; set; } = null!;
    public DbSet<BlobObject> BlobObjects { get; set; } = null!;
    public DbSet<S3Credential> S3Credentials { get; set; } = null!;
    public DbSet<MultipartUpload> MultipartUploads { get; set; } = null!;
    public DbSet<MultipartUploadPart> MultipartUploadParts { get; set; } = null!;
    public DbSet<SystemSettings> SystemSettings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Bucket>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.OwnerId).IsRequired();
            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<S3Credential>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => e.AccessKeyId).IsUnique();
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
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

        modelBuilder.Entity<MultipartUpload>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<MultipartUploadPart>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => new { e.UploadId, e.PartNumber }).IsUnique();
            entity.HasOne(e => e.Upload)
                .WithMany(u => u.Parts)
                .HasForeignKey(e => e.UploadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SystemSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasData(new SystemSettings { Id = 1 }); // seed default row
        });
    }
}
