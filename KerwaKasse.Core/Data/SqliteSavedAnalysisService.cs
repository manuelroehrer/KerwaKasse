using System.Globalization;
using Dapper;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using Microsoft.Data.Sqlite;

namespace KerwaKasse.Core.Data;

public class SqliteSavedAnalysisService : ISavedAnalysisService
{
    // Same canonical text format as SqliteOrderService for the stored period bounds.
    private const string SqliteDateFormat = "yyyy-MM-dd HH:mm:ss";

    private readonly string _connectionString;

    public SqliteSavedAnalysisService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public List<SavedAnalysis> GetAll()
    {
        using var connection = new SqliteConnection(_connectionString);
        return connection.Query<SavedAnalysisRow>(
            """
            SELECT a.Id, a.Name, a.AllProducts, a.FromTime, a.ToTime, a.SortOrder,
                   (SELECT COUNT(*) FROM SavedAnalysisProducts sp WHERE sp.SavedAnalysisId = a.Id) AS ProductCount
            FROM SavedAnalyses a
            ORDER BY a.SortOrder, a.Name
            """)
            .Select(Map).ToList();
    }

    public List<int> GetProductIds(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        return connection.Query<int>(
            "SELECT ProductId FROM SavedAnalysisProducts WHERE SavedAnalysisId = @id",
            new { id }).ToList();
    }

    public int Add(SavedAnalysis analysis)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        connection.Execute(
            """
            INSERT INTO SavedAnalyses (Name, AllProducts, FromTime, ToTime, SortOrder)
            VALUES (@Name, @AllProducts, @FromTime, @ToTime,
                    (SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM SavedAnalyses))
            """,
            ToParams(analysis), transaction);

        int id = (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: transaction);
        InsertProducts(connection, transaction, id, analysis);

        transaction.Commit();
        return id;
    }

    public void Update(SavedAnalysis analysis)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        connection.Execute(
            """
            UPDATE SavedAnalyses
            SET Name = @Name, AllProducts = @AllProducts, FromTime = @FromTime, ToTime = @ToTime
            WHERE Id = @Id
            """,
            ToParams(analysis), transaction);

        connection.Execute("DELETE FROM SavedAnalysisProducts WHERE SavedAnalysisId = @Id",
            new { analysis.Id }, transaction);
        InsertProducts(connection, transaction, analysis.Id, analysis);

        transaction.Commit();
    }

    public void Rename(int id, string newName)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Execute("UPDATE SavedAnalyses SET Name = @newName WHERE Id = @id", new { id, newName });
    }

    public void Delete(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM SavedAnalysisProducts WHERE SavedAnalysisId = @id", new { id }, transaction);
        connection.Execute("DELETE FROM SavedAnalyses WHERE Id = @id", new { id }, transaction);
        transaction.Commit();
    }

    public void UpdateSortOrder(IEnumerable<SavedAnalysis> analyses)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var a in analyses)
        {
            connection.Execute("UPDATE SavedAnalyses SET SortOrder = @SortOrder WHERE Id = @Id",
                new { a.SortOrder, a.Id }, transaction);
        }
        transaction.Commit();
    }

    private static void InsertProducts(SqliteConnection connection, SqliteTransaction transaction, int id, SavedAnalysis analysis)
    {
        if (analysis.AllProducts) return;
        foreach (var productId in analysis.ProductIds.Distinct())
        {
            connection.Execute(
                "INSERT INTO SavedAnalysisProducts (SavedAnalysisId, ProductId) VALUES (@id, @productId)",
                new { id, productId }, transaction);
        }
    }

    private static object ToParams(SavedAnalysis a) => new
    {
        a.Id,
        a.Name,
        AllProducts = a.AllProducts ? 1 : 0,
        FromTime = a.From.HasValue ? a.From.Value.ToString(SqliteDateFormat) : null,
        ToTime = a.To.HasValue ? a.To.Value.ToString(SqliteDateFormat) : null
    };

    private static SavedAnalysis Map(SavedAnalysisRow r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        AllProducts = r.AllProducts != 0,
        From = ParseNullable(r.FromTime),
        To = ParseNullable(r.ToTime),
        SortOrder = r.SortOrder,
        ProductCount = r.ProductCount
    };

    private static DateTime? ParseNullable(string? text) =>
        string.IsNullOrEmpty(text) ? null : DateTime.ParseExact(text, SqliteDateFormat, CultureInfo.InvariantCulture);

    private class SavedAnalysisRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public long AllProducts { get; set; }
        public string? FromTime { get; set; }
        public string? ToTime { get; set; }
        public int SortOrder { get; set; }
        public int ProductCount { get; set; }
    }
}
