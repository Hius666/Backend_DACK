-- Read-only verification; this script never creates, changes or drops anything.
SET NOCOUNT ON;
IF COALESCE(CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),N'') <> N'TUAN\SQLEXPRESS'
 OR COALESCE(DB_NAME(),N'') <> N'AQUAR_GPS_STAGING'
    THROW 51000, 'Expected TUAN\SQLEXPRESS / AQUAR_GPS_STAGING; stopped.', 1;

SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)) AS ServerName,
       DB_NAME() AS DatabaseName, SERVERPROPERTY('ProductVersion') AS SqlVersion;
SELECT MigrationId,ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
SELECT t.name AS TableName,c.name AS ColumnName,ty.name AS SqlType,
       c.is_nullable,c.is_identity,c.max_length
FROM sys.tables t
JOIN sys.columns c ON c.object_id=t.object_id
JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE t.name IN(N'AttendanceLocations',N'AttendanceLocationAlerts')
ORDER BY t.name,c.column_id;
SELECT fk.name,fk.is_disabled,fk.is_not_trusted,fk.delete_referential_action_desc,
       OBJECT_NAME(fk.parent_object_id) AS ChildTable,
       COL_NAME(fc.parent_object_id,fc.parent_column_id) AS ChildColumn,
       OBJECT_NAME(fk.referenced_object_id) AS ParentTable,
       COL_NAME(fc.referenced_object_id,fc.referenced_column_id) AS ParentColumn
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fc ON fk.object_id=fc.constraint_object_id
WHERE fk.parent_object_id IN(OBJECT_ID(N'dbo.AttendanceLocations'),OBJECT_ID(N'dbo.AttendanceLocationAlerts'));
SELECT OBJECT_NAME(i.object_id) AS TableName,i.name,i.is_primary_key,i.is_unique,i.is_disabled,
       ic.key_ordinal,c.name AS ColumnName
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id IN(OBJECT_ID(N'dbo.AttendanceLocations'),OBJECT_ID(N'dbo.AttendanceLocationAlerts'))
  AND ic.key_ordinal>0
ORDER BY TableName,i.name,ic.key_ordinal;
SELECT N'Staff' AS TableName,COUNT_BIG(*) AS RecordCount FROM dbo.Staff
UNION ALL SELECT N'Attendances',COUNT_BIG(*) FROM dbo.Attendances
UNION ALL SELECT N'AttendanceLocations',COUNT_BIG(*) FROM dbo.AttendanceLocations
UNION ALL SELECT N'AttendanceLocationAlerts',COUNT_BIG(*) FROM dbo.AttendanceLocationAlerts;
