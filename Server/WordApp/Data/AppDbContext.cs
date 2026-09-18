using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;
using WordApp.Controllers;
using WordApp.Models;

namespace WordApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Word> Words => Set<Word>();
    public DbSet<Grammar> Grammars => Set<Grammar>();
    public DbSet<Idiom> Idioms => Set<Idiom>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<WordProgress> WordProgresses => Set<WordProgress>();
    public DbSet<GrammarProgress> GrammarProgresses => Set<GrammarProgress>();
    public DbSet<IdiomProgress> IdiomProgresses => Set<IdiomProgress>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Word>().HasKey(w => w.Id);
        mb.Entity<Word>().Property(w => w.English).HasMaxLength(200);
        mb.Entity<Word>().Property(w => w.Source).HasMaxLength(20).HasDefaultValue("Notion");
        mb.Entity<Grammar>().HasKey(g => g.Id);
        mb.Entity<Grammar>().Property(g => g.Title).HasMaxLength(200);
        mb.Entity<Grammar>().Property(g => g.Category).HasMaxLength(100);
        mb.Entity<Grammar>().Property(g => g.Example).HasMaxLength(500);
        mb.Entity<Grammar>().Property(g => g.Explanation).HasMaxLength(1000);
        mb.Entity<Grammar>().Property(g => g.Note).HasMaxLength(500);
        mb.Entity<Grammar>().Property(g => g.Status).HasMaxLength(50);
        mb.Entity<Idiom>().HasKey(i => i.Id);
        mb.Entity<Idiom>().Property(i => i.Title).HasMaxLength(200);
        mb.Entity<Idiom>().Property(i => i.Category).HasMaxLength(100);
        mb.Entity<Idiom>().Property(i => i.Example).HasMaxLength(500);
        mb.Entity<Idiom>().Property(i => i.Explanation).HasMaxLength(1000);
        mb.Entity<Idiom>().Property(i => i.Note).HasMaxLength(500);
        mb.Entity<Idiom>().Property(i => i.Status).HasMaxLength(50);
        mb.Entity<User>().HasKey(u => u.Id);
        mb.Entity<User>().Property(u => u.UserId).HasMaxLength(100);
        mb.Entity<User>().Property(u => u.Pw).HasMaxLength(200);
        mb.Entity<User>().HasIndex(u => u.UserId).IsUnique();
        mb.Entity<User>().Property(u => u.SecurityStamp).HasMaxLength(32);
        mb.Entity<UserSession>().HasKey(s => s.TokenHash);
        mb.Entity<UserSession>().Property(s => s.TokenHash).HasMaxLength(64);
        mb.Entity<UserSession>().Property(s => s.SecurityStamp).HasMaxLength(32);
        mb.Entity<UserSession>().HasIndex(s => s.ExpiresAt);
        mb.Entity<UserSession>().HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);

        mb.Entity<WordProgress>().HasKey(p => p.Id);
        mb.Entity<WordProgress>().Property(p => p.UserId).HasMaxLength(100);
        mb.Entity<WordProgress>().Property(p => p.WordId).HasMaxLength(200);
        mb.Entity<WordProgress>().Property(p => p.Status).HasMaxLength(50);
        // One progress row per (user, word); also the lookup key for upserts.
        mb.Entity<WordProgress>().HasIndex(p => new { p.UserId, p.WordId }).IsUnique();
        mb.Entity<WordProgress>().HasOne<User>().WithMany().HasForeignKey(p => p.UserId)
            .HasPrincipalKey(u => u.UserId).OnDelete(DeleteBehavior.Cascade);

        mb.Entity<GrammarProgress>().HasKey(p => p.Id);
        mb.Entity<GrammarProgress>().Property(p => p.UserId).HasMaxLength(100);
        mb.Entity<GrammarProgress>().Property(p => p.GrammarId).HasMaxLength(200);
        mb.Entity<GrammarProgress>().Property(p => p.Status).HasMaxLength(50);
        mb.Entity<GrammarProgress>().HasIndex(p => new { p.UserId, p.GrammarId }).IsUnique();
        mb.Entity<GrammarProgress>().HasOne<User>().WithMany().HasForeignKey(p => p.UserId)
            .HasPrincipalKey(u => u.UserId).OnDelete(DeleteBehavior.Cascade);

        mb.Entity<IdiomProgress>().HasKey(p => p.Id);
        mb.Entity<IdiomProgress>().Property(p => p.UserId).HasMaxLength(100);
        mb.Entity<IdiomProgress>().Property(p => p.IdiomId).HasMaxLength(200);
        mb.Entity<IdiomProgress>().Property(p => p.Status).HasMaxLength(50);
        mb.Entity<IdiomProgress>().HasIndex(p => new { p.UserId, p.IdiomId }).IsUnique();
        mb.Entity<IdiomProgress>().HasOne<User>().WithMany().HasForeignKey(p => p.UserId)
            .HasPrincipalKey(u => u.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
