using KerwaKasse.Core.Models;

namespace KerwaKasse.Core.Services;

/// <summary>CRUD for user-defined saved analyses (templates and ready-to-run queries).</summary>
public interface ISavedAnalysisService
{
    /// <summary>All saved analyses ordered by SortOrder, then name. <see cref="SavedAnalysis.ProductIds"/>
    /// is not populated here (use <see cref="GetProductIds"/>); <see cref="SavedAnalysis.ProductCount"/> is.</summary>
    List<SavedAnalysis> GetAll();

    List<int> GetProductIds(int id);

    /// <summary>Inserts the analysis and its product selection; returns the generated id.</summary>
    int Add(SavedAnalysis analysis);

    /// <summary>Updates the analysis row and replaces its product selection.</summary>
    void Update(SavedAnalysis analysis);

    void Rename(int id, string newName);

    void Delete(int id);

    void UpdateSortOrder(IEnumerable<SavedAnalysis> analyses);
}
