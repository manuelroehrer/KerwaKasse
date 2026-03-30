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
            CREATE TABLE IF NOT EXISTS Product (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Description TEXT NOT NULL,
                Price REAL NOT NULL,
                Available INTEGER NOT NULL DEFAULT 1,
                Color TEXT,
                ImagePath TEXT,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS "Order" (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrderTime TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS OrderPosition (
                OrderId INTEGER NOT NULL,
                ProductId INTEGER NOT NULL,
                Amount INTEGER NOT NULL,
                UnitPrice REAL NOT NULL,
                ProductDescription TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (OrderId, ProductId),
                FOREIGN KEY (OrderId) REFERENCES "Order"(Id),
                FOREIGN KEY (ProductId) REFERENCES Product(Id)
            );
            """;
        command.ExecuteNonQuery();
    }
}
