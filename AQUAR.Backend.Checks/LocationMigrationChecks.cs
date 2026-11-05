using AQUAR.Api;
using AQUAR.Api.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

internal static class LocationMigrationChecks
{
    public static async Task Run()
    {
        void Check(bool result,string name){if(!result)throw new Exception(name);Console.WriteLine("PASS GPS MIGRATION: "+name);}
        var migration=new AddAttendanceLocations();
        Check(migration.UpOperations.All(o=>o is CreateTableOperation or CreateIndexOperation),"Forward migration only creates new tables/indexes");
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE Attendances (Id INTEGER PRIMARY KEY, Method TEXT NOT NULL, Reason TEXT); INSERT INTO Attendances VALUES (7,'biometric','preserved');");
        async Task<string> OriginalRow(){await using var command=connection.CreateCommand();command.CommandText="SELECT Id || '|' || Method || '|' || Reason FROM Attendances WHERE Id=7;";return (string)(await command.ExecuteScalarAsync())!;}
        var before=await OriginalRow();
        // Execute forward operations with the SQLite generator on a disposable DB only.
        // This is not a substitute for SQL Server DDL/TLS/concurrency staging validation.
        var commands=db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations,db.Model);
        foreach(var command in commands)await db.Database.ExecuteSqlRawAsync(command.CommandText);
        Check(await OriginalRow()==before,"Pre-existing attendance values survive forward operations");
        await using var query=connection.CreateCommand();query.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('AttendanceLocations','AttendanceLocationAlerts');";
        Check(Convert.ToInt64(await query.ExecuteScalarAsync())==2,"New location and alert tables created on temporary DB");
        query.CommandText="SELECT COUNT(*) FROM AttendanceLocations;";
        Check(Convert.ToInt64(await query.ExecuteScalarAsync())==0,"Migration does not invent/backfill legacy GPS");
    }
}
