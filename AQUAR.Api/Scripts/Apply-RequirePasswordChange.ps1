[CmdletBinding()]
param([string]$BackupDirectory)

$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$migration = '20261105063815_RequirePasswordChange'
$database = 'AQUAR_DB'
$server = 'TUAN\SQLEXPRESS'
$originalLocation = Get-Location
$connection = $null

function Query([string]$Sql, [hashtable]$Parameters = @{}) {
    $command = $connection.CreateCommand()
    $command.CommandText = $Sql
    $command.CommandTimeout = 300
    foreach ($key in $Parameters.Keys) { [void]$command.Parameters.AddWithValue($key, $Parameters[$key]) }
    $table = New-Object System.Data.DataTable
    try {
        $reader = $command.ExecuteReader()
        try { $table.Load($reader) } finally { $reader.Dispose() }
        return ,$table
    } finally { $command.Dispose() }
}

function Run-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE. Stopped; no automatic rollback or database reset." }
}

function Execute([string]$Sql, [hashtable]$Parameters = @{}) {
    $command = $connection.CreateCommand()
    $command.CommandText = $Sql
    $command.CommandTimeout = 300
    foreach ($key in $Parameters.Keys) { [void]$command.Parameters.AddWithValue($key, $Parameters[$key]) }
    try { [void]$command.ExecuteNonQuery() } finally { $command.Dispose() }
}

function Snapshot {
    # Hash all original business columns, including PasswordHash, inside SQL Server.
    # Only the digests leave SQL; no passwords or password hashes are printed or written to disk.
    return Query @'
SELECT N'Staff' AS TableName, COUNT_BIG(*) AS RecordCount,
 CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(nvarchar(max),
 (SELECT Id,Name,Email,PasswordHash,Role,Active,HourlyRate,ManagerId,NfcCardId,DepartmentId
  FROM dbo.Staff ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES))), 2) AS Digest FROM dbo.Staff
UNION ALL
SELECT N'Departments', COUNT_BIG(*),
 CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(nvarchar(max),
 (SELECT Id,Name,Description FROM dbo.Departments ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES))), 2) FROM dbo.Departments
UNION ALL
SELECT N'Attendances', COUNT_BIG(*),
 CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(nvarchar(max),
 (SELECT Id,StaffId,CheckInUtc,CheckOutUtc,Method,Manual,RecordedBy,Reason
  FROM dbo.Attendances ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES))), 2) FROM dbo.Attendances;
'@
}

function Compare-Snapshot($Before, $After) {
    foreach ($row in $Before.Rows) {
        $match = @($After.Rows | Where-Object { $_.TableName -eq $row.TableName })
        if ($match.Count -ne 1 -or $match[0].RecordCount -ne $row.RecordCount -or $match[0].Digest -ne $row.Digest) {
            throw "Existing data changed in $($row.TableName). Stopped; inspect the verified backup. No automatic restore was attempted."
        }
    }
}

try {
    Set-Location $workspace
    # Refuse to proceed while an apphost is running; stop any dotnet-hosted AQUAR server manually as well.
    if (Get-Process -Name 'AQUAR.Api','AQUAR.Backend.Checks' -ErrorAction SilentlyContinue) {
        throw 'Stop AQUAR Backend/checks in their terminals before running this script. No process was stopped automatically.'
    }
    $connection = [System.Data.SqlClient.SqlConnection]::new("Server=$server;Database=$database;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=10")
    $connection.Open()
    $target = Query "SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)) AS ServerName, DB_NAME() AS DatabaseName;"
    if ($target.Rows[0].ServerName -ine $server -or $target.Rows[0].DatabaseName -cne $database) { throw 'Unexpected SQL Server/database. Nothing changed.' }
    $history = Query 'SELECT MigrationId,ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;'
    if ($history.Rows.Count -ne 1 -or $history.Rows[0].MigrationId -ne '20261105060157_InitialCreate') {
        throw 'Expected only InitialCreate. Migration may already be applied or database history differs. Nothing changed.'
    }
    $columns = Query "SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Staff') AND name IN(N'MustChangePassword',N'TokenVersion');"
    if ($columns.Rows.Count -ne 0) { throw 'Target columns already exist; stopped without changes.' }

    Run-DotNet -Arguments @('tool','restore')
    $manifest = Get-Content (Join-Path $workspace 'dotnet-tools.json') -Raw | ConvertFrom-Json
    if ($manifest.tools.'dotnet-ef'.version -ne '8.0.11') { throw 'Expected local EF CLI 8.0.11.' }
    Run-DotNet -Arguments @('tool','run','dotnet-ef','--','--version')
    Run-DotNet -Arguments @('build','AQUAR.Backend.Checks\AQUAR.Backend.Checks.csproj','--configuration','Verification','--no-restore')
    Run-DotNet -Arguments @('tool','run','dotnet-ef','--','migrations','has-pending-model-changes','--project','AQUAR.Api','--startup-project','AQUAR.Api','--context','AppDb','--configuration','Verification','--no-build')

    $sqlPath = Join-Path $workspace 'AQUAR.Api\Migrations\RequirePasswordChange.sql'
    Run-DotNet -Arguments @('tool','run','dotnet-ef','--','migrations','script','InitialCreate','RequirePasswordChange','--project','AQUAR.Api','--startup-project','AQUAR.Api','--context','AppDb','--configuration','Verification','--no-build','--output',$sqlPath)
    $sql = Get-Content $sqlPath -Raw
    # Permit exactly the reviewed forward migration, not arbitrary SQL with a few safe keywords.
    $expected = @"
BEGIN TRANSACTION;
GO
ALTER TABLE [Staff] ADD [MustChangePassword] bit NOT NULL DEFAULT CAST(0 AS bit);
GO
ALTER TABLE [Staff] ADD [TokenVersion] int NOT NULL DEFAULT 0;
GO
INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'$migration', N'8.0.11');
GO
COMMIT;
GO
"@
    if (($sql -replace '\s','') -cne ($expected -replace '\s','')) { throw 'Generated SQL differs from the reviewed two-column migration. Nothing changed.' }
    $before = Snapshot

    if ([string]::IsNullOrWhiteSpace($BackupDirectory)) {
        $pathResult = Query "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(260)) AS BackupDirectory;"
        $BackupDirectory = [string]$pathResult.Rows[0].BackupDirectory
    }
    if ([string]::IsNullOrWhiteSpace($BackupDirectory)) { throw 'Supply -BackupDirectory with an existing directory writable by the SQL Server service.' }
    $fileName = 'AQUAR_DB_before_RequirePasswordChange_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '_' + [guid]::NewGuid().ToString('N') + '.bak'
    $backupFile = $BackupDirectory.TrimEnd('\','/') + '\' + $fileName
    Execute -Sql 'BACKUP DATABASE [AQUAR_DB] TO DISK = @path WITH COPY_ONLY, CHECKSUM;' -Parameters @{'@path'=$backupFile}
    Execute -Sql 'RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;' -Parameters @{'@path'=$backupFile}
    Write-Host "Verified COPY_ONLY backup: $backupFile"
    Compare-Snapshot $before (Snapshot)

    Run-DotNet -Arguments @('tool','run','dotnet-ef','--','database','update','RequirePasswordChange','--project','AQUAR.Api','--startup-project','AQUAR.Api','--context','AppDb','--configuration','Verification','--no-build','--connection',"Server=$server;Database=$database;Trusted_Connection=True;TrustServerCertificate=True")
    Compare-Snapshot $before (Snapshot)
    $state = Query 'SELECT COUNT_BIG(*) AS InvalidCount FROM dbo.Staff WHERE MustChangePassword<>0 OR TokenVersion<>0;'
    if ($state.Rows[0].InvalidCount -ne 0) { throw 'Existing accounts do not have expected defaults. Inspect before continuing.' }
    $verifiedColumns = Query "SELECT c.name,t.name AS SqlType,c.is_nullable,dc.definition AS DefaultValue FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id LEFT JOIN sys.default_constraints dc ON c.default_object_id=dc.object_id WHERE c.object_id=OBJECT_ID(N'dbo.Staff') AND c.name IN(N'MustChangePassword',N'TokenVersion');"
    if ($verifiedColumns.Rows.Count -ne 2) { throw 'Expected two new columns after migration.' }
    $verifiedColumns | Format-Table -AutoSize
    $applied = Query 'SELECT MigrationId,ProductVersion FROM dbo.__EFMigrationsHistory WHERE MigrationId=@id;' @{'@id'=$migration}
    if ($applied.Rows.Count -ne 1 -or $applied.Rows[0].ProductVersion -ne '8.0.11') { throw 'Migration history verification failed.' }
    Write-Host 'Original business data, account password hashes and roles preserved; existing accounts have MustChangePassword=false and TokenVersion=0.'

    # The integration runner reads appsettings.json. Refuse environment overrides for the target.
    if (Test-Path Env:ConnectionStrings__Default) { throw 'Remove ConnectionStrings__Default override before running integration checks.' }
    $config = Get-Content 'AQUAR.Api\appsettings.json' -Raw | ConvertFrom-Json
    $testTarget = [System.Data.SqlClient.SqlConnectionStringBuilder]::new([string]$config.ConnectionStrings.Default)
    if ($testTarget.DataSource -ine $server -or $testTarget.InitialCatalog -cne $database -or -not $testTarget.IntegratedSecurity) { throw 'Integration configuration targets another server/database.' }
    Run-DotNet -Arguments @('run','--project','AQUAR.Backend.Checks','--configuration','Verification','--no-build','--no-restore','--','--integration')
    Compare-Snapshot $before (Snapshot)
    Write-Host 'Migration, column/data verification, build and all Backend checks completed successfully. No Admin reset or sample accounts retained.'
} finally {
    if ($connection) { $connection.Dispose() }
    Set-Location $originalLocation
}
