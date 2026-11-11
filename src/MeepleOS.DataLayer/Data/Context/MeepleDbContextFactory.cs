using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MeepleOS.DataLayer.Data.Context;

public sealed class MeepleDbContextFactory : IDesignTimeDbContextFactory<MeepleDbContext>
{
    public MeepleDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MeepleDbContext>()
            .UseSqlite("Data Source=meepleos-design.db").Options);
}
