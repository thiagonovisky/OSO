-- Notificaciones (avisos de gastos por vencer y de mensajes nuevos).
-- Se puede correr varias veces: solo crea lo que falta.
USE [OSO]
GO
SET QUOTED_IDENTIFIER ON  -- necesario para el índice filtrado
GO
IF OBJECT_ID('dbo.Notificaciones', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Notificaciones(
        IdNotificacion     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Notificaciones PRIMARY KEY,
        IdUsuario          INT NOT NULL CONSTRAINT FK_Notificaciones_Usuarios REFERENCES dbo.Usuarios(IdUsuario),
        IdGastoResponsable INT NULL CONSTRAINT FK_Notificaciones_GastoResponsables REFERENCES dbo.GastoResponsables(IdGastoResponsable),
        Texto              VARCHAR(300) NOT NULL,
        FechaCreacion      DATETIME NOT NULL CONSTRAINT DF_Notificaciones_Fecha DEFAULT GETDATE(),
        Leida              BIT NOT NULL CONSTRAINT DF_Notificaciones_Leida DEFAULT 0
    )
    CREATE INDEX IX_Notificaciones_Usuario ON dbo.Notificaciones(IdUsuario, Leida)
    -- Una sola notificación de vencimiento por gasto y usuario
    CREATE UNIQUE INDEX UQ_Notificaciones_Gasto ON dbo.Notificaciones(IdUsuario, IdGastoResponsable) WHERE IdGastoResponsable IS NOT NULL
END
GO

-- Avisos de mensajes nuevos: quién mandó el mensaje (NULL en otros avisos)
IF COL_LENGTH('dbo.Notificaciones', 'IdEmisor') IS NULL
    ALTER TABLE dbo.Notificaciones ADD IdEmisor INT NULL CONSTRAINT FK_Notificaciones_Emisor REFERENCES dbo.Usuarios(IdUsuario)
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Notificaciones_Emisor' AND object_id = OBJECT_ID('dbo.Notificaciones'))
    CREATE INDEX IX_Notificaciones_Emisor ON dbo.Notificaciones(IdUsuario, IdEmisor, Leida)
GO
