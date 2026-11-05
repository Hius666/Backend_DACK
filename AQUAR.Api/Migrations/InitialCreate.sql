IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [Departments] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [Description] nvarchar(max) NULL,
    CONSTRAINT [PK_Departments] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [Staff] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [Email] nvarchar(254) NOT NULL,
    [PasswordHash] nvarchar(max) NOT NULL,
    [Role] int NOT NULL,
    [Active] bit NOT NULL,
    [HourlyRate] decimal(18,2) NOT NULL,
    [ManagerId] int NULL,
    [NfcCardId] nvarchar(max) NULL,
    [DepartmentId] int NULL,
    CONSTRAINT [PK_Staff] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Staff_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Staff_Staff_ManagerId] FOREIGN KEY ([ManagerId]) REFERENCES [Staff] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [Attendances] (
    [Id] int NOT NULL IDENTITY,
    [StaffId] int NOT NULL,
    [CheckInUtc] datetime2 NOT NULL,
    [CheckOutUtc] datetime2 NULL,
    [Method] nvarchar(max) NOT NULL,
    [Manual] bit NOT NULL,
    [RecordedBy] int NULL,
    [Reason] nvarchar(max) NULL,
    CONSTRAINT [PK_Attendances] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Attendances_Staff_StaffId] FOREIGN KEY ([StaffId]) REFERENCES [Staff] ([Id]) ON DELETE NO ACTION
);
GO

CREATE UNIQUE INDEX [IX_Attendances_StaffId] ON [Attendances] ([StaffId]) WHERE [CheckOutUtc] IS NULL;
GO

CREATE INDEX [IX_Attendances_StaffId_CheckInUtc] ON [Attendances] ([StaffId], [CheckInUtc]);
GO

CREATE UNIQUE INDEX [IX_Departments_Name] ON [Departments] ([Name]);
GO

CREATE INDEX [IX_Staff_DepartmentId] ON [Staff] ([DepartmentId]);
GO

CREATE UNIQUE INDEX [IX_Staff_Email] ON [Staff] ([Email]);
GO

CREATE INDEX [IX_Staff_ManagerId] ON [Staff] ([ManagerId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261105060157_InitialCreate', N'8.0.11');
GO

COMMIT;
GO

