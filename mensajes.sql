USE [OSO]
GO
/****** Object:  Table [dbo].[Mensajes] ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Mensajes](
	[IdMensaje] [int] IDENTITY(1,1) NOT NULL,
	[IdEmisor] [int] NOT NULL,
	[IdReceptor] [int] NOT NULL,
	[Texto] [nvarchar](1000) NOT NULL,
	[FechaEnvio] [datetime] NOT NULL,
	[Leido] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[IdMensaje] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[Mensajes] ADD  DEFAULT (getdate()) FOR [FechaEnvio]
GO
ALTER TABLE [dbo].[Mensajes] ADD  DEFAULT ((0)) FOR [Leido]
GO
ALTER TABLE [dbo].[Mensajes]  WITH CHECK ADD  CONSTRAINT [FK_Mensajes_Emisor] FOREIGN KEY([IdEmisor])
REFERENCES [dbo].[Usuarios] ([IdUsuario])
GO
ALTER TABLE [dbo].[Mensajes] CHECK CONSTRAINT [FK_Mensajes_Emisor]
GO
ALTER TABLE [dbo].[Mensajes]  WITH CHECK ADD  CONSTRAINT [FK_Mensajes_Receptor] FOREIGN KEY([IdReceptor])
REFERENCES [dbo].[Usuarios] ([IdUsuario])
GO
ALTER TABLE [dbo].[Mensajes] CHECK CONSTRAINT [FK_Mensajes_Receptor]
GO
CREATE NONCLUSTERED INDEX [IX_Mensajes_Conversacion] ON [dbo].[Mensajes] ([IdEmisor], [IdReceptor], [FechaEnvio])
GO
