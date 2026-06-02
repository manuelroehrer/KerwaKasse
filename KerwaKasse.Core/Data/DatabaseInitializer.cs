using Microsoft.Data.Sqlite;

namespace KerwaKasse.Core.Data;

public static class DatabaseInitializer
{
    public static void Initialize(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Products (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Description TEXT NOT NULL,
                PriceCents INTEGER NOT NULL,
                Available INTEGER NOT NULL DEFAULT 1,
                Color TEXT,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrderTime TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS OrderPositions (
                OrderId INTEGER NOT NULL,
                ProductId INTEGER NOT NULL,
                Amount INTEGER NOT NULL,
                UnitPriceCents INTEGER NOT NULL,
                PRIMARY KEY (OrderId, ProductId),
                FOREIGN KEY (OrderId) REFERENCES Orders(Id),
                FOREIGN KEY (ProductId) REFERENCES Products(Id)
            );
            """;
        command.ExecuteNonQuery();
    }
}
