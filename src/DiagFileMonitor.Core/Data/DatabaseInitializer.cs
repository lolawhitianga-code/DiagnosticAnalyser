using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.Core.Data;

/// <summary>
/// Creates the database on first run, and adds any columns a newer build expects but an
/// existing database does not have. Without this, upgrading the app would mean deleting
/// the history it exists to accumulate.
/// </summary>
public static class DatabaseInitializer
{
    private static readonly (string Table, string Column, string Type)[] ExpectedColumns =
    {
        ("DiagnosticFiles", "Notes", "TEXT NULL"),
        ("DiagnosticFiles", "TicketNumber", "TEXT NULL"),
        ("DiagnosticFiles", "IsBaseline", "INTEGER NOT NULL DEFAULT 0"),
        ("DiagnosticFiles", "ZohoTicketId", "TEXT NULL"),
        ("DiagnosticFiles", "ZohoTicketNumber", "TEXT NULL"),
        ("DiagnosticFiles", "ZohoTicketCreatedUtc", "TEXT NULL"),
        ("DiagnosticFiles", "AlertSentUtc", "TEXT NULL"),
        ("DiagnosticFiles", "MachineName", "TEXT NULL"),
        ("DiagnosticFiles", "SiteLocation", "TEXT NULL"),
        ("DiagnosticFiles", "SoftwareName", "TEXT NULL"),
        ("DiagnosticFiles", "SupportPanel", "TEXT NULL"),
        ("DiagnosticFiles", "SupportMembers", "TEXT NULL"),
        ("DiagnosticFiles", "SupportIssue", "TEXT NULL"),
        ("ProductionLogFiles", "Source", "TEXT NOT NULL DEFAULT 'WeeklyLog'"),
        ("ProductionLogFiles", "CoversFromUtc", "TEXT NULL"),
        ("ProductionLogFiles", "CoversToUtc", "TEXT NULL"),
        ("ProductionLogFiles", "PanelsSkippedAsDuplicate", "INTEGER NOT NULL DEFAULT 0"),
        ("ProductionPanels", "Kind", "TEXT NOT NULL DEFAULT 'Panel'")
    };

    /// <summary>
    /// Tables a newer build expects. EnsureCreated only builds a schema from nothing, so a
    /// database made by an earlier version never gets a table added later without this.
    /// </summary>
    private static readonly (string Table, string Sql)[] ExpectedTables =
    {
        ("MachineSignals", """
            CREATE TABLE IF NOT EXISTS "MachineSignals" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_MachineSignals" PRIMARY KEY AUTOINCREMENT,
                "SerialNumber" TEXT NOT NULL,
                "MachineType" TEXT NOT NULL,
                "Kind" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Address" TEXT NOT NULL,
                "FirstSeenUtc" TEXT NOT NULL,
                "LastSeenUtc" TEXT NOT NULL,
                "BundlesSeenIn" INTEGER NOT NULL,
                "TotalChanges" INTEGER NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_MachineSignals_SerialNumber_Kind_Name_Address"
                ON "MachineSignals" ("SerialNumber", "Kind", "Name", "Address");
            CREATE INDEX IF NOT EXISTS "IX_MachineSignals_MachineType"
                ON "MachineSignals" ("MachineType");
            """),

        ("ProductionLogFiles", """
            CREATE TABLE IF NOT EXISTS "ProductionLogFiles" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ProductionLogFiles" PRIMARY KEY AUTOINCREMENT,
                "SerialNumber" TEXT NOT NULL,
                "FileName" TEXT NOT NULL,
                "Year" INTEGER NOT NULL,
                "Week" INTEGER NOT NULL,
                "ImportedAtUtc" TEXT NOT NULL,
                "LinesRead" INTEGER NOT NULL,
                "ConsecutiveDuplicates" INTEGER NOT NULL,
                "MalformedLines" INTEGER NOT NULL,
                "PanelsStored" INTEGER NOT NULL,
                "Notes" TEXT NULL,
                "Source" TEXT NOT NULL DEFAULT 'WeeklyLog',
                "CoversFromUtc" TEXT NULL,
                "CoversToUtc" TEXT NULL,
                "PanelsSkippedAsDuplicate" INTEGER NOT NULL DEFAULT 0);
            -- The earlier build keyed a week on serial alone, which would block a support
            -- bundle's partial week once the full weekly export was already held.
            DROP INDEX IF EXISTS "IX_ProductionLogFiles_SerialNumber_Year_Week";
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductionLogFiles_SerialNumber_Year_Week_Source"
                ON "ProductionLogFiles" ("SerialNumber", "Year", "Week", "Source");
            """),
        ("ProductionPanels", """
            CREATE TABLE IF NOT EXISTS "ProductionPanels" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ProductionPanels" PRIMARY KEY AUTOINCREMENT,
                "ProductionLogFileId" INTEGER NOT NULL,
                "SerialNumber" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "EndedAt" TEXT NOT NULL,
                "StartedAt" TEXT NULL,
                "Outcome" TEXT NOT NULL,
                "Kind" TEXT NOT NULL DEFAULT 'Panel',
                "FastenerCount" REAL NOT NULL,
                "MembersAssembled" INTEGER NOT NULL,
                "Cube" REAL NOT NULL,
                "Lineal" REAL NOT NULL,
                "BuildMinutes" REAL NOT NULL,
                "IdleMinutes" REAL NOT NULL,
                "Junctions" REAL NOT NULL,
                "BuildTimeImplausible" INTEGER NOT NULL,
                CONSTRAINT "FK_ProductionPanels_ProductionLogFiles_ProductionLogFileId"
                    FOREIGN KEY ("ProductionLogFileId") REFERENCES "ProductionLogFiles" ("Id") ON DELETE CASCADE);
            CREATE INDEX IF NOT EXISTS "IX_ProductionPanels_SerialNumber" ON "ProductionPanels" ("SerialNumber");
            CREATE INDEX IF NOT EXISTS "IX_ProductionPanels_EndedAt" ON "ProductionPanels" ("EndedAt");
            CREATE INDEX IF NOT EXISTS "IX_ProductionPanels_Outcome" ON "ProductionPanels" ("Outcome");
            """)
    };

    public static void Initialize(DiagDbContext context)
    {
        context.Database.EnsureCreated();

        foreach (var (_, sql) in ExpectedTables)
        {
#pragma warning disable EF1002
            context.Database.ExecuteSqlRaw(sql);
#pragma warning restore EF1002
        }

        foreach (var (table, column, type) in ExpectedColumns)
        {
            if (!ColumnExists(context, table, column))
            {
                // EF1002: SQLite cannot parameterise table/column names, and these come from the
                // constant list above rather than from anything a user can influence.
#pragma warning disable EF1002
                context.Database.ExecuteSqlRaw($"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {type};");
#pragma warning restore EF1002
            }
        }
    }

    private static bool ColumnExists(DiagDbContext context, string table, string column)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{table}\");";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
    }
}
