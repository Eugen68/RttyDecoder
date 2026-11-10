using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HamContest;
using HamContest.Udc;
using RttyDecoder.Core;
using RttyDecoder.Wpf;
using RttyDecoder.Wpf.Contest;

namespace RttyDecoder.Wpf.Tests;

public class ContestTests
{
    // WPF native HWND callbacks may outlive an individual test action. Keep the STA dispatcher
    // alive for this test host instead of destroying its CLR thread state after every window.
    private static readonly Lazy<Dispatcher> Ui = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            ready.SetResult(dispatcher);
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    });
    private static Task Sta(Func<Task> action) => Ui.Value.InvokeAsync(action).Task.Unwrap().WaitAsync(TimeSpan.FromSeconds(40));
    private sealed class Host : IContestHost
    {
        public long? FrequencyHz { get; set; } = 14_080_000;
        public bool IsBusy { get; set; }
        public string MyCall => "YO9VA";
        public List<string> Sent { get; } = new();
        public Action? BeforeSend { get; set; }
        public bool Accept { get; set; } = true;
        public int Stops { get; private set; }
        public List<bool> AutoQsy { get; } = new();
        public bool TryTransmit(string text, bool allowAutoQsy)
        {
            BeforeSend?.Invoke(); if (!Accept) return false;
            Sent.Add(text); AutoQsy.Add(allowAutoQsy); return true;
        }
        public void Stop() => Stops++;
    }
    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "RttyContest-tests-" + Guid.NewGuid().ToString("N"));
        public Host Host { get; } = new();
        public ContestFiles Files { get; }
        public ContestViewModel Model { get; }
        public bool FailWrites { get; set; }
        public Workspace()
        {
            Files = new(Root, (path, text) => { if (FailWrites) throw new IOException("Simulated disk error"); ContestFiles.AtomicWrite(path, text); });
            Model = new(Files, ContestStationResolver.Load(), Host);
        }
        public void Start() { Model.CreateSession(); Fill(); }
        public void Fill(string call = "YO3ABC") { Model.SelectCallsign(call); Model.Entry.Serial = "12"; Model.Entry.Exchange = "BU"; }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    [Fact]
    public Task RttyopsSuggestsAnAgreedYearFromTheJournalAndRequiresConfirmation() => Sta(() =>
    {
        using var work = new Workspace();
        string path = Path.Combine(work.Files.DefinitionsFolder, "Year.udc");
        File.WriteAllText(path, "[Contest]\nName=RY_OPSWWDX\nCabrilloName=RTTY-OPS-WW-DX\nMode=RTTY\nNumMults=0\nDefaultContestExchange=SeeRules\nCabrilloFormat=0\n");
        work.Model.RefreshDefinitions(path);
        work.Model.CreateSession();
        work.Model.SentExchange = "1999";
        work.Model.SelectCallsign("SM7ABC");
        work.Model.Entry.Exchange = "1984";
        work.Model.LogContact();

        var reopened = new ContestViewModel(work.Files, ContestStationResolver.Load(), work.Host);
        reopened.LoadSession(work.Model.SessionPath);
        work.Host.FrequencyHz = 7_080_000;
        reopened.SelectCallsign("SM7ABC");
        Assert.Contains("1984 · 20m", reopened.HistorySuggestion);
        Assert.Empty(reopened.Entry.Exchange);
        Assert.Empty(reopened.Entry.Serial);

        reopened.ApplyHistoryCommand.Execute(null);
        Assert.Equal("1984", reopened.Entry.Exchange);
        Assert.Empty(reopened.Entry.Serial);
        reopened.SelectCallsign("SM8ABC");
        Assert.Empty(reopened.Entry.Exchange);
        reopened.SelectCallsign("SM7ABC");
        reopened.Entry.Exchange = "1985";
        Assert.False(reopened.HasHistorySuggestion);
        reopened.LogContact();
        reopened.SelectCallsign("SM7ABC");
        Assert.False(reopened.HasHistorySuggestion);
        return Task.CompletedTask;
    });

    [Fact]
    public Task AutoQsyOnlyInSearchAndPounce() => Sta(() =>
    {
        // Run: raportul pleaca fara QSY automat - frecventa de Run nu aluneca spre corespondent
        using var work = new Workspace(); work.Start();
        work.Model.SelectCallsign("YO3ABC");
        work.Model.Send("REPORT");
        Assert.False(work.Host.AutoQsy.Single());
        work.Model.CancelContact();
        // S&P: QSY-ul automat (dupa A-TUNE) e permis - mergi pe frecventa statiei chemate
        work.Model.SearchAndPounce = true;
        work.Model.SelectCallsign("YO3ABD");
        work.Model.Send("REPORT");
        Assert.True(work.Host.AutoQsy.Last());
        return Task.CompletedTask;
    });

    [Fact]
    public Task F1RepeatsOwnCallInSearchAndPounceAfterReserving() => Sta(() =>
    {
        using var work = new Workspace(); work.Start();
        work.Model.SelectCallsign("YO3ABC");
        work.Model.ReserveSerial();
        // Run: F1 = CQ, nu peste un QSO fixat
        Assert.Throws<InvalidOperationException>(() => work.Model.Send("CQ"));
        Assert.False(work.Model.CqCommand.CanExecute(null));
        // S&P: F1 = indicativul propriu, se poate repeta dupa ce ai trimis schimbul
        work.Model.SearchAndPounce = true;
        Assert.True(work.Model.CqCommand.CanExecute(null));
        work.Model.Send("CQ");
        Assert.Single(work.Host.Sent);
        Assert.True(work.Model.IsReserved);
        return Task.CompletedTask;
    });

    [Fact]
    public Task MacrosAreCreatedPerUdcAndReloadedForAReopenedSession() => Sta(() =>
    {
        using var work = new Workspace();
        string macroPath = Path.Combine(work.Files.DefinitionsFolder, "DEMO-RTTY.macros.json");
        Assert.True(File.Exists(macroPath));
        work.Start();
        string sessionPath = work.Model.SessionPath;
        work.Model.SearchAndPounce = true;
        work.Model.SaveMacro(1, new("Schimb", "{RST} {EXCHANGE} DE {MYCALL}"));
        Assert.Contains("599 001 DE YO9VA", work.Model.MacroTooltip(1));
        Assert.Contains("Serial estimat", work.Model.MacroTooltip(1));
        Assert.False(work.Model.IsReserved);

        var reopened = new ContestViewModel(work.Files, ContestStationResolver.Load(), work.Host);
        reopened.LoadSession(sessionPath);
        Assert.Equal("Raport", reopened.GetMacro(1).Name);
        reopened.SearchAndPounce = true;
        Assert.Equal("Schimb", reopened.GetMacro(1).Name);
        reopened.SelectCallsign("YO3ABC");
        reopened.Send("REPORT");
        Assert.Equal("599 001 DE YO9VA", work.Host.Sent.Single());
        Assert.DoesNotContain("Serial estimat", reopened.MacroTooltip(1));

        reopened.EndSession();
        string other = Path.Combine(work.Files.DefinitionsFolder, "Other.udc");
        File.Copy(Path.Combine(work.Files.DefinitionsFolder, "DEMO-RTTY.udc"), other);
        reopened.RefreshDefinitions(other);
        Assert.True(File.Exists(Path.Combine(work.Files.DefinitionsFolder, "Other.macros.json")));
        Assert.Equal("Raport", reopened.GetMacro(1).Name);

        string incoming = Directory.CreateDirectory(Path.Combine(work.Root, "incoming")).FullName;
        string external = Path.Combine(incoming, "Travel.udc");
        File.Copy(other, external);
        File.Copy(macroPath, Path.ChangeExtension(external, ".macros.json"));
        reopened.ImportFiles([external]);
        Assert.Equal("Schimb", reopened.GetMacro(1).Name);
        string importedMacros = Path.Combine(work.Files.DefinitionsFolder, "Travel.macros.json");
        string saved = File.ReadAllText(importedMacros);
        File.WriteAllText(Path.ChangeExtension(external, ".macros.json"), "{}");
        reopened.ImportFiles([external]);
        Assert.Equal(saved, File.ReadAllText(importedMacros));
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReservationIsDurableBeforeTxAndRepeatRestartUseTheSameNumber() => Sta(() =>
    {
        using var work = new Workspace(); work.Start();
        work.Host.BeforeSend = () =>
        {
            var saved = work.Files.Load(work.Model.SessionPath);
            Assert.Equal("YO3ABC", saved.PendingContact!.Callsign);
            Assert.Contains("\"Number\": 1", saved.SessionJson);
        };
        work.Model.Send("REPORT"); work.Model.Send("REPORT");
        Assert.Equal(2, work.Host.Sent.Count); Assert.All(work.Host.Sent, text => Assert.Contains("001 001", text));
        Assert.Empty(work.Model.Contacts);
        // A session stores its own UDC; editing/removing the catalog must not change a running journal.
        File.WriteAllText(Path.Combine(work.Files.DefinitionsFolder, "DEMO-RTTY.udc"), "[Contest]\nName=BROKEN\nNewRule=True");
        var reopened = new ContestViewModel(work.Files, ContestStationResolver.Load(), work.Host);
        reopened.LoadSession(work.Model.SessionPath);
        Assert.True(reopened.IsReserved); Assert.Equal("YO3ABC", reopened.Entry.Callsign);
        reopened.LogContact();
        Assert.Equal(1, reopened.Contacts.Single().Qso.Sent.Serial);
        Assert.Equal(2, reopened.Contacts.Single().Points);
        Assert.Contains("002", reopened.SerialText);
        return Task.CompletedTask;
    });

    [Fact]
    public Task FailedPersistenceCannotTransmitOrSpendANumberAndFailedCancelKeepsReservation() => Sta(() =>
    {
        using var work = new Workspace(); work.Start();
        work.FailWrites = true;
        Assert.False(work.Model.Run(() => work.Model.Send("REPORT")));
        Assert.Empty(work.Host.Sent); Assert.False(work.Model.IsReserved); Assert.Contains("001", work.Model.SerialText);
        work.FailWrites = false; work.Model.ReserveSerial();
        work.FailWrites = true;
        Assert.False(work.Model.Run(work.Model.CancelContact)); Assert.True(work.Model.IsReserved);
        Assert.NotNull(work.Files.Load(work.Model.SessionPath).PendingContact);
        work.FailWrites = false; work.Model.CancelContact(); work.Fill(); work.Model.LogContact();
        Assert.Equal(2, work.Model.Contacts.Single().Qso.Sent.Serial);
        Assert.Empty(work.Host.Sent);
        return Task.CompletedTask;
    });

    [Fact]
    public Task InvalidReceivedExchangeDoesNotAdvanceSerialOrJournal() => Sta(() =>
    {
        using var work = new Workspace(); work.Start(); work.Model.Entry.Exchange = "INVALID";
        Assert.False(work.Model.Run(work.Model.LogContact));
        Assert.Empty(work.Model.Contacts); Assert.Contains("001", work.Model.SerialText);
        work.Model.Entry.Exchange = "BU"; work.Model.LogContact();
        Assert.Equal(1, work.Model.Contacts.Single().Qso.Sent.Serial);
        return Task.CompletedTask;
    });

    [Fact]
    public Task BoundCallCannotBeSwitchedAndBusyHostBlocksChanges() => Sta(() =>
    {
        using var work = new Workspace(); work.Start(); work.Model.ReserveSerial();
        work.Model.SelectCallsign("YO4ABC"); Assert.Equal("YO3ABC", work.Model.Entry.Callsign);
        work.Host.IsBusy = true;
        Assert.Throws<InvalidOperationException>(work.Model.LogContact);
        Assert.Throws<InvalidOperationException>(work.Model.CancelContact);
        work.Host.IsBusy = false; work.Model.LogContact();
        Assert.Equal("YO3ABC", work.Model.Contacts.Single().Call);
        return Task.CompletedTask;
    });

    [Fact]
    public Task EditingAndDeletingRecalculateDupesAndPreserveTransmittedNumbers() => Sta(() =>
    {
        using var work = new Workspace(); work.Start(); work.Model.LogContact();
        work.Fill(); Assert.Contains("DUBLU", work.Model.Preview); work.Model.LogContact();
        Assert.Equal(1, work.Model.Contacts.Count(q => q.IsDupe));
        work.Model.SelectedQso = work.Model.Contacts.Single(q => q.Qso.Sent.Serial == 1); work.Model.EditSelected();
        work.Model.Entry.Callsign = "YO4ABC"; work.Model.LogContact();
        Assert.Equal(1, work.Model.Contacts.Single(q => q.Call == "YO4ABC").Qso.Sent.Serial);
        Assert.Equal(0, work.Model.Contacts.Count(q => q.IsDupe));
        work.Model.SelectedQso = work.Model.Contacts.Single(q => q.Call == "YO4ABC"); work.Model.DeleteSelected();
        Assert.Single(work.Model.Contacts); Assert.Equal(2, work.Model.Contacts.Single().Qso.Sent.Serial);
        var reopened = new ContestViewModel(work.Files, ContestStationResolver.Load(), work.Host); reopened.LoadSession(work.Model.SessionPath);
        Assert.Single(reopened.Contacts); Assert.Contains("003", reopened.SerialText);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ChangedCatalogRulesAreBlockedAndImportDoesNotOverwriteAnExistingFile() => Sta(() =>
    {
        using var work = new Workspace();
        string original = Path.Combine(work.Files.DefinitionsFolder, "DEMO-RTTY.udc");
        string text = File.ReadAllText(original); string source = Path.Combine(work.Root, "DEMO-RTTY.udc");
        File.WriteAllText(source, text.Replace("PointsPerContact=2", "PointsPerContact=3"));
        string imported = work.Files.Import(source);
        Assert.NotEqual(original, imported); Assert.Equal(text, File.ReadAllText(original));
        File.WriteAllText(original, text + "\nUnsupportedRule=True\n"); work.Model.RefreshDefinitions(original);
        Assert.False(work.Model.CreateCommand.CanExecute(null)); Assert.Contains("UnsupportedRule", work.Model.Diagnostics);
        return Task.CompletedTask;
    });

    [Fact]
    public Task CatFrequencyIsRfAndDisconnectRequiresExplicitManualMode() => Sta(() =>
    {
        using var work = new Workspace(); work.Start();
        work.Host.FrequencyHz = null;
        Assert.False(work.Model.Run(work.Model.LogContact)); Assert.Empty(work.Model.Contacts);
        work.Model.Entry.UseCat = false; work.Model.Entry.FrequencyKhz = "7080,125"; work.Model.LogContact();
        Assert.Equal(7_080_125, work.Model.Contacts.Single().Qso.FrequencyHz);
        return Task.CompletedTask;
    });

    [Fact]
    public Task AdifUsesLengthsUtcMhzAndExplicitSerialsWithoutLosingEmbeddedTags() => Sta(() =>
    {
        using var work = new Workspace(); work.Start(); work.Model.Entry.TimeUtc = "2026-11-06 12:13:14";
        work.Model.Entry.Comment = "TEXT <EOR> HERE"; work.Model.LogContact();
        var fields = ParseAdi(work.Model.ExportAdif());
        Assert.Equal("20261106", fields["QSO_DATE"]); Assert.Equal("121314", fields["TIME_ON"]);
        Assert.Equal("14.08", fields["FREQ"]); Assert.Equal("1", fields["STX"]); Assert.Equal("12", fields["SRX"]);
        Assert.Equal("TEXT <EOR> HERE", fields["COMMENT"]); Assert.Equal("YO9VA", fields["STATION_CALLSIGN"]);
        Assert.Single(Directory.GetFiles(work.Files.SessionsFolder, "*.json"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task CabrilloRequiresOperatorCategoryAndAdifDoesNotSilentlyDropUnicode() => Sta(() =>
    {
        using var work = new Workspace(); work.Start(); work.Model.Entry.Comment = "Stefan Ș"; work.Model.LogContact();
        Assert.Throws<ArgumentException>(() => work.Model.ExportAdif());
        Assert.Equal("Stefan Ș", work.Model.Contacts.Single().Qso.Received.Comment);
        Assert.Throws<ArgumentException>(() => work.Model.ExportCabrillo());
        return Task.CompletedTask;
    });

    [Fact]
    public void WaeOverlayOverridesDxccAliasesAndPortableWpxIsExplicit()
    {
        const string source = "Island: 14: 28: EU: 0: 0: 0: *T/i:\n    =T1ABC;\nMainland: 14: 28: EU: 0: 0: 0: T:\n    T,=T1ABC;\n";
        var resolver = new ContestStationResolver(source);
        Assert.Equal("T", resolver.Resolve("T1ABC", CountryIdentityKind.Dxcc).CountryPrefix);
        Assert.Equal("T/i", resolver.Resolve("T1ABC", CountryIdentityKind.Wae).CountryPrefix);
        Assert.Equal("YO9", ContestStationResolver.SimpleWpx("YO9VA"));
        Assert.Equal("", ContestStationResolver.SimpleWpx("DL/YO9VA"));
    }

    [Fact]
    public Task DecoderBridgeCannotTransmitWithoutRigOrAlterTheNormalMacros() => Sta(() =>
    {
        using var rx = new RttyReceiver(() => null!);
        using var decoder = new RttyDecoderViewModel(rx);
        var before = decoder.ExportMacros().Select(m => m.Text).ToArray();
        Assert.False(decoder.TrySendContestText("CQ TEST DE YO9VA", allowAutoQsy: false));
        Assert.False(decoder.Tx.IsTransmitting); Assert.Equal(before, decoder.ExportMacros().Select(m => m.Text));
        return Task.CompletedTask;
    });

    [Fact]
    public Task MissingOwnSectionIsRejectedBeforeCreatingAContestThatExcludesOwnMultiplier() => Sta(() =>
    {
        using var work = new Workspace();
        string path = Path.Combine(work.Files.DefinitionsFolder, "DEMO-RTTY.udc");
        File.AppendAllText(path, "\nDoNotCountMeAsMult=True\n"); work.Model.RefreshDefinitions(path);
        Assert.Throws<ArgumentException>(work.Model.CreateSession); Assert.False(work.Model.HasSession);
        work.Model.OwnSection = "BU"; work.Model.CreateSession(); Assert.True(work.Model.HasSession);
        return Task.CompletedTask;
    });

    [Fact]
    public Task FixedExchangeMustBeFilledBeforeReportOrLog()
        => Sta(() =>
    {
        using var work = new Workspace();
        string path = Path.Combine(work.Files.DefinitionsFolder, "YEAR-RTTY.udc");
        File.WriteAllText(path, "[Contest]\nName=YEAR-RTTY\nMode=RTTY\nNumMults=0\nDefaultContestExchange=SeeRules\nCabrilloFormat=0\n");
        work.Model.RefreshDefinitions(path); work.Model.CreateSession(); work.Fill();
        Assert.Throws<ArgumentException>(() => work.Model.Send("REPORT")); Assert.Empty(work.Host.Sent);
        Assert.Throws<ArgumentException>(work.Model.LogContact); Assert.Empty(work.Model.Contacts);
        work.Model.SentExchange = "1999"; work.Model.Send("REPORT");
        Assert.Contains("1999 1999", work.Host.Sent.Single());
        return Task.CompletedTask;
    });

    [Fact]
    public Task FailedSessionLoadCannotReplaceTheActiveDefinitionOrJournal() => Sta(() =>
    {
        using var work = new Workspace(); work.Start(); work.Model.LogContact();
        string originalPath = work.Model.SessionPath;
        var original = work.Files.Load(originalPath);
        string changedText = original.DefinitionText + "\nPointsMultByBand=14,2147483647\n";
        var changedRules = UdcImporter.Import(changedText, original.SectionTexts.Select(SectionList.Parse)).RequireRules();
        var changedSession = new ContestSession(changedRules, new("YO9VA"));
        changedSession.Log(work.Model.Contacts.Single().Qso, changedSession.ReserveSerial());
        string badPath = Path.Combine(work.Files.SessionsFolder, "bad.contest.json");
        work.Files.Save(badPath, original with { DefinitionText = changedText, SessionJson = SessionStore.Serialize(changedSession) });
        Assert.Throws<OverflowException>(() => work.Model.LoadSession(badPath));
        Assert.Equal(originalPath, work.Model.SessionPath); Assert.True(work.Model.Flush());
        Assert.Equal(original.DefinitionText, work.Files.Load(originalPath).DefinitionText);
        Assert.Equal(2, work.Model.Contacts.Single().Points);
        return Task.CompletedTask;
    });

    [Fact]
    public Task NewSessionDoesNotReusePreviousExchangeCategoryOrStartingSerial() => Sta(() =>
    {
        using var work = new Workspace(); work.Model.FirstSerial = "42"; work.Start();
        work.Model.SentExchange = "OLD"; work.Model.CabrilloHeaders = "CATEGORY-POWER: HIGH";
        work.Model.EndSession();
        Assert.Equal("1", work.Model.FirstSerial); Assert.Equal("", work.Model.SentExchange);
        Assert.DoesNotContain("HIGH", work.Model.CabrilloHeaders);
        work.Model.CreateSession(); Assert.Contains("001", work.Model.SerialText);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SeparateWindowIsModelessAndCanReopenWithoutLosingTheJournal() => Sta(async () =>
    {
        using var work = new Workspace(); var parent = new Window { Width = 600, Height = 400 };
        ContestWindow? window = null;
        try
        {
            parent.Show(); window = new(work.Model) { Owner = parent }; window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(parent.IsEnabled); Assert.True(window.IsVisible);
            var menu = Assert.IsType<Menu>(window.FindName("ContestMenu"));
            Assert.Equal(new[] { "_Fișiere", "_Setări", "_Deasupra decodorului", "_Unelte", "_Ajutor" }, menu.Items.OfType<MenuItem>().Select(item => Assert.IsType<string>(item.Header)));
            var keepAbove = Assert.IsType<MenuItem>(menu.Items[2]);
            Assert.True(keepAbove.IsCheckable);
            Assert.True(keepAbove.IsChecked);
            var tools = Assert.IsType<MenuItem>(menu.Items[3]);
            Assert.All(tools.Items.OfType<MenuItem>(), item => Assert.False(item.IsEnabled));
            Capture(window, "contest-setup.png");
            work.Start(); work.Model.ReserveSerial();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Capture(window, "contest-reserved.png");
            work.Model.LogContact();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.All(tools.Items.OfType<MenuItem>(), item => Assert.True(item.IsEnabled));
            Capture(window, "contest-journal.png");
            window.Close(); Assert.True(parent.IsEnabled); Assert.Equal(0, work.Host.Stops);
            window = new(work.Model) { Owner = parent, Width = 920, Height = 640 }; window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Single(work.Model.Contacts); Capture(window, "contest-small.png");

            work.Model.EndSession();
            var reopenedMenu = Assert.IsType<Menu>(window.FindName("ContestMenu"));
            var files = Assert.IsType<MenuItem>(reopenedMenu.Items[0]);
            var resume = Assert.IsType<MenuItem>(files.Items[2]);
            resume.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Single(work.Model.Contacts);
        }
        finally { window?.Close(); parent.Close(); }
    });

    private static Dictionary<string, string> ParseAdi(string text)
    {
        var fields = new Dictionary<string, string>(); int index = 0;
        while ((index = text.IndexOf('<', index)) >= 0)
        {
            int end = text.IndexOf('>', index); string header = text[(index + 1)..end]; index = end + 1;
            int colon = header.IndexOf(':'); if (colon < 0) continue;
            string key = header[..colon]; int length = int.Parse(header[(colon + 1)..]); fields[key] = text.Substring(index, length); index += length;
        }
        return fields;
    }
    private static void Capture(Window window, string name)
    {
        string? folder = Environment.GetEnvironmentVariable("RTTY_UI_ARTIFACTS"); if (folder == null) return;
        Directory.CreateDirectory(folder); window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(Path.Combine(folder, name)); encoder.Save(file);
    }
}
