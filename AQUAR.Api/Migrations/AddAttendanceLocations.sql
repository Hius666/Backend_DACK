BEGIN TRANSACTION;
GO

CREATE TABLE [AttendanceLocations] (
    [Id] int NOT NULL IDENTITY,
    [AttendanceId] int NOT NULL,
    [Event] int NOT NULL,
    [Latitude] float NULL,
    [Longitude] float NULL,
    [RecordedAtUtc] datetime2 NOT NULL,
    [Status] int NOT NULL,
    [DistanceMeters] float NULL,
    [CompanyLatitude] float NULL,
    [CompanyLongitude] float NULL,
    [RadiusMeters] float NOT NULL,
    [VerificationReason] nvarchar(64) NOT NULL,
    CONSTRAINT [PK_AttendanceLocations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AttendanceLocations_Attendances_AttendanceId] FOREIGN KEY ([AttendanceId]) REFERENCES [Attendances] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [AttendanceLocationAlerts] (
    [Id] int NOT NULL IDENTITY,
    [AttendanceLocationId] int NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [Kind] nvarchar(64) NOT NULL,
    CONSTRAINT [PK_AttendanceLocationAlerts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AttendanceLocationAlerts_AttendanceLocations_AttendanceLocationId] FOREIGN KEY ([AttendanceLocationId]) REFERENCES [AttendanceLocations] ([Id]) ON DELETE NO ACTION
);
GO

CREATE UNIQUE INDEX [IX_AttendanceLocationAlerts_AttendanceLocationId] ON [AttendanceLocationAlerts] ([AttendanceLocationId]);
GO

CREATE UNIQUE INDEX [IX_AttendanceLocations_AttendanceId_Event] ON [AttendanceLocations] ([AttendanceId], [Event]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261105080604_AddAttendanceLocations', N'8.0.11');
GO

COMMIT;
GO

