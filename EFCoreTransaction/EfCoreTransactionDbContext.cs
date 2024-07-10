using Microsoft.EntityFrameworkCore;

namespace EFCoreTransaciton;

public class EfCoreTransactionDbContext(DbContextOptions<EfCoreTransactionDbContext> opt) : DbContext(opt)
{
    public DbSet<Model> Models { get; set; }
}

public class Model
{
    public int Id { get; set; }
    public string Name { get; set; }
}