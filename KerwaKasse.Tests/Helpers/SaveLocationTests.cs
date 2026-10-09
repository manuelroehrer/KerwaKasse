using System.IO;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using NSubstitute;

namespace KerwaKasse.Tests.Helpers;

public class SaveLocationTests : IDisposable
{
    private static readonly string Documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private readonly string _folder = Directory.CreateTempSubdirectory("KerwaKasseSave_").FullName;
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    [Fact]
    public void InitialFolder_IsTheFolderSavedToLast()
    {
        _settings.Get<string>("lastSaveFolder").Returns(_folder);

        Assert.Equal(_folder, new SaveLocation(_settings).InitialFolder());
    }

    [Fact]
    public void InitialFolder_IsDocuments_WithoutAFolderSavedToLast()
    {
        Assert.Equal(Documents, new SaveLocation(_settings).InitialFolder());
    }

    [Fact]
    public void InitialFolder_IsDocuments_WhenTheFolderSavedToLastIsGone()
    {
        _settings.Get<string>("lastSaveFolder").Returns(Path.Combine(_folder, "gone"));

        Assert.Equal(Documents, new SaveLocation(_settings).InitialFolder());
    }

    [Fact]
    public void Remember_KeepsTheFolderOfTheSavedFile()
    {
        new SaveLocation(_settings).Remember(Path.Combine(_folder, "Auswertung.pdf"));

        _settings.Received(1).Set("lastSaveFolder", _folder);
        _settings.Received(1).Save();
    }

    [Fact]
    public void UniqueFileName_NumbersTheNameWhenItIsTaken()
    {
        Assert.Equal("Auswertung.pdf", SaveLocation.UniqueFileName(_folder, "Auswertung", ".pdf"));

        File.WriteAllText(Path.Combine(_folder, "Auswertung.pdf"), "");
        File.WriteAllText(Path.Combine(_folder, "Auswertung (2).pdf"), "");

        Assert.Equal("Auswertung (3).pdf", SaveLocation.UniqueFileName(_folder, "Auswertung", ".pdf"));
        // Another extension is another file.
        Assert.Equal("Auswertung.csv", SaveLocation.UniqueFileName(_folder, "Auswertung", ".csv"));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
