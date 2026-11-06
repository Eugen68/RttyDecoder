using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using HamContest;
using HamContest.Udc;

namespace RttyDecoder.Wpf.Contest;

public sealed class ContestViewModel : ObservableObject
{
    private readonly ContestFiles _files;
    private readonly ContestStationResolver _resolver;
    private readonly IContestHost _host;
    private ContestSession? _session;
    private ContestDefinition? _definition;
    private PendingContestContact? _pending;
    private Guid? _editing;
    private (string Report, string Fixed)? _beforeEdit;
    private bool _updating;
    private string _ownCall = "", _ownSection = "", _ownGrid = "", _ownWpx = "", _firstSerial = "1";
    private string _sentReport = "599", _sentExchange = "", _status = "Alegeti o definitie UDC si creati o sesiune.", _preview = "", _diagnostics = "", _sessionPath = "", _summary = "";
    private ContestDefinitionFile? _selectedDefinition;
    private ContestQsoRow? _selectedQso;
    private const string EmptyHeaders = "CATEGORY-OPERATOR: \nCATEGORY-BAND: \nCATEGORY-POWER: \nCATEGORY-ASSISTED: \nCATEGORY-TRANSMITTER: \nOPERATORS: ";
    private string _cabrilloHeaders = EmptyHeaders;
    public ObservableCollection<ContestDefinitionFile> Definitions { get; } = new();
    public ObservableCollection<ContestQsoRow> Contacts { get; } = new();
    public ContestEntry Entry { get; } = new();
    public ContestFiles Files => _files;
    public bool HasSession => _session != null;
    public bool IsSetup => !HasSession;
    public bool IsReserved => _pending != null;
    public bool CanChangeSent => !IsReserved && _editing == null;
    public bool CanChangeCall => !IsReserved;
    public bool IsEditing => _editing != null;
    public string LogButtonText => IsEditing ? "Salveaza corectia" : "Logheaza QSO (Enter)";
    public string SessionTitle => _session == null ? "Nicio sesiune activa" : $"{_session.Rules.Name} · {_session.OwnStation.Callsign}";
    public string ExportBaseName => _session == null ? "contest" : _session.Rules.Name + "-" + _session.OwnStation.Callsign.Replace('/', '_');
    public string SerialText => _session == null ? "" : !_session.Rules.UsesSerialNumbers ? "Schimb fix" : IsEditing
        ? "Serialul transmis se pastreaza" : _session.PendingSerial is { } pending ? $"Serial rezervat: {pending.Number:D3}" : $"Urmatorul serial: {_session.NextSerial:D3}";
    public string SessionPath { get => _sessionPath; private set => Set(ref _sessionPath, value); }
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Preview { get => _preview; private set => Set(ref _preview, value); }
    public string Diagnostics { get => _diagnostics; private set => Set(ref _diagnostics, value); }
    public string OwnCall { get => _ownCall; set => Set(ref _ownCall, value ?? ""); }
    public string OwnSection { get => _ownSection; set => Set(ref _ownSection, value ?? ""); }
    public string OwnGrid { get => _ownGrid; set => Set(ref _ownGrid, value ?? ""); }
    public string OwnWpx { get => _ownWpx; set => Set(ref _ownWpx, value ?? ""); }
    public string FirstSerial { get => _firstSerial; set => Set(ref _firstSerial, value ?? ""); }
    public string CabrilloHeaders { get => _cabrilloHeaders; set => Set(ref _cabrilloHeaders, value ?? ""); }
    public string SentReport { get => _sentReport; set { if (CanChangeSent && Set(ref _sentReport, value ?? "")) RefreshPreview(); } }
    public string SentExchange { get => _sentExchange; set { if (CanChangeSent && Set(ref _sentExchange, value ?? "")) RefreshPreview(); } }
    public ContestDefinitionFile? SelectedDefinition
    {
        get => _selectedDefinition;
        set
        {
            if (!Set(ref _selectedDefinition, value) || HasSession) return;
            try
            {
                _definition = value == null ? null : _files.LoadDefinition(value.Path);
                Diagnostics = _definition == null ? "" : string.Join(Environment.NewLine, _definition.Import.Diagnostics.Select(d => $"{d.Severity} · linia {d.Line} · {d.Key}: {d.Message}"));
                Status = _definition == null ? "Alegeti un UDC." : _definition.Import.CanScore
                    ? $"{_definition.Import.RequireRules().DisplayName}. Schimb propus de UDC: {_definition.Import.RequireRules().DefaultSentExchange}. DEMO este numai pentru exercitiu."
                    : "Definitia nu poate fi folosita. Consultati diagnosticele UDC.";
            }
            catch (Exception error) { _definition = null; Status = "Citire UDC: " + error.Message; }
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public ContestQsoRow? SelectedQso { get => _selectedQso; set => Set(ref _selectedQso, value); }
    public ICommand CreateCommand { get; }
    public ICommand LogCommand { get; }
    public ICommand ReserveCommand { get; }
    public ICommand CancelContactCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand CqCommand { get; }
    public ICommand ReportCommand { get; }
    public ICommand AgnCommand { get; }
    public ICommand TuCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand CheckCommand { get; }

    public ContestViewModel(ContestFiles files, ContestStationResolver resolver, IContestHost host)
    {
        _files = files; _resolver = resolver; _host = host; OwnCall = host.MyCall;
        CreateCommand = Command(CreateSession, () => IsSetup && _definition?.Import.CanScore == true);
        LogCommand = Command(LogContact, () => HasSession && !string.IsNullOrWhiteSpace(Entry.Callsign) && !_host.IsBusy);
        ReserveCommand = Command(ReserveSerial, () => HasSession && !IsEditing && !string.IsNullOrWhiteSpace(Entry.Callsign) && !_host.IsBusy);
        CancelContactCommand = Command(CancelContact, () => HasSession && !_host.IsBusy);
        EditCommand = Command(EditSelected, () => HasSession && !IsReserved && SelectedQso != null && !_host.IsBusy);
        CqCommand = Command(() => Send("CQ"), () => HasSession && !IsEditing && !IsReserved && !_host.IsBusy);
        ReportCommand = Command(() => Send("REPORT"), () => HasSession && !IsEditing && !string.IsNullOrWhiteSpace(Entry.Callsign) && !_host.IsBusy);
        AgnCommand = Command(() => Send("AGN"), () => HasSession && !IsEditing && !_host.IsBusy);
        TuCommand = Command(() => Send("TU"), () => HasSession && !IsEditing && !_host.IsBusy);
        StopCommand = new RelayCommand(host.Stop);
        CheckCommand = new RelayCommand(RefreshPreview);
        Entry.PropertyChanged += (_, e) => { if (!_updating) { if (e.PropertyName == nameof(ContestEntry.UseCat)) SyncFrequency(); RefreshPreview(); } };
        RefreshDefinitions(); SyncFrequency();
    }
    private ICommand Command(Action action, Func<bool> enabled) => new RelayCommand(() => Run(action), enabled);
    public bool Run(Action action)
    {
        try { action(); return true; }
        catch (Exception error) { Status = error.Message; return false; }
    }
    public void RefreshDefinitions(string? selectPath = null)
    {
        if (HasSession) return;
        _selectedDefinition = null; _definition = null; OnPropertyChanged(nameof(SelectedDefinition));
        Definitions.Clear(); foreach (var definition in _files.ListDefinitions()) Definitions.Add(definition);
        SelectedDefinition = Definitions.FirstOrDefault(d => d.Path == selectPath) ?? Definitions.FirstOrDefault();
    }
    public void ImportFiles(IEnumerable<string> paths)
    {
        string? selected = null;
        foreach (string path in paths) { string target = _files.Import(path); if (System.IO.Path.GetExtension(target).Equals(".udc", StringComparison.OrdinalIgnoreCase)) selected = target; }
        RefreshDefinitions(selected); Status = "Fisiere importate in UserDefinedContests / SupportFiles. Sesiunea activa pastreaza propria definitie.";
    }
    public void SyncFrequency()
    {
        if (Entry.UseCat && !IsEditing && _host.FrequencyHz is { } frequency)
        {
            string text = (frequency / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
            if (text == Entry.FrequencyKhz) return;
            _updating = true; Entry.FrequencyKhz = text; _updating = false; RefreshPreview();
        }
    }
    public void SelectCallsign(string call)
    {
        if (IsReserved || IsEditing) { Status = "QSO-ul curent este fixat. Logati-l sau anulati-l inainte de a alege alt indicativ."; return; }
        _updating = true; Entry.Callsign = call; Entry.Wpx = ContestStationResolver.SimpleWpx(call); _updating = false;
        SyncFrequency(); RefreshPreview();
    }

    public void CreateSession()
    {
        if (HasSession || _definition?.Import.CanScore != true) throw new InvalidOperationException("Alegeti o definitie valida pentru o sesiune noua.");
        var rules = _definition.Import.RequireRules();
        string mode = _definition.Import.Document.Entries.FirstOrDefault(e => e.Section.Equals("Contest", StringComparison.OrdinalIgnoreCase) && e.Key.Equals("Mode", StringComparison.OrdinalIgnoreCase))?.Value.Trim().ToUpperInvariant() ?? "CW";
        if (mode is not ("RTTY" or "BOTH")) throw new InvalidOperationException("Acest modul foloseste RTTY. Alegeti un UDC cu Mode=RTTY sau BOTH.");
        if (!int.TryParse(FirstSerial, NumberStyles.None, CultureInfo.InvariantCulture, out int first) || first < 1) throw new ArgumentException("Serialul de inceput trebuie sa fie un numar pozitiv.");
        var own = _resolver.Resolve(OwnCall, rules.RequiredCountryIdentityKind, EmptyNull(OwnWpx), OwnSection, OwnGrid);
        var ownIssues = rules.Validate(new Qso
        {
            Time = DateTimeOffset.UtcNow, FrequencyHz = 14_080_000, Station = own,
            Sent = new() { Serial = rules.UsesSerialNumbers ? first : null },
            Received = new() { Value = own.CqZone?.ToString(CultureInfo.InvariantCulture) ?? "", Section = own.Section, Grid = own.Grid }
        }, own).Where(issue => issue.Field.StartsWith("Own", StringComparison.Ordinal) || issue.Field is "CountryPrefix" or "CountryIdentityKind" or "Continent").ToArray();
        if (ownIssues.Length != 0) throw new ArgumentException("Completati datele statiei proprii: " + string.Join("; ", ownIssues.Select(issue => issue.Message)));
        var session = new ContestSession(rules, own, first);
        string path = _files.NewSessionPath(rules.Name);
        var previousPath = SessionPath; SessionPath = path;
        try { SaveCandidate(session, null, Entry.Capture()); }
        catch { SessionPath = previousPath; throw; }
        _files.Remember(path); Status = "Sesiune creata. QSO-urile si rezervarile se salveaza automat.";
    }

    public void LoadSession(string path)
    {
        EnsureIdle(); if (HasSession && !Flush()) throw new IOException("Sesiunea curenta nu a putut fi salvata.");
        var document = _files.Load(path);
        var imported = UdcImporter.Import(document.DefinitionText, document.SectionTexts.Select(SectionList.Parse));
        var rules = imported.RequireRules();
        var session = SessionStore.Deserialize(document.SessionJson, rules);
        if ((session.PendingSerial != null) != (document.PendingContact != null && rules.UsesSerialNumbers)) throw new InvalidDataException("Rezervarea si QSO-ul in asteptare nu corespund.");
        if (document.PendingContact is { } bound && (bound.Callsign.Length == 0 || document.Draft is { } draft && !draft.Callsign.Trim().Equals(bound.Callsign, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Indicativul rezervarii nu corespunde formularului salvat.");
        session.Score();
        _definition = new(document.DefinitionFileName, document.DefinitionText, document.SectionTexts, imported);
        _session = session; _pending = document.PendingContact; _editing = null; _beforeEdit = null; SessionPath = System.IO.Path.GetFullPath(path);
        OwnCall = session.OwnStation.Callsign; OwnSection = session.OwnStation.Section; OwnGrid = session.OwnStation.Grid; OwnWpx = session.OwnStation.WpxPrefix;
        _sentReport = _pending?.Report ?? document.SentReport; _sentExchange = _pending?.FixedExchange ?? document.FixedExchange;
        CabrilloHeaders = document.CabrilloHeaders;
        _updating = true; Entry.Apply(document.Draft ?? new()); _updating = false;
        if (_pending != null) { Entry.Callsign = _pending.Callsign; }
        Diagnostics = string.Join(Environment.NewLine, imported.Diagnostics.Select(d => $"{d.Severity} · {d.Key}: {d.Message}"));
        _files.Remember(path); Refresh(); Status = "Sesiune redeschisa; rezervarea existenta este pastrata.";
    }

    public void EndSession()
    {
        EnsureIdle(); if (!Flush()) throw new IOException("Sesiunea nu a putut fi salvata.");
        _session = null; _pending = null; _editing = null; _beforeEdit = null; SessionPath = ""; Contacts.Clear(); Summary = "";
        _sentReport = "599"; _sentExchange = ""; FirstSerial = "1"; CabrilloHeaders = EmptyHeaders;
        _updating = true; Entry.Apply(new()); _updating = false; RefreshDefinitions(); NotifyState();
    }

    private ContestSession Clone() => ContestSession.Restore(_session!.Rules, _session.CaptureState());
    private ContestDocument Document(ContestSession candidate, PendingContestContact? pending, ContestDraft draft) =>
        new(1, _definition!.FileName, _definition.Text, _definition.Sections, SessionStore.Serialize(candidate), _beforeEdit?.Report ?? SentReport, _beforeEdit?.Fixed ?? SentExchange, pending, draft, CabrilloHeaders);
    private void SaveCandidate(ContestSession candidate, PendingContestContact? pending, ContestDraft draft)
    {
        candidate.Score();
        _files.Save(SessionPath, Document(candidate, pending, draft)); // publish in-memory state only AFTER durable replacement
        if (_beforeEdit is { } original) { _sentReport = original.Report; _sentExchange = original.Fixed; _beforeEdit = null; }
        _session = candidate; _pending = pending; _editing = null;
        _updating = true; Entry.Apply(draft); _updating = false; Refresh();
    }
    public bool Flush()
    {
        if (_session == null) return true;
        try { _files.Save(SessionPath, Document(_session, _pending, IsEditing ? new(FrequencyKhz: Entry.FrequencyKhz, UseCat: Entry.UseCat) : Entry.Capture())); return true; }
        catch (Exception error) { Status = "Salvarea a esuat: " + error.Message; return false; }
    }

    public void ReserveSerial()
    {
        EnsureIdle(); EnsureSession(); if (IsEditing) throw new InvalidOperationException("Finalizati editarea inainte de rezervare.");
        if (_pending != null) return;
        var rules = (UdcContestRules)_session!.Rules;
        var station = _resolver.Resolve(Entry.Callsign, rules.RequiredCountryIdentityKind, EmptyNull(Entry.Wpx));
        ValidateSent(); SyncFrequency(); ReadFrequency();
        var candidate = Clone(); if (rules.UsesSerialNumbers) candidate.ReserveSerial();
        var pending = new PendingContestContact(station.Callsign, SentReport, SentExchange, DateTimeOffset.UtcNow);
        SaveCandidate(candidate, pending, Entry.Capture() with { Callsign = station.Callsign });
        Status = "Schimb fixat si salvat. Repetarea foloseste acelasi serial.";
    }

    public void LogContact()
    {
        EnsureIdle(); EnsureSession(); SyncFrequency();
        var candidate = Clone();
        if (_editing is { } id) candidate.Replace(BuildQso(id));
        else
        {
            var qso = BuildQso(Guid.NewGuid()); // validate received data before spending a new serial
            if (candidate.Rules.UsesSerialNumbers && candidate.PendingSerial == null) candidate.ReserveSerial();
            candidate.Log(qso, candidate.PendingSerial);
        }
        var empty = new ContestDraft(FrequencyKhz: Entry.FrequencyKhz, UseCat: Entry.UseCat);
        SaveCandidate(candidate, null, empty); Status = "QSO salvat.";
    }

    public void CancelContact()
    {
        EnsureIdle(); EnsureSession();
        var candidate = Clone(); if (_editing == null && candidate.PendingSerial is { } serial) candidate.AbandonSerial(serial);
        SaveCandidate(candidate, null, new(FrequencyKhz: Entry.FrequencyKhz, UseCat: Entry.UseCat));
        Status = "Formular eliberat; serialele deja rezervate nu sunt refolosite.";
    }
    public void EditSelected()
    {
        EnsureIdle(); EnsureSession(); if (IsReserved || SelectedQso == null) throw new InvalidOperationException("Selectati un QSO si finalizati rezervarea curenta.");
        var qso = SelectedQso.Qso; _beforeEdit ??= (SentReport, SentExchange); _editing = qso.Id; _sentReport = qso.Sent.Report; _sentExchange = qso.Sent.Value;
        _updating = true;
        Entry.Apply(new(qso.Station.Callsign, (qso.FrequencyHz / 1000m).ToString(CultureInfo.InvariantCulture), qso.Received.Report,
            qso.Received.Serial?.ToString(CultureInfo.InvariantCulture) ?? "", qso.Received.Value, qso.Received.Name, qso.Received.Misc,
            qso.Received.Section, qso.Received.Grid, qso.Received.Comment, qso.Station.WpxPrefix, qso.Time.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"), false));
        _updating = false; NotifyState(); RefreshPreview();
    }
    public void DeleteSelected()
    {
        EnsureIdle(); EnsureSession(); if (IsReserved || IsEditing || SelectedQso == null) throw new InvalidOperationException("Selectati un QSO, fara rezervare/editare activa.");
        var candidate = Clone(); candidate.Remove(SelectedQso.Qso.Id); SaveCandidate(candidate, null, Entry.Capture()); Status = "QSO sters; scorul a fost recalculat.";
    }

    private Qso BuildQso(Guid id)
    {
        var rules = (UdcContestRules)_session!.Rules;
        string call = Entry.Callsign.Trim().ToUpperInvariant();
        if (_pending != null && call != _pending.Callsign) throw new InvalidOperationException("Indicativul difera de QSO-ul rezervat.");
        DateTimeOffset time = _pending?.Time ?? DateTimeOffset.UtcNow;
        if (Entry.TimeUtc.Length != 0)
        {
            if (!DateTimeOffset.TryParseExact(Entry.TimeUtc, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time))
                throw new ArgumentException("Ora UTC trebuie sa fie yyyy-MM-dd HH:mm:ss sau goala pentru ora curenta.");
        }
        int? receivedSerial = null;
        if (Entry.Serial.Length != 0)
        { if (!int.TryParse(Entry.Serial, NumberStyles.None, CultureInfo.InvariantCulture, out int serial) || serial <= 0) throw new ArgumentException("Numarul primit trebuie sa fie pozitiv."); receivedSerial = serial; }
        int? sentSerial = _editing is { } edit ? _session.Contacts.First(q => q.Id == edit).Sent.Serial : _session.PendingSerial?.Number;
        ValidateSent();
        return new()
        {
            Id = id, Time = time, FrequencyHz = ReadFrequency(), Mode = ContactMode.RTTY,
            Station = _resolver.Resolve(call, rules.RequiredCountryIdentityKind, EmptyNull(Entry.Wpx)),
            Sent = new() { Report = _pending?.Report ?? SentReport, Serial = sentSerial, Value = _pending?.FixedExchange ?? SentExchange },
            Received = new() { Report = Entry.Report.Trim(), Serial = receivedSerial, Value = Entry.Exchange.Trim(), Misc = Entry.Misc.Trim(), Name = Entry.Name.Trim(),
                Section = Entry.Section.Trim(), Grid = Entry.Grid.Trim().ToUpperInvariant(), Comment = Entry.Comment.Trim() }
        };
    }
    private long ReadFrequency()
    {
        if (Entry.UseCat && !IsEditing && _host.FrequencyHz == null) throw new ArgumentException("CAT indisponibil. Debifati CAT si introduceti frecventa RF manual.");
        if (!decimal.TryParse(Entry.FrequencyKhz.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal khz) || khz <= 0 || khz > 30_000)
            throw new ArgumentException("Completati frecventa RF in kHz (CAT sau manual), de exemplu 14080.");
        return checked((long)decimal.Round(khz * 1000));
    }
    private void ValidateSent()
    {
        foreach (string value in new[] { SentReport, SentExchange }) if (value.Any(char.IsControl)) throw new ArgumentException("Schimbul transmis trebuie sa fie pe o singura linie.");
        if (!IsEditing && _session?.Rules is UdcContestRules rules)
        {
            string[] proposed = rules.DefaultSentExchange.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool needsFixedPart = proposed.Length != 0 && (proposed[0] != "001" || proposed.Length > 1);
            if (needsFixedPart && string.IsNullOrWhiteSpace(SentExchange))
                throw new ArgumentException("Completati schimbul fix transmis, conform regulamentului. Textul propus de UDC este: " + rules.DefaultSentExchange);
        }
    }
    private void EnsureIdle() { if (_host.IsBusy) throw new InvalidOperationException("Asteptati terminarea emisiei sau apasati STOP."); }
    private void EnsureSession() { if (_session == null) throw new InvalidOperationException("Creati sau deschideti o sesiune."); }
    private static string? EmptyNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public void Send(string kind)
    {
        EnsureIdle(); EnsureSession(); if (IsEditing) throw new InvalidOperationException("Incheiati editarea inainte de emisie.");
        string own = _session!.OwnStation.Callsign;
        string text;
        if (kind == "CQ")
        {
            if (IsReserved) throw new InvalidOperationException("Logati sau anulati QSO-ul curent inainte de CQ.");
            text = $"CQ TEST DE {own} {own} K";
        }
        else if (kind == "REPORT")
        {
            ReserveSerial();
            string exchange = string.Join(" ", new[] { _session.PendingSerial?.Number.ToString("D3"), _pending!.FixedExchange }.Where(s => !string.IsNullOrWhiteSpace(s)));
            text = $"{_pending.Callsign} {_pending.Report} {exchange} {exchange} DE {own} K";
        }
        else if (kind == "AGN") text = "AGN AGN PSE";
        else if (kind == "TU") text = $"{(IsReserved ? _pending!.Callsign : Entry.Callsign.Trim().ToUpperInvariant())} TU DE {own} K";
        else throw new ArgumentException("Mesaj necunoscut.");
        if (!_host.TryTransmit(text)) throw new InvalidOperationException("Mesajul nu a fost acceptat pentru emisie. Verificati statia si configuratia FSK in aplicatia principala.");
        Status = "Mesaj acceptat pentru emisie. Logarea QSO-ului se face explicit.";
    }

    public void RefreshPreview()
    {
        if (_updating || _session == null) return;
        if (string.IsNullOrWhiteSpace(Entry.Callsign) && !IsReserved && !IsEditing)
        { Preview = "Selectati un indicativ din decodor sau completati formularul."; return; }
        try
        {
            var candidate = BuildQso(_editing ?? Guid.NewGuid());
            if (_session.Rules.UsesSerialNumbers && candidate.Sent.Serial == null) candidate = candidate with { Sent = candidate.Sent with { Serial = _session.NextSerial } };
            var errors = _session.Rules.Validate(candidate, _session.OwnStation);
            if (!errors.IsEmpty) { Preview = "De verificat: " + string.Join("; ", errors.Select(e => e.Message)); return; }
            var existing = _session.Contacts.Where(q => q.Id != candidate.Id);
            var result = _session.Rules.Score(existing.Append(candidate), _session.OwnStation).Contacts.First(r => r.Qso.Id == candidate.Id);
            Preview = result.IsDupe ? "DUBLU — QSO-ul va ramane in jurnal, cu zero puncte." : $"Valid · {result.Points} puncte · {result.NewMultipliers.Length} multiplicatori noi";
        }
        catch (Exception error) { Preview = error.Message; }
    }
    private void Refresh()
    {
        Contacts.Clear();
        if (_session != null)
        {
            var score = _session.Score();
            foreach (var item in score.Contacts.Reverse()) Contacts.Add(new(item.Qso, item.IsDupe, item.Points, item.NewMultipliers));
            Summary = $"{Contacts.Count} QSO · {score.Contacts.Count(q => q.IsDupe)} duble · {score.QsoPoints} puncte · {score.ScoringMultipliers} mult. · SCOR {score.Total}";
        }
        NotifyState(); RefreshPreview();
    }
    private void NotifyState()
    {
        foreach (string property in new[] { nameof(HasSession), nameof(IsSetup), nameof(IsReserved), nameof(CanChangeCall), nameof(CanChangeSent), nameof(IsEditing), nameof(LogButtonText), nameof(SessionTitle), nameof(SerialText), nameof(SentReport), nameof(SentExchange) }) OnPropertyChanged(property);
        CommandManager.InvalidateRequerySuggested();
    }
    public string ExportCabrillo()
    {
        EnsureSession(); if (!Flush()) throw new IOException("Salvarea sesiunii a esuat.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in CabrilloHeaders.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) throw new ArgumentException("Antet Cabrillo invalid. Folositi CHEIE: valoare, cate unul pe linie.");
            string key = line[..colon].Trim().ToUpperInvariant(), value = line[(colon + 1)..].Trim();
            if (value.Length != 0 && !headers.TryAdd(key, value)) throw new ArgumentException("Antet Cabrillo repetat: " + key);
        }
        foreach (string key in new[] { "CATEGORY-OPERATOR", "CATEGORY-BAND", "CATEGORY-POWER", "CATEGORY-ASSISTED", "CATEGORY-TRANSMITTER" })
            if (!headers.ContainsKey(key)) throw new ArgumentException("Completati categoria in Antete Cabrillo: " + key);
        if (headers.TryGetValue("CATEGORY-MODE", out string? mode) && mode != "RTTY") throw new ArgumentException("Categoria de mod trebuie sa fie RTTY.");
        headers["CATEGORY-MODE"] = "RTTY";
        return CabrilloExporter.Export(_session!, headers);
    }
    public string ExportAdif() { EnsureSession(); if (!Flush()) throw new IOException("Salvarea sesiunii a esuat."); return ContestAdif.Export(_session!); }
}
