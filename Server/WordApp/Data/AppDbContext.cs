using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;
using WordApp.Models;

namespace WordApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Word> Words => Set<Word>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Word>().HasKey(w => w.Id);
        mb.Entity<Word>().Property(w => w.English).HasMaxLength(200);
    }
}