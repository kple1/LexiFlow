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
    public DbSet<User> Users => Set<User>();
    public DbSet<WordProgress> WordProgresses => Set<WordProgress>();
    public DbSet<GrammarProgress> GrammarProgresses => Set<GrammarProgress>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Word>().HasKey(w => w.Id);
        mb.Entity<Word>().Property(w => w.English).HasMaxLength(200);
        mb.Entity<Grammar>().HasKey(g => g.Id);
        mb.Entity<Grammar>().Property(g => g.Title).HasMaxLength(200);
        mb.Entity<Grammar>().Property(g => g.Category).HasMaxLength(100);
        mb.Entity<Grammar>().Property(g => g.Example).HasMaxLength(500);
        mb.Entity<Grammar>().Property(g => g.Explanation).HasMaxLength(1000);
        mb.Entity<Grammar>().Property(g => g.Note).HasMaxLength(500);
        mb.Entity<Grammar>().Property(g => g.Status).HasMaxLength(50);
        mb.Entity<User>().HasKey(u => u.Id);
        mb.Entity<User>().Property(u => u.UserId).HasMaxLength(100);
        mb.Entity<User>().Property(u => u.Pw).HasMaxLength(200);

        mb.Entity<WordProgress>().HasKey(p => p.Id);
        mb.Entity<WordProgress>().Property(p => p.UserId).HasMaxLength(100);
        mb.Entity<WordProgress>().Property(p => p.WordId).HasMaxLength(200);
        mb.Entity<WordProgress>().Property(p => p.Status).HasMaxLength(50);
        // One progress row per (user, word); also the lookup key for upserts.
        mb.Entity<WordProgress>().HasIndex(p => new { p.UserId, p.WordId }).IsUnique();

        mb.Entity<GrammarProgress>().HasKey(p => p.Id);
        mb.Entity<GrammarProgress>().Property(p => p.UserId).HasMaxLength(100);
        mb.Entity<GrammarProgress>().Property(p => p.GrammarId).HasMaxLength(200);
        mb.Entity<GrammarProgress>().Property(p => p.Status).HasMaxLength(50);
        mb.Entity<GrammarProgress>().HasIndex(p => new { p.UserId, p.GrammarId }).IsUnique();
    }
}