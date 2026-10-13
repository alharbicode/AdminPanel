BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    DECLARE @var sysname;
    SELECT @var = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'CustomerId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT [' + @var + '];');
    ALTER TABLE [Orders] ALTER COLUMN [CustomerId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [AddressLine] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [CancellationReason] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [CancellationRequestReason] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [CancellationRequestedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [CancelledAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [CancelledBy] nvarchar(80) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [CheckoutOwner] nvarchar(80) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [CheckoutRequestHash] nvarchar(64) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [City] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [ContactEmail] nvarchar(254) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [ContactName] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [CountryCode] nvarchar(2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [Currency] nvarchar(3) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [DeliveryInstructions] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [DiscountTotal] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [Governorate] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [GuestTrackingHash] nvarchar(64) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [IdempotencyKey] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [PaymentMethod] nvarchar(10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [Phone] nvarchar(30) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [Reference] nvarchar(32) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [ShippingFee] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [StockRestoredAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [Subtotal] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [Orders] ADD [Version] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [AppliedDiscountId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [DiscountName] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [DiscountPercentage] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [OriginalUnitPrice] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [ProductName] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [UnitDiscount] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    CREATE TABLE [ShippingZones] (
        [Id] int NOT NULL IDENTITY,
        [Governorate] nvarchar(100) NOT NULL,
        [City] nvarchar(100) NOT NULL,
        [NormalizedDestination] nvarchar(401) NOT NULL,
        [Fee] decimal(18,2) NOT NULL,
        [Enabled] bit NOT NULL,
        [Version] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_ShippingZones] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Orders_CheckoutOwner_IdempotencyKey] ON [Orders] ([CheckoutOwner], [IdempotencyKey]) WHERE [CheckoutOwner] IS NOT NULL AND [IdempotencyKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Orders_GuestTrackingHash] ON [Orders] ([GuestTrackingHash]) WHERE [GuestTrackingHash] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Orders_Reference] ON [Orders] ([Reference]) WHERE [Reference] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ShippingZones_NormalizedDestination] ON [ShippingZones] ([NormalizedDestination]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013213455_AddCheckoutSnapshotsAndShippingZones'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013213455_AddCheckoutSnapshotsAndShippingZones', N'9.0.20');
END;

COMMIT;
GO

