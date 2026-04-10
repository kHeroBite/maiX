using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace mAIx.Data;

/// <summary>
/// EF Core 마이그레이션 도구용 Design-Time DbContext Factory.
/// dotnet ef migrations add/update 명령 실행 시 DI 없이 DbContext 생성.
/// </summary>
public class mAIxDbContextFactory : IDesignTimeDbContextFactory<mAIxDbContext>
{
    public mAIxDbContext CreateDbContext(string[] args)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dbPath = Path.Combine(appData, "mAIx", "mAIx.db");

        var optionsBuilder = new DbContextOptionsBuilder<mAIxDbContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        return new mAIxDbContext(optionsBuilder.Options);
    }
}
