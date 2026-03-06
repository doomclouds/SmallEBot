using Microsoft.EntityFrameworkCore;
using SmallEBot.Core.Entities;

namespace SmallEBot.Infrastructure.Data;

public class SmallEBotDbContext(DbContextOptions<SmallEBotDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Conversation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserName, x.UpdatedAt });
        });
    }
}
