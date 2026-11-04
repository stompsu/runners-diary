-- Creates the API's database user and gives it read/write rights (ADR 0031/0032). Idempotent; run by job-migrate as the
-- SQL Entra admin after the EF bundle. The API gets no DDL rights.
--
-- Scripting variables (sqlcmd reads them from the environment):
--   API_IDENTITY_NAME       display name of the user-assigned identity, e.g. id-rd-api
--   API_IDENTITY_CLIENT_ID  its client (application) id: the SID of an Entra service principal/managed identity user.
--                           CREATE USER ... WITH SID needs no Microsoft Graph or Directory Readers rights.
:on error exit
SET NOCOUNT ON;

DECLARE @name sysname = N'$(API_IDENTITY_NAME)';
DECLARE @sid varbinary(16) = CONVERT(varbinary(16), CAST(N'$(API_IDENTITY_CLIENT_ID)' AS uniqueidentifier));
DECLARE @sql nvarchar(max);

-- A user with the same name but another SID is left over from a deleted identity: it can never log in, so replace it.
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @name AND sid <> @sid)
BEGIN
  SET @sql = N'DROP USER ' + QUOTENAME(@name) + N';';
  EXEC (@sql);
END;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @name)
BEGIN
  SET @sql = N'CREATE USER ' + QUOTENAME(@name) + N' WITH SID = ' + CONVERT(nvarchar(34), @sid, 1) + N', TYPE = E;';
  EXEC (@sql);
  PRINT N'Created database user ' + @name;
END;

IF IS_ROLEMEMBER(N'db_datareader', @name) <> 1
BEGIN
  SET @sql = N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@name) + N';';
  EXEC (@sql);
END;

IF IS_ROLEMEMBER(N'db_datawriter', @name) <> 1
BEGIN
  SET @sql = N'ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@name) + N';';
  EXEC (@sql);
END;

PRINT N'Database user ' + @name + N' is ready.';
GO
