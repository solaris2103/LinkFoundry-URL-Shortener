using Microsoft.EntityFrameworkCore;
using Shortener.Core;

namespace Shortener.Infrastructure;

public sealed class ShortenerDbContext(DbContextOptions<ShortenerDbContext> options) : DbContext(options)
{
    public DbSet<ShortLink> Links => Set<ShortLink>();
    public DbSet<ClickEvent> Clicks => Set<ClickEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortLink>(entity =>
        {
            entity.HasKey(link => link.Id);
            entity.HasIndex(link => link.Code).IsUnique();
            entity.Property(link => link.Code).HasMaxLength(32).IsRequired();
            entity.Property(link => link.DestinationUrl).HasMaxLength(2048).IsRequired();
            entity.HasMany(link => link.Clicks).WithOne(click => click.Link)
                .HasForeignKey(click => click.Code).HasPrincipalKey(link => link.Code);
        });

        modelBuilder.Entity<ClickEvent>(entity =>
        {
            entity.HasKey(click => click.Id);
            entity.HasIndex(click => new { click.Code, click.OccurredAt });
            entity.Property(click => click.ReferrerHost).HasMaxLength(253);
        });
    }
}