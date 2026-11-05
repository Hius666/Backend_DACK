BEGIN TRANSACTION;
GO

ALTER TABLE [Staff] ADD [MustChangePassword] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

ALTER TABLE [Staff] ADD [TokenVersion] int NOT NULL DEFAULT 0;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261105063815_RequirePasswordChange', N'8.0.11');
GO

COMMIT;
GO

