using Microsoft.EntityFrameworkCore;
using SmallEBot.Core.Entities;

namespace SmallEBot.Infrastructure.Data;

public class SmallEBotDbContext(DbContextOptions<SmallEBotDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationTurn> ConversationTurns => Set<ConversationTurn>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ToolCall> ToolCalls => Set<ToolCall>();
    public DbSet<ThinkBlock> ThinkBlocks => Set<ThinkBlock>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Conversation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserName, x.UpdatedAt });
        });

        modelBuilder.Entity<ConversationTurn>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.HasOne(x => x.Conversation)
                .WithMany(x => x.Turns)
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ChatMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.HasOne(x => x.Conversation)
                .WithMany(x => x.Messages)
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Turn)
                .WithMany()
                .HasForeignKey(x => x.TurnId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ToolCall>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.HasOne(x => x.Conversation)
                .WithMany(x => x.ToolCalls)
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Turn)
                .WithMany()
                .HasForeignKey(x => x.TurnId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ThinkBlock>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.HasOne(x => x.Conversation)
                .WithMany(x => x.ThinkBlocks)
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Turn)
                .WithMany()
                .HasForeignKey(x => x.TurnId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
