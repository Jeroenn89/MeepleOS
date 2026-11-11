using Microsoft.Data.Sqlite;

if (args.Length != 2)
{
    Console.Error.WriteLine("Gebruik: CloneTestDatabase <bron.db> <nieuwe-test.db>");
    return 1;
}

var sourcePath = Path.GetFullPath(args[0]);
var targetPath = Path.GetFullPath(args[1]);
if (!File.Exists(sourcePath))
{
    Console.Error.WriteLine($"Bronbestand bestaat niet: {sourcePath}");
    return 1;
}
if (File.Exists(targetPath) || sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Doel bestaat al of is gelijk aan de bron: {targetPath}");
    return 1;
}

Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
var sourceConnection = new SqliteConnectionStringBuilder
{
    DataSource = sourcePath,
    Mode = SqliteOpenMode.ReadOnly
};
var targetConnection = new SqliteConnectionStringBuilder
{
    DataSource = targetPath,
    Mode = SqliteOpenMode.ReadWriteCreate
};
using var source = new SqliteConnection(sourceConnection.ToString());
using var target = new SqliteConnection(targetConnection.ToString());
source.Open();
target.Open();
source.BackupDatabase(target);
Console.WriteLine($"Testkopie gemaakt: {targetPath}");
return 0;
