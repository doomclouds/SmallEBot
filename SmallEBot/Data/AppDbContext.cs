using Microsoft.EntityFrameworkCore;
using SmallEBot.Data.Entities;

namespace SmallEBot.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ToolCall> ToolCalls => Set<ToolCall>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Conversation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserName, x.UpdatedAt });
        });

        modelBuilder.Entity<ChatMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.HasOne(x => x.Conversation)
                .WithMany(x => x.Messages)
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ToolCall>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ChatMessageId);
            e.HasOne(x => x.ChatMessage)
                .WithMany(x => x.ToolCalls)
                .HasForeignKey(x => x.ChatMessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
