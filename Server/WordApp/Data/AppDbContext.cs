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
    }
}