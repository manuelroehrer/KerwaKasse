using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.ViewModel;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KerwaKasse.Tests.ViewModels;

public class EventsViewModelTests
{
    private const string KerwaPath = @"C:\data\database\kerwa.db";
    private const string HerbstPath = @"C:\data\database\herbstkerwa.db";

    private readonly IEventCatalog _catalog;
    private readonly IDatabaseBackupService _backupService;
    private readonly IDialogService _dialogService;

    public EventsViewModelTests()
    {
        _catalog = Substitute.For<IEventCatalog>();
        _backupService = Substitute.For<IDatabaseBackupService>();
        _dialogService = Substitute.For<IDialogService>();

        _catalog.ActiveFilePath.Returns(KerwaPath);
        _catalog.GetAll().Returns(new List<EventDatabase>
        {
            new(HerbstPath, "Herbstkerwa", 8, 0, null, null),
            new(KerwaPath, "Kerwa", 20, 150, new DateTime(2026, 7, 11), new DateTime(2026, 7, 13))
        });
    }

    private EventsViewModel CreateSut() =>
        new(_catalog, _backupService, _dialogService, NullLogger<EventsViewModel>.Instance);

    private void AnswerNewEventDialog(NewEventRequest request) =>
        _dialogService.ShowNewEventDialogAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<Func<string, bool>>()).Returns(request);

    [Fact]
    public void Constructor_ListsEventsAndMarksTheActiveOne()
    {
        var sut = CreateSut();

        Assert.Equal(2, sut.Events.Count);
        Assert.Equal("Kerwa", sut.ActiveEventName);
        Assert.False(sut.Events[0].IsActive);
        Assert.True(sut.Events[1].IsActive);
        Assert.Equal("8 Produkte · noch keine Verkäufe", sut.Events[0].Details);
        Assert.Equal("20 Produkte · 150 Verkäufe · 11.07.2026 – 13.07.2026", sut.Events[1].Details);
        Assert.Equal("20 Produkte und 150 Verkäufe (11.07.2026 – 13.07.2026)", sut.Events[1].ContentsText);
    }

    [Fact]
    public void Refresh_PicksUpChangedKeyFigures()
    {
        var sut = CreateSut();
        _catalog.GetAll().Returns(new List<EventDatabase>
        {
            new(HerbstPath, "Herbstkerwa", 8, 0, null, null),
            new(KerwaPath, "Kerwa", 21, 151, new DateTime(2026, 7, 11), new DateTime(2026, 9, 28))
        });

        sut.Refresh();

        Assert.Equal(21, sut.ActiveEvent.Source.Products);
        Assert.Equal(151, sut.ActiveEvent.Source.Orders);
    }

    [Fact]
    public void Switch_Confirmed_SwitchesAndRaisesEvent()
    {
        _dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var sut = CreateSut();
        bool raised = false;
        sut.ActiveEventSwitched += (_, _) => raised = true;

        sut.SwitchCommand.Execute(sut.Events[0]);

        _catalog.Received(1).SetActive(HerbstPath);
        Assert.True(raised);
    }

    [Fact]
    public void Switch_Cancelled_KeepsActiveEvent()
    {
        _dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        var sut = CreateSut();

        sut.SwitchCommand.Execute(sut.Events[0]);

        _catalog.DidNotReceive().SetActive(Arg.Any<string>());
    }

    [Fact]
    public void Create_Empty_CreatesEvent()
    {
        AnswerNewEventDialog(new NewEventRequest("Sommerfest", null));
        _catalog.Create("Sommerfest")
            .Returns(new EventDatabase(@"C:\data\database\sommerfest.db", "Sommerfest", 0, 0, null, null));
        var sut = CreateSut();
        bool changed = false;
        sut.EventsChanged += (_, _) => changed = true;

        sut.CreateCommand.Execute(null);

        _catalog.Received(1).Create("Sommerfest");
        Assert.True(changed);
    }

    [Fact]
    public void Create_FromBackup_ImportsChosenFile()
    {
        AnswerNewEventDialog(new NewEventRequest("Kerwa 2025", @"D:\backup.db"));
        _catalog.Import(@"D:\backup.db", "Kerwa 2025")
            .Returns(new EventDatabase(@"C:\data\database\kerwa-2025.db", "Kerwa 2025", 20, 900, null, null));
        var sut = CreateSut();

        sut.CreateCommand.Execute(null);

        _catalog.Received(1).Import(@"D:\backup.db", "Kerwa 2025");
        _catalog.DidNotReceive().Create(Arg.Any<string>());
    }

    [Fact]
    public void Create_DialogChecksBackupFilesWithTheBackupService()
    {
        Func<string, bool> isValidBackup = null;
        _dialogService.ShowNewEventDialogAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Do<Func<string, bool>>(f => isValidBackup = f))
            .Returns((NewEventRequest)null);
        _backupService.IsValidDatabaseFile(@"D:\junk.db").Returns(false);
        var sut = CreateSut();

        sut.CreateCommand.Execute(null);

        Assert.False(isValidBackup(@"D:\junk.db"));
        _backupService.Received(1).IsValidDatabaseFile(@"D:\junk.db");
    }

    [Fact]
    public void Create_DuplicateName_IsRejected()
    {
        AnswerNewEventDialog(new NewEventRequest("herbstkerwa", null));
        var sut = CreateSut();

        sut.CreateCommand.Execute(null);

        _catalog.DidNotReceive().Create(Arg.Any<string>());
        _dialogService.Received(1).ShowError(Arg.Is<string>(m => m.Contains("bereits")));
    }

    [Fact]
    public void Rename_RenamesAndRaisesChanged()
    {
        _dialogService.ShowTextInputAsync(Arg.Any<string>(), Arg.Any<string>(), "Kerwa").Returns("Kerwa 2026");
        var sut = CreateSut();
        bool changed = false;
        sut.EventsChanged += (_, _) => changed = true;

        sut.RenameCommand.Execute(sut.Events[1]);

        _catalog.Received(1).Rename(KerwaPath, "Kerwa 2026");
        Assert.True(changed);
    }

    [Fact]
    public void Delete_ActiveEvent_DoesNothing()
    {
        var sut = CreateSut();

        sut.DeleteCommand.Execute(sut.Events[1]);

        _catalog.DidNotReceive().Delete(Arg.Any<string>());
        _dialogService.DidNotReceive().ShowDeleteEventDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Func<string>>());
    }

    [Fact]
    public void Delete_DescribesContents_WithoutFullStopAfterTheDateRange()
    {
        _catalog.ActiveFilePath.Returns(HerbstPath);
        var sut = CreateSut();

        sut.DeleteCommand.Execute(sut.Events[1]); // Kerwa, not active here

        _dialogService.Received(1).ShowDeleteEventDialogAsync("Kerwa",
            "„Kerwa“ enthält 20 Produkte und 150 Verkäufe (11.07.2026 – 13.07.2026)", Arg.Any<Func<string>>());
    }

    [Fact]
    public void Delete_NotConfirmed_KeepsEvent()
    {
        _dialogService.ShowDeleteEventDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Func<string>>()).Returns(false);
        var sut = CreateSut();

        sut.DeleteCommand.Execute(sut.Events[0]);

        _catalog.DidNotReceive().Delete(Arg.Any<string>());
    }

    [Fact]
    public void Delete_Confirmed_DeletesEvent()
    {
        _dialogService.ShowDeleteEventDialogAsync("Herbstkerwa", Arg.Any<string>(), Arg.Any<Func<string>>()).Returns(true);
        var sut = CreateSut();

        sut.DeleteCommand.Execute(sut.Events[0]);

        _catalog.Received(1).Delete(HerbstPath);
    }

    [Fact]
    public void Delete_BackupFromDialog_SavesTheEventToBeDeleted()
    {
        Func<string> createBackup = null;
        _dialogService.ShowDeleteEventDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Do<Func<string>>(f => createBackup = f))
            .Returns(false);
        _dialogService.ShowSaveFileDialog(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns((string)null);
        var sut = CreateSut();

        sut.DeleteCommand.Execute(sut.Events[0]);
        createBackup();

        _dialogService.Received(1).ShowSaveFileDialog(Arg.Any<string>(), Arg.Is<string>(t => t.Contains("Herbstkerwa")),
            Arg.Is<string>(n => n.StartsWith("kerwakasse_backup_herbstkerwa_")));
    }
}
