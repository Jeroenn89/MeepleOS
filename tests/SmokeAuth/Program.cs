using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Data.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

if (args.Length != 1) throw new ArgumentException("Geef een nieuw tijdelijk databasepad op.");
var path = Path.GetFullPath(args[0]);
if (File.Exists(path)) throw new InvalidOperationException("De testdatabase bestaat al.");
await using var db = new MeepleDbContext(new DbContextOptionsBuilder<MeepleDbContext>()
    .UseSqlite($"Data Source={path};Pooling=False").Options);
await db.Database.MigrateAsync();
var account = new MeepleUser { Id = Guid.NewGuid().ToString("N"), UserName = "smoke", SecurityStamp = Guid.NewGuid().ToString("N") };
db.Users.Add(account);
await db.SaveChangesAsync();
var protector = DataProtectionProvider.Create(new DirectoryInfo(path + ".auth-keys"),
    builder => builder.SetApplicationName("MeepleOS"))
    .CreateProtector("MeepleOS.McpAccount.v1").ToTimeLimitedDataProtector();
Console.Write(protector.Protect(new MeepleOS.BusinessLayer.Services.McpAuthPayload(account.Id, account.SecurityStamp).Serialize(), TimeSpan.FromMinutes(10)));
