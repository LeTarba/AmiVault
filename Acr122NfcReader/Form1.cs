using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCSC;
using PCSC.Iso7816;

namespace Acr122NfcReader;

public partial class Form1 : Form
{
    private const byte FirstUserPage = 4;
    private const byte LastUserPage = 129;
    private const byte DynamicLockPage = 130;
    private const byte ConfigPage0 = 131;
    private const byte ConfigPage1 = 132;
    private const byte PasswordPage = 133;
    private const byte PackPage = 134;
    private const int UserMemoryLength = 504;
    private const int AmiiboHeadOffsetInUserMemory = 0x54 - (FirstUserPage * 4);
    private const int AmiiboTailOffsetInUserMemory = 0x58 - (FirstUserPage * 4);
    private const int AmiiboVariantOffsetInUserMemory = 0x5C - (FirstUserPage * 4);
    private static readonly JsonSerializerOptions AppStateJsonOptions = new() { WriteIndented = true };
    private static readonly byte[] AmiiboDynamicLockBytes = [0x01, 0x00, 0x0F, 0xBD];
    private static readonly byte[] AmiiboStaticLockBytes = [0x00, 0x00, 0x0F, 0xE0];
    private static readonly HttpClient AmiiboApiClient = new()
    {
        BaseAddress = new Uri("https://amiiboapi.org/")
    };

    private readonly IContextFactory _contextFactory = ContextFactory.Instance;
    private readonly Dictionary<string, AmiiboApiEntry?> _amiiboApiCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> _amiiboImageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<AmiiboBinItem, Panel> _amiiboCards = [];
    private readonly Dictionary<string, List<AmiiboBinItem>> _amiiboFolderGroups = new(StringComparer.OrdinalIgnoreCase);
    private bool _isPolling;
    private bool _isRunningTool;
    private string? _lastStatus;
    private TagSnapshot? _lastSnapshot;
    private List<AmiiboBinItem> _amiiboBinItems = [];
    private int _amiiboSelectionVersion;
    private AmiiboDumpPreparer.AmiiboKeySet? _amiiboKeys;
    private AmiiboBinItem? _selectedAmiiboBinItem;
    private Panel? _selectedAmiiboCard;
    private string? _amiiboFolderPath;
    private string? _keyFolderPath;
    private string _amiiboKeyStatus = "Non chargees";
    private string _uiLanguage = "Français";

    public Form1()
    {
        InitializeComponent();
    }

    private void Form1_Load(object sender, EventArgs e)
    {
        var appState = LoadAppState();
        var defaultBinRoot = FindWorkspaceDirectory("Amiibo Bin");
        var defaultKeyRoot = FindWorkspaceDirectory("!Essential Files");
        var initialBinRoot = Directory.Exists(appState.AmiiboFolder)
            ? appState.AmiiboFolder
            : defaultBinRoot;
        var initialKeyRoot = Directory.Exists(appState.KeyFolder)
            ? appState.KeyFolder
            : defaultKeyRoot;
        _amiiboFolderPath = initialBinRoot;
        _keyFolderPath = initialKeyRoot;
        _uiLanguage = string.IsNullOrWhiteSpace(appState.UiLanguage) ? "Français" : appState.UiLanguage;
        chkAmiiboDryRun.Checked = appState.AmiiboDryRun;
        lblAmiiboSelectedName.Text = NoAmiiboSelectedText();
        txtAmiiboInfo.Text = L(
            "Ouvre les paramètres pour choisir le dossier principal des .bin amiibo puis sélectionne un amiibo pour voir ses informations.",
            "Open Settings to choose the main folder for amiibo .bin files, then select an amiibo to view its information.",
            "Abre Configuración para elegir la carpeta principal de los .bin amiibo y luego selecciona un amiibo para ver su información.",
            "Offne die Einstellungen, um den Hauptordner der amiibo-.bin-Dateien auszuwahlen, und wahle dann ein amiibo aus, um seine Informationen anzuzeigen.");
        UpdateSettingsMenuState();
        ApplyUiLanguage();
        LoadAmiiboKeys(initialKeyRoot);
        if (!string.IsNullOrWhiteSpace(initialBinRoot))
        {
            LoadAmiiboBinLibrary(initialBinRoot);
        }

        RefreshReaders();
        pollTimer.Start();
    }

    private void Form1_FormClosing(object sender, FormClosingEventArgs e)
    {
        pollTimer.Stop();
        SaveAppState();
    }

    private string L(string french, string english, string spanish, string german)
    {
        return _uiLanguage switch
        {
            "Espagnol" => spanish,
            "Allemand" => german,
            "Anglais" => english,
            _ => french
        };
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool StartsWithAny(string value, params string[] prefixes)
    {
        return prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private string YesText() => L("Oui", "Yes", "Si", "Ja");

    private string NoText() => L("Non", "No", "No", "Nein");

    private string NoneText() => L("aucune", "none", "ninguna", "keine");

    private string UnavailableText() => L("indisponible", "unavailable", "no disponible", "nicht verfugbar");

    private string NoAmiiboSelectedText() => L(
        "Aucun amiibo sélectionné",
        "No amiibo selected",
        "Ningun amiibo seleccionado",
        "Kein amiibo ausgewahlt");

    private string TagDetectedLabelText() => L(
        "Tag détecté",
        "Detected tag",
        "Etiqueta detectada",
        "Erkanntes Tag");

    private string AboutTitleText() => L("À propos", "About", "Acerca de", "Info");

    private string AmiiboWriteActionTitle() => L(
        "Écriture amiibo",
        "Amiibo write",
        "Escritura amiibo",
        "Amiibo schreiben");

    private void btnRefreshReaders_Click(object sender, EventArgs e)
    {
        RefreshReaders();
    }

    private async void btnDump_Click(object sender, EventArgs e)
    {
        await ExecutePythonToolAsync(
            "nfc_dump.py",
            ["--no-prompt"],
            "Dump NTAG215",
            "Dump en lecture seule du tag et sauvegarde des fichiers .bin/.txt.");
    }

    private async void btnResetTag_Click(object sender, EventArgs e)
    {
        var result = MessageBox.Show(
            L(
                "Cette action efface le contenu utilisateur du tag et tente une remise a zero.\r\n\r\nContinuer ?",
                "This action erases the tag user data and tries to reset it.\r\n\r\nContinue?",
                "Esta accion borra el contenido de usuario de la etiqueta e intenta reiniciarla.\r\n\r\nContinuar?",
                "Diese Aktion loscht den Benutzerinhalt des Tags und versucht, es zuruckzusetzen.\r\n\r\nFortfahren?"),
            L(
                "Confirmation remise a zero",
                "Reset confirmation",
                "Confirmacion de reinicio",
                "Bestatigung Zurucksetzen"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            return;
        }

        await ExecutePythonToolAsync(
            "nfc_reset.py",
            ["--once", "--no-prompt", "--yes", "--try-known-amiibo-pwd"],
            "Remise a zero NTAG215",
            "Effacement, reinitialisation de la configuration et verification finale.");
    }

    private async void btnWriteTest_Click(object sender, EventArgs e)
    {
        var result = MessageBox.Show(
            L(
                "Ce test ecrit sur toute la memoire utilisateur puis remet le tag a zero.\r\n\r\nLancer le test d'ecriture ?",
                "This test writes across the whole user memory and then resets the tag.\r\n\r\nStart the write test?",
                "Esta prueba escribe en toda la memoria de usuario y luego reinicia la etiqueta.\r\n\r\nIniciar la prueba de escritura?",
                "Dieser Test schreibt den gesamten Benutzerspeicher und setzt das Tag anschliessend zuruck.\r\n\r\nSchreibtest starten?"),
            L(
                "Confirmation test ecriture",
                "Write test confirmation",
                "Confirmacion de prueba de escritura",
                "Bestatigung Schreibtest"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            return;
        }

        await ExecutePythonToolAsync(
            "nfc_writetest.py",
            ["--once", "--no-prompt", "--yes"],
            "Test ecriture NTAG215",
            "Test complet de l'ecriture utilisateur avec verification et remise a zero.");
    }

    private async void btnWriteAmiibo_Click(object sender, EventArgs e)
    {
        EnsureAmiiboKeysLoaded();
        var dryRun = chkAmiiboDryRun.Checked;

        var selectedBinPath = _selectedAmiiboBinItem?.FullPath;
        if (string.IsNullOrWhiteSpace(selectedBinPath))
        {
            using var dialog = new OpenFileDialog
            {
                Title = L(
                    "Choisir un fichier amiibo .bin",
                    "Choose an amiibo .bin file",
                    "Elegir un archivo amiibo .bin",
                    "Amiibo-.bin-Datei auswahlen"),
                Filter = L(
                    "Fichiers amiibo (*.bin)|*.bin|Tous les fichiers (*.*)|*.*",
                    "Amiibo files (*.bin)|*.bin|All files (*.*)|*.*",
                    "Archivos amiibo (*.bin)|*.bin|Todos los archivos (*.*)|*.*",
                    "Amiibo-Dateien (*.bin)|*.bin|Alle Dateien (*.*)|*.*"),
                InitialDirectory = !string.IsNullOrWhiteSpace(_amiiboFolderPath) && Directory.Exists(_amiiboFolderPath)
                    ? _amiiboFolderPath
                    : FindWorkspaceDirectory("Amiibo Bin") ?? Environment.CurrentDirectory,
                RestoreDirectory = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            selectedBinPath = dialog.FileName;
        }

        var result = MessageBox.Show(
            (dryRun
                ? L(
                    "Cette action prepare et verifie l'ecriture amiibo sans rien modifier sur le tag.\r\n\r\n",
                    "This action prepares and verifies amiibo writing without changing anything on the tag.\r\n\r\n",
                    "Esta accion prepara y verifica la escritura amiibo sin modificar la etiqueta.\r\n\r\n",
                    "Diese Aktion bereitet das Amiibo-Schreiben vor und verifiziert es, ohne das Tag zu verandern.\r\n\r\n")
                : L(
                    "Cette action ecrit un dump amiibo sur le NTAG215 present sur le lecteur et pose les verrous finaux.\r\n\r\n",
                    "This action writes an amiibo dump to the NTAG215 on the reader and applies the final locks.\r\n\r\n",
                    "Esta accion escribe un dump amiibo en el NTAG215 presente en el lector y aplica los bloqueos finales.\r\n\r\n",
                    "Diese Aktion schreibt einen Amiibo-Dump auf das NTAG215 im Leser und setzt die finalen Sperren.\r\n\r\n")) +
            L(
                "N'utilise cette fonction que pour tes propres sauvegardes et ne retire pas le tag pendant l'ecriture.\r\n\r\nContinuer ?",
                "Use this feature only for your own backups and do not remove the tag while writing.\r\n\r\nContinue?",
                "Usa esta funcion solo para tus propias copias y no retires la etiqueta durante la escritura.\r\n\r\nContinuar?",
                "Verwende diese Funktion nur fur deine eigenen Sicherungen und entferne das Tag wahrend des Schreibens nicht.\r\n\r\nFortfahren?"),
            L(
                "Confirmation ecriture amiibo",
                "Amiibo write confirmation",
                "Confirmacion de escritura amiibo",
                "Bestatigung Amiibo-Schreiben"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            return;
        }

        SaveAppState();
        await ExecuteAmiiboWriteAsync(selectedBinPath, dryRun);
    }

    private void chkAmiiboDryRun_CheckedChanged(object sender, EventArgs e)
    {
        SaveAppState();
    }

    private void menuItemSettings_Click(object sender, EventArgs e)
    {
        using var dialog = new SettingsDialog(_keyFolderPath, _amiiboFolderPath, _amiiboKeyStatus, _uiLanguage);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var normalizedKeyFolder = string.IsNullOrWhiteSpace(dialog.KeyFolderPath) ? null : dialog.KeyFolderPath.Trim();
        var normalizedAmiiboFolder = string.IsNullOrWhiteSpace(dialog.AmiiboFolderPath) ? null : dialog.AmiiboFolderPath.Trim();
        var shouldReloadLibrary = !string.Equals(_amiiboFolderPath, normalizedAmiiboFolder, StringComparison.OrdinalIgnoreCase);

        _keyFolderPath = normalizedKeyFolder;
        _amiiboFolderPath = normalizedAmiiboFolder;
        _uiLanguage = dialog.SelectedLanguage;
        ApplyUiLanguage();
        LoadAmiiboKeys(_keyFolderPath);
        if (shouldReloadLibrary || _amiiboBinItems.Count == 0)
        {
            LoadAmiiboBinLibrary(_amiiboFolderPath);
        }

        SaveAppState();
    }

    private void menuItemAbout_Click(object sender, EventArgs e)
    {
        MessageBox.Show(
            this,
            L(
                "AmiVault\n\nCréé par LeTarba et Trae.",
                "AmiVault\n\nCreated by LeTarba and Trae.",
                "AmiVault\n\nCreado por LeTarba y Trae.",
                "AmiVault\n\nErstellt von LeTarba und Trae."),
            AboutTitleText(),
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void btnRefreshLibrary_Click(object sender, EventArgs e)
    {
        LoadAmiiboBinLibrary(_amiiboFolderPath);
        SaveAppState();
    }

    private void pollTimer_Tick(object sender, EventArgs e)
    {
        if (_isPolling || _isRunningTool)
        {
            return;
        }

        ReadSelectedTag();
    }

    private void RefreshReaders()
    {
        try
        {
            using var context = _contextFactory.Establish(SCardScope.System);
            var readers = context.GetReaders() ?? Array.Empty<string>();
            var previousSelection = cbReaders.SelectedItem as string;

            cbReaders.BeginUpdate();
            cbReaders.Items.Clear();
            cbReaders.Items.AddRange(readers);
            cbReaders.EndUpdate();

            if (!string.IsNullOrWhiteSpace(previousSelection) && readers.Contains(previousSelection))
            {
                cbReaders.SelectedItem = previousSelection;
            }
            else if (cbReaders.Items.Count > 0)
            {
                cbReaders.SelectedIndex = 0;
            }

            if (readers.Length == 0)
            {
                UpdateDisplay(L(
                    "Aucun lecteur PC/SC détecté.",
                    "No PC/SC reader detected.",
                    "No se detecto ningun lector PC/SC.",
                    "Kein PC/SC-Leser erkannt."), "-", "-", "-", "-", "-");
            }
            else
            {
                AddLog($"{L("Lecteur(s) détecté(s)", "Detected reader(s)", "Lector(es) detectado(s)", "Erkannte Leser")}: {string.Join(", ", readers)}");
                UpdateDisplay(
                    L(
                        "Lecteur prêt. Approchez un tag NFC.",
                        "Reader ready. Present an NFC tag.",
                        "Lector listo. Acerca una etiqueta NFC.",
                        "Leser bereit. Halte ein NFC-Tag davor."),
                    _lastSnapshot?.Verdict ?? "-",
                    _lastSnapshot?.Uid ?? "-",
                    _lastSnapshot?.Atr ?? "-",
                    _lastSnapshot?.BlankStatus ?? "-",
                    _lastSnapshot?.WritableStatus ?? "-");
            }
        }
        catch (Exception ex)
        {
            UpdateDisplay($"{L("Erreur lors de la recherche des lecteurs", "Reader scan error", "Error al buscar lectores", "Fehler bei der Lesersuche")}: {ex.Message}", "-", "-", "-", "-", "-");
            AddLog($"{L("Recherche lecteurs impossible", "Reader scan failed", "No se pudo buscar lectores", "Lesersuche fehlgeschlagen")}: {ex.Message}");
        }
    }

    private void ReadSelectedTag(bool forceRefresh = false)
    {
        var readerName = cbReaders.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(readerName))
        {
            UpdateDisplay(L(
                "Selectionnez un lecteur ACR122.",
                "Select an ACR122 reader.",
                "Selecciona un lector ACR122.",
                "Wahle einen ACR122-Leser aus."), "-", "-", "-", "-", "-");
            return;
        }

        _isPolling = true;

        try
        {
            using var context = _contextFactory.Establish(SCardScope.System);
            var readers = context.GetReaders() ?? Array.Empty<string>();
            if (!readers.Contains(readerName))
            {
                UpdateDisplay(L(
                    "Le lecteur selectionne n'est plus disponible.",
                    "The selected reader is no longer available.",
                    "El lector seleccionado ya no esta disponible.",
                    "Der ausgewahlte Leser ist nicht mehr verfugbar."), "-", "-", "-", "-", "-");
                return;
            }

            using var isoReader = new IsoReader(
                context,
                readerName,
                SCardShareMode.Shared,
                SCardProtocol.Any,
                false);

            var atr = ReadAtr(context, readerName);
            var uid = ReadUid(isoReader);

            if (!forceRefresh &&
                _lastSnapshot is { AnalysisComplete: true } cachedSnapshot &&
                string.Equals(cachedSnapshot.Uid, uid, StringComparison.Ordinal))
            {
                UpdateDisplay(
                    cachedSnapshot.Status,
                    cachedSnapshot.Verdict,
                    cachedSnapshot.Uid,
                    cachedSnapshot.Atr,
                    cachedSnapshot.BlankStatus,
                    cachedSnapshot.WritableStatus);
                return;
            }

            var analysis = AnalyzeNtag215(isoReader);
            var snapshot = new TagSnapshot(
                analysis.Status,
                analysis.Verdict,
                uid,
                string.IsNullOrWhiteSpace(atr) ? "-" : atr,
                analysis.BlankStatus,
                analysis.WritableStatus,
                true,
                analysis.AmiiboIdentity);

            UpdateDisplay(
                snapshot.Status,
                snapshot.Verdict,
                snapshot.Uid,
                snapshot.Atr,
                snapshot.BlankStatus,
                snapshot.WritableStatus);

            if (!string.Equals(_lastSnapshot?.Uid, uid, StringComparison.Ordinal))
            {
                AddLog($"{L("Tag lu sur", "Tag read on", "Etiqueta leida en", "Tag gelesen auf")} '{readerName}': UID = {uid}");
            }

            AddLog($"{L("Verdict", "Verdict", "Veredicto", "Urteil")}: {analysis.Verdict}");
            AddLog(analysis.Diagnostics);
            _lastSnapshot = snapshot;
            UpdateDetectedTagAmiiboInfoAsync(snapshot.Uid, snapshot.AmiiboIdentity);
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (message.Contains("smart card", StringComparison.OrdinalIgnoreCase) &&
                (message.Contains("not present", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("no smart card", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("removed", StringComparison.OrdinalIgnoreCase)))
            {
                _lastSnapshot = null;
                UpdateDisplay(L(
                    "En attente d'un tag NFC...",
                    "Waiting for an NFC tag...",
                    "Esperando una etiqueta NFC...",
                    "Warte auf ein NFC-Tag..."), "-", "-", "-", "-", "-");
            }
            else
            {
                UpdateDisplay($"{L("Erreur NFC", "NFC error", "Error NFC", "NFC-Fehler")}: {message}", "-", "-", "-", "-", "-");
                AddLog($"{L("Erreur NFC", "NFC error", "Error NFC", "NFC-Fehler")}: {message}");
            }
        }
        finally
        {
            _isPolling = false;
        }
    }

    private string ReadAtr(ISCardContext context, string readerName)
    {
        using var cardReader = context.ConnectReader(
            readerName,
            SCardShareMode.Shared,
            SCardProtocol.Any);

        var atrBytes = cardReader.GetAttrib(SCardAttribute.AtrString) ?? Array.Empty<byte>();
        return atrBytes.Length == 0 ? "-" : BitConverter.ToString(atrBytes);
    }

    private string ReadUid(IsoReader isoReader)
    {
        var uidBytes = ReadUidBytes(isoReader);
        return uidBytes.Length == 0 ? "(vide)" : BitConverter.ToString(uidBytes);
    }

    private byte[] ReadUidBytes(IsoReader isoReader)
    {
        var response = TransmitCase2(isoReader, 0xCA, 0x00, 0x00, 0x00);
        EnsureSuccess(response, "lecture UID");
        return response.GetData() ?? Array.Empty<byte>();
    }

    private TagAnalysis AnalyzeNtag215(IsoReader isoReader)
    {
        var version = ReadGetVersion(isoReader);
        var tagType = DescribeTagType(version);
        var firstPages = ReadBinary(isoReader, 0, 16);
        var userMemory = ReadFullUserMemory(isoReader);
        var protectionPages = ReadBinary(isoReader, DynamicLockPage, 20);
        var signature = ReadSignature(isoReader);
        var counter = ReadCounter(isoReader);

        var staticLockBytes = firstPages.Skip(10).Take(2).ToArray();
        var page0 = firstPages.Take(4).ToArray();
        var page1 = firstPages.Skip(4).Take(4).ToArray();
        var page2 = firstPages.Skip(8).Take(4).ToArray();
        var ccPage = firstPages.Skip(12).Take(4).ToArray();
        var dynamicLockBytes = protectionPages.Take(3).ToArray();
        var cfg0 = protectionPages.Skip(4).Take(4).ToArray();
        var cfg1 = protectionPages.Skip(8).Take(4).ToArray();
        var passwordPage = protectionPages.Skip(12).Take(4).ToArray();
        var packPage = protectionPages.Skip(16).Take(4).ToArray();
        var auth0 = protectionPages[7];
        var access = protectionPages[8];
        var hasProtection = auth0 <= LastUserPage;
        var readProtected = (access & 0x80) != 0;
        var uidPageLayout = $"{BitConverter.ToString(page0)} {BitConverter.ToString(page1)} {BitConverter.ToString(page2)}";
        var uidPageInfo = BuildUidPageInfo(page0, page1, page2);
        var ccInfo = BuildCcInfo(ccPage);
        var blankStatus = BuildBlankStatus(userMemory, ccPage);
        var writableStatus = BuildWritableStatus(staticLockBytes, dynamicLockBytes, auth0, readProtected);
        var amiiboIdentity = ExtractAmiiboTagIdentity(userMemory);
        var configInfo = BuildConfigInfo(cfg0, cfg1, passwordPage, packPage, auth0, access);
        var counterInfo = counter is null ? L("indisponible ou desactive", "unavailable or disabled", "no disponible o desactivado", "nicht verfugbar oder deaktiviert") : BitConverter.ToString(counter);
        var signatureInfo = signature is null ? UnavailableText() : BitConverter.ToString(signature);
        var versionInfo = version is null ? UnavailableText() : BitConverter.ToString(version);
        var isNtag215 = IsLikelyNtag215(version, protectionPages.Length);
        var signatureLooksAuthentic = signature is { Length: 32 } && signature.Distinct().Count() > 4;
        var status = isNtag215
            ? $"{TagDetectedLabelText()}: {tagType}"
            : L(
                $"Tag détecté, type à confirmer ({tagType})",
                $"Detected tag, type to confirm ({tagType})",
                $"Etiqueta detectada, tipo por confirmar ({tagType})",
                $"Tag erkannt, Typ zu bestatigen ({tagType})");
        var verdict = BuildVerdict(tagType, isNtag215, signatureLooksAuthentic, blankStatus, writableStatus, hasProtection, readProtected);

        var diagnostics =
            $"Type={tagType}, GET_VERSION={versionInfo}, READ_SIG={signatureInfo}, {L("compteur NFC", "NFC counter", "contador NFC", "NFC-Zahler")}={counterInfo}. " +
            $"{L("Pages", "Pages", "Paginas", "Seiten")} 0-3={uidPageLayout}, UID/BCC={uidPageInfo}, CC={ccInfo}. " +
            $"{L("Lock statique", "Static lock", "Bloqueo estatico", "Statische Sperre")}={BitConverter.ToString(staticLockBytes)}, {L("lock dynamique", "dynamic lock", "bloqueo dinamico", "dynamische Sperre")}={BitConverter.ToString(dynamicLockBytes)}. " +
            (amiiboIdentity is null
                ? $"Amiibo ID={UnavailableText()}. "
                : $"Amiibo ID={amiiboIdentity.ApiId}, {L("variant", "variant", "variante", "Variante")}={amiiboIdentity.Variant ?? NoneText()}. ") +
            $"{configInfo}. {L("Protection", "Protection", "Proteccion", "Schutz")}={(hasProtection ? L("active", "active", "activa", "aktiv") : L("inactive", "inactive", "inactiva", "inaktiv"))}, {L("lecture protegee", "read protected", "lectura protegida", "Leseschutz")}={(readProtected ? YesText().ToLowerInvariant() : NoText().ToLowerInvariant())}.";

        return new TagAnalysis(status, verdict, blankStatus, writableStatus, diagnostics, amiiboIdentity);
    }

    private static AmiiboTagIdentity? ExtractAmiiboTagIdentity(byte[] userMemory)
    {
        if (userMemory.Length < AmiiboVariantOffsetInUserMemory + 4)
        {
            return null;
        }

        var head = Convert.ToHexString(userMemory.AsSpan(AmiiboHeadOffsetInUserMemory, 4));
        var tail = Convert.ToHexString(userMemory.AsSpan(AmiiboTailOffsetInUserMemory, 4));
        if (head == "00000000" || tail == "00000000")
        {
            return null;
        }

        var variant = Convert.ToHexString(userMemory.AsSpan(AmiiboVariantOffsetInUserMemory, 4));
        return new AmiiboTagIdentity(head, tail, variant == "00000000" ? null : variant);
    }

    private byte[] ReadFullUserMemory(IsoReader isoReader)
    {
        var userMemory = new byte[UserMemoryLength];

        for (var page = FirstUserPage; page <= LastUserPage; page++)
        {
            var pageData = ReadBinary(isoReader, page, 4);
            Buffer.BlockCopy(pageData, 0, userMemory, (page - FirstUserPage) * 4, 4);
        }

        return userMemory;
    }

    private byte[] ReadBinary(IsoReader isoReader, byte page, int length)
    {
        if (length <= 0 || length % 4 != 0)
        {
            throw new InvalidOperationException(
                $"Longueur de lecture invalide pour la page {page}: {length} octet(s).");
        }

        var buffer = new List<byte>(length);
        var currentPage = page;
        var remaining = length;

        while (remaining > 0)
        {
            var chunkLength = remaining >= 16 ? 16 : 4;
            var chunk = ReadBinaryChunkWithFallback(isoReader, currentPage, chunkLength);

            buffer.AddRange(chunk);
            currentPage += (byte)(chunk.Length / 4);
            remaining -= chunk.Length;
        }

        return buffer.ToArray();
    }

    private byte[] ReadBinaryChunkWithFallback(IsoReader isoReader, byte page, int preferredLength)
    {
        try
        {
            return ReadBinaryChunk(isoReader, page, preferredLength);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("SW=6300", StringComparison.Ordinal))
        {
            if (preferredLength == 16)
            {
                AddLog($"Lecture page {page} refusee en 0x10, nouvelle tentative en 0x04.");

                try
                {
                    return ReadBinaryChunk(isoReader, page, 4);
                }
                catch (InvalidOperationException retryEx) when (retryEx.Message.Contains("SW=6300", StringComparison.Ordinal))
                {
                    AddLog($"Lecture page {page} refusee aussi en 0x04, tentative en commande directe 0x30.");
                    return ReadDirectTagPages(isoReader, page, preferredLength);
                }
            }

            AddLog($"Lecture page {page} refusee en 0x04, tentative en commande directe 0x30.");
            return ReadDirectTagPages(isoReader, page, preferredLength);
        }
    }

    private byte[] ReadBinaryChunk(IsoReader isoReader, byte page, int length)
    {
        var response = TransmitCase2(isoReader, 0xB0, 0x00, page, length);
        EnsureSuccess(response, $"lecture page {page}");

        var data = response.GetData() ?? Array.Empty<byte>();
        if (data.Length < length)
        {
            throw new InvalidOperationException(
                $"La lecture de la page {page} a retourne {data.Length} octet(s) au lieu de {length}.");
        }

        return data.Take(length).ToArray();
    }

    private byte[] ReadDirectTagPages(IsoReader isoReader, byte page, int requestedLength)
    {
        var response = TransmitPn532CommunicateThru(isoReader, 0x30, page);

        var result = ParsePn532Response(response, 0xD5, 0x43);
        if (page == 0)
        {
            AddLog($"READ_DIRECT page 0 brut: {result.RawHex}");
        }

        if (result.Pn532Status is not 0x00)
        {
            throw new InvalidOperationException(
                $"Echec de lecture directe page {page} (PN532=0x{result.Pn532Status:X2}, SW={response.SW1:X2}{response.SW2:X2}).");
        }

        if (result.Data.Length < 16)
        {
            throw new InvalidOperationException(
                $"Lecture directe page {page} incomplete ({result.Data.Length} octet(s), brut={result.RawHex}).");
        }

        return result.Data.Take(requestedLength).ToArray();
    }

    private byte[]? ReadGetVersion(IsoReader isoReader)
    {
        var response = TransmitPn532CommunicateThru(isoReader, 0x60);
        var result = ParsePn532Response(response, 0xD5, 0x43);
        AddLog($"GET_VERSION brut: {result.RawHex}");
        return result.Pn532Status == 0x00 ? result.Data : null;
    }

    private byte[]? ReadSignature(IsoReader isoReader)
    {
        var response = TransmitPn532CommunicateThru(isoReader, 0x3C, 0x00);
        var result = ParsePn532Response(response, 0xD5, 0x43);
        return result.Pn532Status == 0x00 ? result.Data : null;
    }

    private byte[]? ReadCounter(IsoReader isoReader)
    {
        var response = TransmitPn532CommunicateThru(isoReader, 0x39, 0x02);
        var result = ParsePn532Response(response, 0xD5, 0x43);
        return result.Pn532Status == 0x00 ? result.Data : null;
    }

    private static bool IsLikelyNtag215(byte[]? version, int readableBytesAtEnd)
    {
        return version is { Length: >= 8 } &&
               version[1] == 0x04 &&
               version[2] == 0x04 &&
               version[6] == 0x11 &&
               readableBytesAtEnd >= 20;
    }

    private static string DescribeTagType(byte[]? version)
    {
        if (version is not { Length: >= 8 })
        {
            return "type inconnu";
        }

        if (version[1] == 0x04 && version[2] == 0x04)
        {
            return version[6] switch
            {
                0x0F => "NTAG213",
                0x11 => "NTAG215",
                0x13 => "NTAG216",
                _ => $"NXP Type 2 (taille 0x{version[6]:X2})"
            };
        }

        return $"tag non reconnu (GET_VERSION={BitConverter.ToString(version)})";
    }

    private static string BuildUidPageInfo(byte[] page0, byte[] page1, byte[] page2)
    {
        if (page0.Length < 4 || page1.Length < 4 || page2.Length < 4)
        {
            return "pages UID incompletes";
        }

        var reconstructedUid = new[]
        {
            page0[0], page0[1], page0[2], page1[0], page1[1], page1[2], page1[3]
        };

        var bcc0 = page0[3];
        var bcc1 = page2[0];
        return $"UID pages={BitConverter.ToString(reconstructedUid)}, BCC0=0x{bcc0:X2}, BCC1=0x{bcc1:X2}";
    }

    private static string BuildCcInfo(byte[] ccPage)
    {
        if (ccPage.Length < 4)
        {
            return "page CC incomplete";
        }

        if (ccPage.SequenceEqual(new byte[] { 0xE1, 0x10, 0x3E, 0x00 }))
        {
            return "CC vierge standard E1-10-3E-00";
        }

        return $"CC={BitConverter.ToString(ccPage)}";
    }

    private static string BuildConfigInfo(
        byte[] cfg0,
        byte[] cfg1,
        byte[] passwordPage,
        byte[] packPage,
        byte auth0,
        byte access)
    {
        var pwdLooksBlank = passwordPage.All(b => b == 0x00);
        var packLooksBlank = packPage.All(b => b == 0x00);
        return $"CFG0={BitConverter.ToString(cfg0)}, CFG1={BitConverter.ToString(cfg1)}, " +
               $"AUTH0=0x{auth0:X2}, ACCESS=0x{access:X2}, " +
               $"PWD_lisible={(pwdLooksBlank ? "00-00-00-00" : BitConverter.ToString(passwordPage))}, " +
               $"PACK_lisible={(packLooksBlank ? "00-00-00-00" : BitConverter.ToString(packPage))}";
    }

    private Pn532Response ParsePn532Response(Response response, byte expectedHeader0, byte expectedHeader1)
    {
        var rawData = response.GetData() ?? Array.Empty<byte>();
        var rawHex = rawData.Length == 0 ? "(vide)" : BitConverter.ToString(rawData);

        if (response.SW1 != 0x90 || response.SW2 != 0x00)
        {
            return new Pn532Response(null, Array.Empty<byte>(), rawHex, false);
        }

        if (rawData.Length >= 3 && rawData[0] == expectedHeader0 && rawData[1] == expectedHeader1)
        {
            return new Pn532Response(rawData[2], rawData.Skip(3).ToArray(), rawHex, true);
        }

        if (rawData.Length >= 2)
        {
            return new Pn532Response(null, rawData, rawHex, false);
        }

        return new Pn532Response(null, Array.Empty<byte>(), rawHex, false);
    }

    private Response TransmitCase2(IsoReader isoReader, byte instruction, byte p1, byte p2, int le)
    {
        var apdu = new CommandApdu(IsoCase.Case2Short, isoReader.ActiveProtocol)
        {
            CLA = 0xFF,
            Instruction = (InstructionCode)instruction,
            P1 = p1,
            P2 = p2,
            Le = le
        };

        return isoReader.Transmit(apdu);
    }

    private Response TransmitCase3(IsoReader isoReader, byte instruction, byte p1, byte p2, byte[] data)
    {
        var apdu = new CommandApdu(IsoCase.Case3Short, isoReader.ActiveProtocol)
        {
            CLA = 0xFF,
            Instruction = (InstructionCode)instruction,
            P1 = p1,
            P2 = p2,
            Data = data
        };

        return isoReader.Transmit(apdu);
    }

    private Response TransmitCase4(
        IsoReader isoReader,
        byte instruction,
        byte p1,
        byte p2,
        byte[] data,
        int le)
    {
        var apdu = new CommandApdu(IsoCase.Case4Short, isoReader.ActiveProtocol)
        {
            CLA = 0xFF,
            Instruction = (InstructionCode)instruction,
            P1 = p1,
            P2 = p2,
            Data = data,
            Le = le
        };

        return isoReader.Transmit(apdu);
    }

    private Response TransmitPn532CommunicateThru(IsoReader isoReader, params byte[] commandData)
    {
        var data = new byte[2 + commandData.Length];
        data[0] = 0xD4;
        data[1] = 0x42;
        Buffer.BlockCopy(commandData, 0, data, 2, commandData.Length);
        return TransmitCase3(isoReader, 0x00, 0x00, 0x00, data);
    }

    private static void EnsureSuccess(Response response, string operation)
    {
        if (response.SW1 != 0x90 || response.SW2 != 0x00)
        {
            throw new InvalidOperationException(
                $"Echec de {operation} (SW={response.SW1:X2}{response.SW2:X2}).");
        }
    }

    private int WritePageWithVerify(IsoReader isoReader, byte page, byte[] data)
    {
        if (data.Length != 4)
        {
            throw new InvalidOperationException($"La page {page} doit etre ecrite avec exactement 4 octets.");
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            WritePageRaw(isoReader, page, data);
            var readBack = ReadBinary(isoReader, page, 4);
            if (readBack.SequenceEqual(data))
            {
                return attempt;
            }
        }

        throw new InvalidOperationException(
            $"Echec de verification apres ecriture de la page {page:X2}.");
    }

    private void WritePageRaw(IsoReader isoReader, byte page, byte[] data)
    {
        try
        {
            var response = TransmitCase3(isoReader, 0xD6, 0x00, page, data);
            EnsureSuccess(response, $"ecriture page {page:X2}");
            return;
        }
        catch (InvalidOperationException)
        {
            var response = TransmitPn532CommunicateThru(isoReader, 0xA2, page, data[0], data[1], data[2], data[3]);
            var result = ParsePn532Response(response, 0xD5, 0x43);
            if (result.Pn532Status is 0x00)
            {
                return;
            }

            var status = result.Pn532Status.HasValue ? $"0x{result.Pn532Status.Value:X2}" : "inconnu";

            throw new InvalidOperationException(
                $"Echec d'ecriture page {page:X2} (PN532={status}, brut={result.RawHex}).");
        }
    }

    private string AuthenticateWithPassword(IsoReader isoReader, byte[] password, string label, byte[]? expectedPack = null)
    {
        if (password.Length != 4)
        {
            throw new InvalidOperationException("Le mot de passe NTAG doit faire 4 octets.");
        }

        var response = TransmitPn532CommunicateThru(
            isoReader,
            0x1B,
            password[0],
            password[1],
            password[2],
            password[3]);
        var result = ParsePn532Response(response, 0xD5, 0x43);
        if (result.Pn532Status is 0x00)
        {
            if (expectedPack is not null)
            {
                if (expectedPack.Length < 2)
                {
                    throw new InvalidOperationException("Le PACK attendu doit contenir au moins 2 octets.");
                }

                if (result.Data.Length < 2 ||
                    result.Data[0] != expectedPack[0] ||
                    result.Data[1] != expectedPack[1])
                {
                    throw new InvalidOperationException(
                        $"PWD_AUTH {label} OK mais PACK inattendu (attendu={BitConverter.ToString(expectedPack.Take(2).ToArray())}, " +
                        $"recu={(result.Data.Length >= 2 ? BitConverter.ToString(result.Data.Take(2).ToArray()) : "(vide)")}, brut={result.RawHex}).");
                }
            }

            return result.RawHex;
        }

        var status = result.Pn532Status.HasValue ? $"0x{result.Pn532Status.Value:X2}" : "inconnu";
        throw new InvalidOperationException(
            $"PWD_AUTH {label} refuse (PN532={status}, brut={result.RawHex}).");
    }

    private string WriteAmiiboSecurityPages(
        IsoReader isoReader,
        byte[] passwordPageData,
        byte[] packPageData,
        List<string> retriedPages)
    {
        if (passwordPageData.Length != 4 || packPageData.Length != 4)
        {
            throw new InvalidOperationException("Les pages PWD/PACK doivent contenir exactement 4 octets.");
        }

        string? passwordAuthRaw = null;
        Exception? lastPasswordError = null;
        var passwordAttempts = 0;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                WritePageRaw(isoReader, PasswordPage, passwordPageData);
                passwordAuthRaw = AuthenticateWithPassword(isoReader, passwordPageData, "apres ecriture PWD");
                passwordAttempts = attempt;
                break;
            }
            catch (Exception ex)
            {
                lastPasswordError = ex;
            }
        }

        if (passwordAuthRaw is null)
        {
            throw new InvalidOperationException("Impossible de valider la page PWD apres ecriture.", lastPasswordError);
        }

        if (passwordAttempts > 1)
        {
            retriedPages.Add($"{PasswordPage:X2}(x{passwordAttempts})");
        }

        string? packAuthRaw = null;
        Exception? lastPackError = null;
        var packAttempts = 0;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                WritePageRaw(isoReader, PackPage, packPageData);
                packAuthRaw = AuthenticateWithPassword(isoReader, passwordPageData, "verification PACK", packPageData);
                packAttempts = attempt;
                break;
            }
            catch (Exception ex)
            {
                lastPackError = ex;
            }
        }

        if (packAuthRaw is null)
        {
            throw new InvalidOperationException("Impossible de valider la page PACK apres ecriture.", lastPackError);
        }

        if (packAttempts > 1)
        {
            retriedPages.Add($"{PackPage:X2}(x{packAttempts})");
        }

        return $"Verification PWD/PACK OK -> {packAuthRaw}";
    }

    private static byte[] BuildLockedStaticPage(byte[] currentPage)
    {
        if (currentPage.Length != 4)
        {
            throw new InvalidOperationException("La page 02 doit contenir exactement 4 octets.");
        }

        return
        [
            currentPage[0],
            currentPage[1],
            AmiiboStaticLockBytes[2],
            AmiiboStaticLockBytes[3]
        ];
    }

    private static byte[] BuildExpectedFinalAmiiboDump(
        AmiiboDumpPreparer.PreparedAmiiboDump prepared,
        byte[] currentPage2)
    {
        var expectedDump = (byte[])prepared.EncryptedPages.Clone();
        var lockedPage2 = BuildLockedStaticPage(currentPage2);
        Buffer.BlockCopy(lockedPage2, 0, expectedDump, 2 * 4, 4);
        Buffer.BlockCopy(AmiiboDynamicLockBytes, 0, expectedDump, DynamicLockPage * 4, 4);
        return expectedDump;
    }

    private string VerifyWrittenAmiiboMatchesSourceBin(
        IsoReader isoReader,
        AmiiboDumpPreparer.PreparedAmiiboDump prepared,
        byte[] currentPage2)
    {
        var expectedDump = BuildExpectedFinalAmiiboDump(prepared, currentPage2);
        var actualDump = ReadBinary(isoReader, 0, (PackPage + 1) * 4);
        var mismatches = new List<string>();
        for (byte page = 0; page <= PackPage && mismatches.Count < 8; page++)
        {
            if (page is PasswordPage or PackPage)
            {
                // NTAG215 masque PWD/PACK en relecture brute: la verification se fait via PWD_AUTH.
                continue;
            }

            var offset = page * 4;
            var expectedPage = expectedDump.Skip(offset).Take(4).ToArray();
            var actualPage = actualDump.Skip(offset).Take(4).ToArray();
            if (!expectedPage.SequenceEqual(actualPage))
            {
                mismatches.Add(
                    $"{page:X2}: attendu={BitConverter.ToString(expectedPage)}, lu={BitConverter.ToString(actualPage)}");
            }
        }

        if (mismatches.Count == 0)
        {
            return "Comparaison avec le bin source OK -> dump final identique (adapte a l'UID du tag, PWD/PACK verifies separement).";
        }

        throw new InvalidOperationException(
            "Le tag relu apres ecriture ne correspond pas au bin source adapte pour ce tag. " +
            string.Join(" ; ", mismatches));
    }

    private string VerifyAmiiboDumpBeforeLocking(
        IsoReader isoReader,
        AmiiboDumpPreparer.PreparedAmiiboDump prepared)
    {
        for (byte page = 3; page < PasswordPage; page++)
        {
            var expectedPageData = prepared.EncryptedPages.Skip(page * 4).Take(4).ToArray();
            var currentPageData = ReadBinary(isoReader, page, 4);
            if (!currentPageData.SequenceEqual(expectedPageData))
            {
                throw new InvalidOperationException(
                    $"Verification finale avant verrouillage en echec sur la page {page:X2}.");
            }
        }

        var passwordPageData = prepared.EncryptedPages.Skip(PasswordPage * 4).Take(4).ToArray();
        var packPageData = prepared.EncryptedPages.Skip(PackPage * 4).Take(4).ToArray();
        var securityVerification = AuthenticateWithPassword(
            isoReader,
            passwordPageData,
            "verification finale avant verrouillage",
            packPageData);
        return $"Verification complete avant verrouillage OK -> {securityVerification}";
    }

    private int WritePageIfNeededWithVerify(IsoReader isoReader, byte page, byte[] expectedData)
    {
        var current = ReadBinary(isoReader, page, 4);
        if (current.SequenceEqual(expectedData))
        {
            return 0;
        }

        return WritePageWithVerify(isoReader, page, expectedData);
    }

    private string BuildBlankStatus(byte[] userMemory, byte[] ccPage)
    {
        if (userMemory.All(b => b == 0x00))
        {
            return ccPage.SequenceEqual(new byte[] { 0xE1, 0x10, 0x3E, 0x00 })
                ? YesText()
                : L("Oui (memoire vide)", "Yes (empty memory)", "Si (memoria vacia)", "Ja (leerer Speicher)");
        }

        var trimmed = TrimTrailingZeroes(userMemory);
        if (trimmed.SequenceEqual(new byte[] { 0xFE }) ||
            trimmed.SequenceEqual(new byte[] { 0x03, 0x00, 0xFE }))
        {
            return L("Oui (NDEF vide)", "Yes (empty NDEF)", "Si (NDEF vacio)", "Ja (leeres NDEF)");
        }

        return NoText();
    }

    private string BuildWritableStatus(
        byte[] staticLockBytes,
        byte[] dynamicLockBytes,
        byte auth0,
        bool readProtected)
    {
        var staticUserLocked = staticLockBytes.Any(b => b != 0x00);
        var dynamicUserLocked = dynamicLockBytes.Any(b => b != 0x00);
        var passwordProtected = auth0 <= LastUserPage;

        if (!staticUserLocked && !dynamicUserLocked && !passwordProtected)
        {
            return YesText();
        }

        if (passwordProtected && !staticUserLocked && !dynamicUserLocked)
        {
            return readProtected
                ? L(
                    $"Non, protege en lecture/ecriture a partir de la page {auth0}",
                    $"No, read/write protected from page {auth0}",
                    $"No, protegido en lectura/escritura desde la pagina {auth0}",
                    $"Nein, ab Seite {auth0} lese-/schreibgeschutzt")
                : L(
                    $"Non sans mot de passe, ecriture protegee a partir de la page {auth0}",
                    $"No without password, write protected from page {auth0}",
                    $"No sin contrasena, protegido contra escritura desde la pagina {auth0}",
                    $"Nein ohne Passwort, ab Seite {auth0} schreibgeschutzt");
        }

        if ((staticUserLocked || dynamicUserLocked) && !passwordProtected)
        {
            return L(
                "Partiellement ou totalement verrouille",
                "Partially or fully locked",
                "Parcial o totalmente bloqueado",
                "Teilweise oder vollstandig gesperrt");
        }

        return L(
            "Non, verrouille et protege",
            "No, locked and protected",
            "No, bloqueado y protegido",
            "Nein, gesperrt und geschutzt");
    }

    private static byte[] BuildAmiiboPassword(byte[] tagUid)
    {
        if (tagUid.Length != 7)
        {
            throw new InvalidOperationException("Le tag doit exposer un UID sur 7 octets pour calculer le mot de passe amiibo.");
        }

        return
        [
            (byte)(0xAA ^ tagUid[1] ^ tagUid[3]),
            (byte)(0x55 ^ tagUid[2] ^ tagUid[4]),
            (byte)(0xAA ^ tagUid[3] ^ tagUid[5]),
            (byte)(0x55 ^ tagUid[4] ^ tagUid[6])
        ];
    }

    private static byte[] TrimTrailingZeroes(byte[] data)
    {
        var lastIndex = data.Length - 1;
        while (lastIndex >= 0 && data[lastIndex] == 0x00)
        {
            lastIndex--;
        }

        if (lastIndex < 0)
        {
            return Array.Empty<byte>();
        }

        return data.Take(lastIndex + 1).ToArray();
    }

    private string BuildVerdict(
        string tagType,
        bool isNtag215,
        bool signatureLooksAuthentic,
        string blankStatus,
        string writableStatus,
        bool hasProtection,
        bool readProtected)
    {
        var authenticity = isNtag215
            ? (signatureLooksAuthentic
                ? L("Probable NTAG215 NXP authentique", "Likely authentic NXP NTAG215", "Probable NTAG215 NXP autentico", "Wahrscheinlich authentisches NXP NTAG215")
                : L("NTAG215 detecte, signature atypique", "NTAG215 detected, unusual signature", "NTAG215 detectado, firma atipica", "NTAG215 erkannt, ungewohnliche Signatur"))
            : L($"Type a confirmer ({tagType})", $"Type to confirm ({tagType})", $"Tipo por confirmar ({tagType})", $"Typ zu bestatigen ({tagType})");

        var blankSummary = StartsWithAny(blankStatus, "Oui", "Yes", "Si", "Ja")
            ? L("tag vierge", "blank tag", "etiqueta vacia", "leeres Tag")
            : L("tag non vierge", "non-blank tag", "etiqueta no vacia", "nicht leeres Tag");

        string writeSummary;
        if (writableStatus == YesText())
        {
            writeSummary = L("inscriptible", "writable", "escribible", "beschreibbar");
        }
        else if (StartsWithAny(writableStatus, "Non sans mot de passe", "No without password", "No sin contrasena", "Nein ohne Passwort"))
        {
            writeSummary = L("ecriture protegee", "write protected", "escritura protegida", "schreibgeschutzt");
        }
        else if (StartsWithAny(writableStatus, "Non", "No", "Nein"))
        {
            writeSummary = L("non inscriptible", "not writable", "no escribible", "nicht beschreibbar");
        }
        else
        {
            writeSummary = L("zones verrouillees", "locked areas", "zonas bloqueadas", "gesperrte Bereiche");
        }

        string protectionSummary;
        if (!hasProtection)
        {
            protectionSummary = L("protection inactive", "protection inactive", "proteccion inactiva", "Schutz inaktiv");
        }
        else if (readProtected)
        {
            protectionSummary = L("protection lecture/ecriture active", "read/write protection active", "proteccion de lectura/escritura activa", "Lese-/Schreibschutz aktiv");
        }
        else
        {
            protectionSummary = L("protection d'ecriture active", "write protection active", "proteccion de escritura activa", "Schreibschutz aktiv");
        }

        return $"{authenticity}, {blankSummary}, {writeSummary}, {protectionSummary}.";
    }

    private async Task ExecutePythonToolAsync(
        string scriptName,
        IReadOnlyList<string> arguments,
        string actionTitle,
        string actionDescription)
    {
        if (_isRunningTool)
        {
            return;
        }

        var scriptPath = FindWorkspaceFile(scriptName);
        if (scriptPath is null)
        {
            var message = $"{L("Script introuvable", "Script not found", "Script no encontrado", "Skript nicht gefunden")}: {scriptName}";
            SetActionOutput(actionTitle, $"{actionDescription}\r\n\r\n{L("Erreur", "Error", "Error", "Fehler")}: {message}");
            AddLog(message);
            ShowActionPopup(actionTitle, message, MessageBoxIcon.Error);
            return;
        }

        try
        {
            SetBusyState(true);
            _lastSnapshot = null;
            SetActionOutput(actionTitle, $"{actionDescription}\r\n\r\n{L("Execution en cours...", "Running...", "Ejecucion en curso...", "Ausfuhrung lauft...")}");
            AddLog($"{actionTitle}: {L("lancement du script", "starting script", "inicio del script", "Starte Skript")} {Path.GetFileName(scriptPath)}.");

            var result = await RunPythonScriptAsync(scriptPath, arguments);
            var details = BuildToolOutput(actionDescription, result);
            SetActionOutput(actionTitle, details);

            var status = result.ExitCode == 0
                ? L("termine avec succes", "completed successfully", "terminado con exito", "erfolgreich abgeschlossen")
                : L($"termine avec code {result.ExitCode}", $"completed with code {result.ExitCode}", $"terminado con codigo {result.ExitCode}", $"mit Code {result.ExitCode} abgeschlossen");
            AddLog($"{actionTitle}: {status}.");

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                AddLog($"{actionTitle} stderr: {FirstLine(result.StandardError)}");
            }

            if (result.ExitCode == 0)
            {
                ShowActionPopup(actionTitle, $"{actionTitle} {L("terminé avec succès.", "completed successfully.", "terminado con exito.", "erfolgreich abgeschlossen.")}", MessageBoxIcon.Information);
            }
            else
            {
                ShowActionPopup(actionTitle, $"{actionTitle} {L($"termine avec une erreur (code {result.ExitCode}).", $"completed with an error (code {result.ExitCode}).", $"termino con un error (codigo {result.ExitCode}).", $"wurde mit einem Fehler beendet (Code {result.ExitCode}).")}", MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            SetActionOutput(actionTitle, $"{actionDescription}\r\n\r\n{L("Erreur", "Error", "Error", "Fehler")}: {ex.Message}");
            AddLog($"{actionTitle}: {L("echec", "failed", "fallo", "fehlgeschlagen")} ({ex.Message}).");
            ShowActionPopup(actionTitle, $"{actionTitle} {L("impossible.", "failed.", "no fue posible.", "fehlgeschlagen.")}\r\n\r\n{L("Erreur", "Error", "Error", "Fehler")} : {ex.Message}", MessageBoxIcon.Error);
        }
        finally
        {
            SetBusyState(false);
            ReadSelectedTag(forceRefresh: true);
        }
    }

    private async Task ExecuteAmiiboWriteAsync(string binPath, bool dryRun)
    {
        if (_isRunningTool)
        {
            return;
        }

        try
        {
            SetBusyState(true);
            _lastSnapshot = null;
            SetActionOutput(
                AmiiboWriteActionTitle(),
                $"{L("Preparation du dump amiibo depuis", "Preparing amiibo dump from", "Preparando dump amiibo desde", "Bereite Amiibo-Dump vor von")}:\r\n{binPath}\r\n\r\n{L("Mode", "Mode", "Modo", "Modus")}: {(dryRun ? L("dry run non destructif", "non-destructive dry run", "dry run no destructivo", "nicht destruktiver Dry Run") : L("complet", "full", "completo", "vollstandig"))}\r\n\r\n{L("Execution en cours...", "Running...", "Ejecucion en curso...", "Ausfuhrung lauft...")}");
            AddLog($"{AmiiboWriteActionTitle()}: {L("preparation de", "preparing", "preparacion de", "Vorbereitung von")} {Path.GetFileName(binPath)} ({(dryRun ? L("dry run non destructif", "non-destructive dry run", "dry run no destructivo", "nicht destruktiver Dry Run") : L("complet", "full", "completo", "vollstandig"))}).");

            var result = await Task.Run(() => PerformAmiiboWrite(binPath, dryRun));
            SetActionOutput(AmiiboWriteActionTitle(), result.Details);
            AddLog($"{AmiiboWriteActionTitle()}: {result.Status}.");
            if (dryRun)
            {
                ShowActionPopup(AmiiboWriteActionTitle(), L("Le dry run non destructif est terminé avec succès.", "The non-destructive dry run completed successfully.", "El dry run no destructivo termino con exito.", "Der nicht destruktive Dry Run wurde erfolgreich abgeschlossen."), MessageBoxIcon.Information);
            }
            else
            {
                ShowActionPopup(AmiiboWriteActionTitle(), L("L'écriture amiibo est terminée avec succès.", "Amiibo writing completed successfully.", "La escritura amiibo termino con exito.", "Das Amiibo-Schreiben wurde erfolgreich abgeschlossen."), MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            SetActionOutput(AmiiboWriteActionTitle(), $"{AmiiboWriteActionTitle()} {L("impossible.", "failed.", "no fue posible.", "fehlgeschlagen.")}\r\n\r\n{L("Erreur", "Error", "Error", "Fehler")}: {ex.Message}");
            AddLog($"{AmiiboWriteActionTitle()}: {L("echec", "failed", "fallo", "fehlgeschlagen")} ({ex.Message}).");
            ShowActionPopup(AmiiboWriteActionTitle(), $"{AmiiboWriteActionTitle()} {L("impossible.", "failed.", "no fue posible.", "fehlgeschlagen.")}\r\n\r\n{L("Erreur", "Error", "Error", "Fehler")} : {ex.Message}", MessageBoxIcon.Error);
        }
        finally
        {
            SetBusyState(false);
            ReadSelectedTag(forceRefresh: true);
        }
    }

    private void ShowActionPopup(string title, string message, MessageBoxIcon icon)
    {
        MessageBox.Show(
            this,
            message,
            title,
            MessageBoxButtons.OK,
            icon);
    }

    private void LoadAmiiboKeys(string? folderPath)
    {
        _amiiboKeys = null;
        _keyFolderPath = string.IsNullOrWhiteSpace(folderPath) ? null : folderPath.Trim();

        if (string.IsNullOrWhiteSpace(_keyFolderPath))
        {
            UpdateAmiiboKeyStatus(L("Aucun dossier configure", "No folder configured", "Ninguna carpeta configurada", "Kein Ordner konfiguriert"), Color.DarkGoldenrod);
            return;
        }

        try
        {
            _amiiboKeys = AmiiboDumpPreparer.LoadKeysFromFolder(_keyFolderPath);
            UpdateAmiiboKeyStatus($"{L("Chargees via", "Loaded via", "Cargadas mediante", "Geladen uber")} {_amiiboKeys.SourceDescription}", Color.DarkGreen);
        }
        catch (Exception ex)
        {
            UpdateAmiiboKeyStatus($"{L("Erreur", "Error", "Error", "Fehler")}: {ex.Message}", Color.Firebrick);
            AddLog($"{L("Cles amiibo", "Amiibo keys", "Claves amiibo", "Amiibo-Schlussel")}: {ex.Message}");
        }
    }

    private void EnsureAmiiboKeysLoaded()
    {
        if (_amiiboKeys is not null)
        {
            return;
        }

        throw new InvalidOperationException(
            L(
                "Les clés amiibo ne sont pas chargées. Utilise le menu Paramètres pour choisir un dossier contenant key_retail.bin ou unfixed-info.bin + locked-secret.bin.",
                "Amiibo keys are not loaded. Use the Settings menu to choose a folder containing key_retail.bin or unfixed-info.bin + locked-secret.bin.",
                "Las claves amiibo no estan cargadas. Usa el menu Configuracion para elegir una carpeta que contenga key_retail.bin o unfixed-info.bin + locked-secret.bin.",
                "Die Amiibo-Schlussel sind nicht geladen. Verwende das Menu Einstellungen, um einen Ordner mit key_retail.bin oder unfixed-info.bin + locked-secret.bin auszuwahlen."));
    }

    private void UpdateAmiiboKeyStatus(string status, Color color)
    {
        _amiiboKeyStatus = status;
        menuItemSettings.ForeColor = color;
        menuItemSettings.ToolTipText = status;
        UpdateSettingsMenuState();
    }

    private void UpdateSettingsMenuState()
    {
        menuItemSettings.Text = GetLocalizedSettingsMenuText(_amiiboKeys is null);
        menuItemSettings.ForeColor = _amiiboKeys is null ? Color.DarkGoldenrod : Color.DarkGreen;
    }

    private void LoadAmiiboBinLibrary(string? rootPath)
    {
        _amiiboFolderPath = string.IsNullOrWhiteSpace(rootPath) ? null : rootPath.Trim();
        ClearAmiiboLibraryView();
        try
        {
            _amiiboBinItems.Clear();
            _amiiboCards.Clear();
            _amiiboFolderGroups.Clear();
            _selectedAmiiboBinItem = null;
            _selectedAmiiboCard = null;

            if (string.IsNullOrWhiteSpace(_amiiboFolderPath))
            {
                lblAmiiboSelectedName.Text = NoAmiiboSelectedText();
                toolTipAmiibo.SetToolTip(picAmiiboImage, string.Empty);
                ReplaceAmiiboImage(null);
                txtAmiiboInfo.Text = L(
                    "Utilise le menu Paramètres pour choisir le dossier principal des .bin amiibo.",
                    "Use the Settings menu to choose the main folder for amiibo .bin files.",
                    "Usa el menu Configuracion para elegir la carpeta principal de los .bin amiibo.",
                    "Verwende das Menu Einstellungen, um den Hauptordner der amiibo-.bin-Dateien auszuwahlen.");
                return;
            }

            if (!Directory.Exists(_amiiboFolderPath))
            {
                lblAmiiboSelectedName.Text = NoAmiiboSelectedText();
                toolTipAmiibo.SetToolTip(picAmiiboImage, string.Empty);
                ReplaceAmiiboImage(null);
                txtAmiiboInfo.Text = $"{L("Dossier introuvable", "Folder not found", "Carpeta no encontrada", "Ordner nicht gefunden")} : {_amiiboFolderPath}";
                return;
            }

            var files = Directory
                .EnumerateFiles(_amiiboFolderPath, "*.bin", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}!Essential Files{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => !Path.GetFileName(path).StartsWith("key_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var item = CreateAmiiboBinItem(_amiiboFolderPath, file);
                _amiiboBinItems.Add(item);
            }

            BuildAmiiboFolderList();
            txtAmiiboInfo.Text = _amiiboBinItems.Count == 0
                ? L("Aucun fichier .bin amiibo trouvé dans ce dossier.", "No amiibo .bin file found in this folder.", "No se encontro ningun archivo .bin amiibo en esta carpeta.", "Keine amiibo-.bin-Datei in diesem Ordner gefunden.")
                : L($"{_amiiboBinItems.Count} fichier(s) .bin trouvés. Sélectionne un amiibo pour voir ses informations.", $"{_amiiboBinItems.Count} .bin file(s) found. Select an amiibo to view its information.", $"{_amiiboBinItems.Count} archivo(s) .bin encontrado(s). Selecciona un amiibo para ver su informacion.", $"{_amiiboBinItems.Count} .bin-Datei(en) gefunden. Wahle ein amiibo aus, um seine Informationen anzuzeigen.");
            lblAmiiboSelectedName.Text = NoAmiiboSelectedText();
            toolTipAmiibo.SetToolTip(picAmiiboImage, string.Empty);
            ReplaceAmiiboImage(null);

            if (_amiiboBinItems.Count > 0)
            {
                if (cbAmiiboFolders.Items.Count > 0)
                {
                    cbAmiiboFolders.SelectedIndex = 0;
                }
                SelectAmiiboCard(_amiiboBinItems[0], focusDetails: false);
            }
        }
        catch (Exception ex)
        {
            txtAmiiboInfo.Text = $"{L("Scan des bin amiibo impossible", "Amiibo .bin scan failed", "No se pudo escanear los .bin amiibo", "Scannen der amiibo-.bin-Dateien fehlgeschlagen")}: {ex.Message}";
            AddLog($"{L("Bibliothèque amiibo", "Amiibo library", "Biblioteca amiibo", "Amiibo-Bibliothek")}: {ex.Message}");
        }
    }

    private void ClearAmiiboLibraryView()
    {
        foreach (var pictureBox in pnlAmiiboLibrary.Controls.OfType<Panel>()
                     .SelectMany(panel => panel.Controls.OfType<PictureBox>()))
        {
            var image = pictureBox.Image;
            pictureBox.Image = null;
            image?.Dispose();
        }

        pnlAmiiboLibrary.Controls.Clear();
        cbAmiiboFolders.Items.Clear();
    }

    private void BuildAmiiboFolderList()
    {
        var groups = _amiiboBinItems
            .GroupBy(item => GetAmiiboFolderGroup(item.RelativePath))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            _amiiboFolderGroups[group.Key] = group.ToList();
            cbAmiiboFolders.Items.Add(group.Key);
        }
    }

    private void cbAmiiboFolders_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (cbAmiiboFolders.SelectedItem is not string folderKey ||
            !_amiiboFolderGroups.TryGetValue(folderKey, out var items))
        {
            return;
        }

        RenderAmiiboFolder(items);
    }

    private void RenderAmiiboFolder(IEnumerable<AmiiboBinItem> items)
    {
        foreach (var pictureBox in pnlAmiiboLibrary.Controls.OfType<Panel>()
                     .SelectMany(panel => panel.Controls.OfType<PictureBox>()))
        {
            var image = pictureBox.Image;
            pictureBox.Image = null;
            image?.Dispose();
        }

        pnlAmiiboLibrary.Controls.Clear();

        foreach (var item in items)
        {
            var card = CreateAmiiboCard(item);
            _amiiboCards[item] = card;
            pnlAmiiboLibrary.Controls.Add(card);
        }
    }

    private static string GetAmiiboFolderGroup(string relativePath)
    {
        var folder = Path.GetDirectoryName(relativePath)?.Replace(Path.DirectorySeparatorChar, '/');
        return string.IsNullOrWhiteSpace(folder) ? "Racine" : folder;
    }

    private Panel CreateAmiiboCard(AmiiboBinItem item)
    {
        var card = new Panel
        {
            Width = 104,
            Height = 168,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(6),
            BackColor = SystemColors.Window,
            Cursor = Cursors.Hand,
            Tag = item
        };

        var picture = new PictureBox
        {
            Width = 88,
            Height = 104,
            Location = new Point(7, 7),
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Tag = item
        };

        var label = new Label
        {
            Width = 88,
            Height = 44,
            Location = new Point(7, 117),
            TextAlign = ContentAlignment.TopCenter,
            AutoEllipsis = true,
            Text = item.DisplayName,
            Tag = item
        };

        void handleClick(object? sender, EventArgs e) => SelectAmiiboCard(item, focusDetails: true);

        card.Click += handleClick;
        picture.Click += handleClick;
        label.Click += handleClick;
        card.Controls.Add(picture);
        card.Controls.Add(label);
        var toolTip = BuildAmiiboToolTip(item, null);
        toolTipAmiibo.SetToolTip(card, toolTip);
        toolTipAmiibo.SetToolTip(picture, toolTip);
        toolTipAmiibo.SetToolTip(label, toolTip);
        _ = PopulateAmiiboCardAsync(item, picture, label);
        return card;
    }

    private async Task PopulateAmiiboCardAsync(AmiiboBinItem item, PictureBox picture, Label label)
    {
        try
        {
            var apiEntry = item.ApiEntry ?? await GetAmiiboApiEntryAsync(item);
            item.ApiEntry = apiEntry;
            if (picture.IsDisposed || label.IsDisposed)
            {
                return;
            }

            if (apiEntry is not null)
            {
                label.Text = apiEntry.Name;
                var image = await CreateAmiiboImageAsync(apiEntry.Image);
                if (picture.IsDisposed)
                {
                    image?.Dispose();
                    return;
                }

                var previous = picture.Image;
                picture.Image = image;
                previous?.Dispose();
            }

            var toolTip = BuildAmiiboToolTip(item, apiEntry);
            if (picture.Parent is Control parentControl)
            {
                toolTipAmiibo.SetToolTip(parentControl, toolTip);
            }
            toolTipAmiibo.SetToolTip(picture, toolTip);
            toolTipAmiibo.SetToolTip(label, toolTip);
        }
        catch
        {
            // Keep local placeholders if metadata loading fails.
        }
    }

    private void SelectAmiiboCard(AmiiboBinItem item, bool focusDetails)
    {
        if (_selectedAmiiboCard is not null)
        {
            _selectedAmiiboCard.BackColor = SystemColors.Window;
        }

        _selectedAmiiboBinItem = item;
        if (_amiiboCards.TryGetValue(item, out var card))
        {
            _selectedAmiiboCard = card;
            card.BackColor = Color.FromArgb(230, 240, 255);

            if (cbAmiiboFolders.SelectedItem is not string selectedFolder ||
                !_amiiboFolderGroups.TryGetValue(selectedFolder, out var selectedItems) ||
                !selectedItems.Contains(item))
            {
                var folderKey = GetAmiiboFolderGroup(item.RelativePath);
                cbAmiiboFolders.SelectedItem = folderKey;
            }

            if (focusDetails)
            {
                card.ScrollControlIntoView(card);
            }
        }

        _ = ShowAmiiboSelectionDetailsAsync(item);
    }

    private void ApplyUiLanguage()
    {
        switch (_uiLanguage)
        {
            case "Espagnol":
                menuItemAbout.Text = "Acerca de";
                groupBoxReader.Text = "Lector";
                lblReader.Text = "Lector NFC/PCSC";
                btnRefreshReaders.Text = "Actualizar";
                groupBoxLibrary.Text = "Biblioteca amiibo";
                btnRefreshLibrary.Text = "Actualizar";
                groupBoxTag.Text = "Información de la etiqueta";
                lblStatusTitle.Text = "Etiqueta detectada:";
                lblVerdictTitle.Text = "Información:";
                lblUidTitle.Text = "UID:";
                lblBlankTitle.Text = "Vacía:";
                lblWritableTitle.Text = "Escritura:";
                lblAmiiboInfo.Text = "Etiqueta detectada / API";
                groupBoxActions.Text = "Acciones";
                btnDump.Text = "Volcar etiqueta";
                btnResetTag.Text = "Formatear / reiniciar";
                btnWriteTest.Text = "Probar escritura";
                btnWriteAmiibo.Text = "Escribir amiibo .bin";
                chkAmiiboDryRun.Text = "Modo dry run no destructivo";
                groupBoxLog.Text = "Registro";
                break;

            case "Allemand":
                menuItemAbout.Text = "Info";
                groupBoxReader.Text = "Leser";
                lblReader.Text = "NFC/PCSC-Leser";
                btnRefreshReaders.Text = "Aktualisieren";
                groupBoxLibrary.Text = "Amiibo-Bibliothek";
                btnRefreshLibrary.Text = "Aktualisieren";
                groupBoxTag.Text = "Tag-Informationen";
                lblStatusTitle.Text = "Erkanntes Tag:";
                lblVerdictTitle.Text = "Informationen:";
                lblUidTitle.Text = "UID:";
                lblBlankTitle.Text = "Leer:";
                lblWritableTitle.Text = "Schreiben:";
                lblAmiiboInfo.Text = "Erkanntes Tag / API";
                groupBoxActions.Text = "Aktionen";
                btnDump.Text = "Tag dumpen";
                btnResetTag.Text = "Formatieren / zurücksetzen";
                btnWriteTest.Text = "Schreibtest";
                btnWriteAmiibo.Text = "Amiibo-.bin schreiben";
                chkAmiiboDryRun.Text = "Nicht destruktiver Dry Run";
                groupBoxLog.Text = "Protokoll";
                break;

            case "Anglais":
                menuItemAbout.Text = "About";
                groupBoxReader.Text = "Reader";
                lblReader.Text = "NFC/PCSC reader";
                btnRefreshReaders.Text = "Refresh";
                groupBoxLibrary.Text = "Amiibo Library";
                btnRefreshLibrary.Text = "Refresh";
                groupBoxTag.Text = "Tag Information";
                lblStatusTitle.Text = "Detected tag:";
                lblVerdictTitle.Text = "Information:";
                lblUidTitle.Text = "UID:";
                lblBlankTitle.Text = "Blank:";
                lblWritableTitle.Text = "Write:";
                lblAmiiboInfo.Text = "Detected tag / API";
                groupBoxActions.Text = "Actions";
                btnDump.Text = "Dump tag";
                btnResetTag.Text = "Format / reset";
                btnWriteTest.Text = "Write test";
                btnWriteAmiibo.Text = "Write amiibo .bin";
                chkAmiiboDryRun.Text = "Non-destructive dry run";
                groupBoxLog.Text = "Log";
                break;

            default:
                menuItemAbout.Text = "À propos";
                groupBoxReader.Text = "Lecteur";
                lblReader.Text = "Lecteur NFC/PCSC";
                btnRefreshReaders.Text = "Rafraîchir";
                groupBoxLibrary.Text = "Bibliothèque amiibo";
                btnRefreshLibrary.Text = "Rafraîchir";
                groupBoxTag.Text = "Informations Tag";
                lblStatusTitle.Text = "Tag détecté :";
                lblVerdictTitle.Text = "Informations :";
                lblUidTitle.Text = "UID :";
                lblBlankTitle.Text = "Vierge :";
                lblWritableTitle.Text = "Écriture :";
                lblAmiiboInfo.Text = "Tag détecté / API";
                groupBoxActions.Text = "Actions";
                btnDump.Text = "Dumper le tag";
                btnResetTag.Text = "Formater / remise à zéro";
                btnWriteTest.Text = "Tester l'écriture";
                btnWriteAmiibo.Text = "Écrire un amiibo .bin";
                chkAmiiboDryRun.Text = "Mode dry run non destructif";
                groupBoxLog.Text = "Journal";
                break;
        }

        UpdateSettingsMenuState();
        lblLog.Visible = false;
        txtLog.Location = new Point(16, 24);
        txtLog.Size = new Size(1100, 153);
    }

    private string GetLocalizedSettingsMenuText(bool showWarningMarker)
    {
        var baseText = _uiLanguage switch
        {
            "Anglais" => "Settings",
            "Espagnol" => "Configuración",
            "Allemand" => "Einstellungen",
            _ => "Paramètres"
        };

        return showWarningMarker ? $"{baseText} *" : baseText;
    }

    private static AmiiboBinItem CreateAmiiboBinItem(string rootPath, string filePath)
    {
        var relativePath = Path.GetRelativePath(rootPath, filePath);
        var bytes = File.ReadAllBytes(filePath);
        var fileSize = bytes.Length;

        string? head = null;
        string? tail = null;
        string? variant = null;

        if (bytes.Length >= 0x60)
        {
            head = Convert.ToHexString(bytes.AsSpan(0x54, 4));
            tail = Convert.ToHexString(bytes.AsSpan(0x58, 4));
            var parsedVariant = Convert.ToHexString(bytes.AsSpan(0x5C, 4));
            variant = parsedVariant == "00000000" ? null : parsedVariant;
        }

        return new AmiiboBinItem(filePath, relativePath, Path.GetFileNameWithoutExtension(filePath), fileSize, head, tail, variant);
    }

    private async void UpdateDetectedTagAmiiboInfoAsync(string uid, AmiiboTagIdentity? identity)
    {
        var selectionVersion = ++_amiiboSelectionVersion;
        var builder = new StringBuilder();
        lblAmiiboSelectedName.Text = TagDetectedLabelText();
        toolTipAmiibo.SetToolTip(picAmiiboImage, string.Empty);
        ReplaceAmiiboImage(null);
        builder.AppendLine($"{L("Tag lu", "Tag read", "Etiqueta leida", "Tag gelesen")} : {uid}");

        if (identity is null)
        {
            builder.AppendLine();
            builder.AppendLine(L("Amiibo : aucun identifiant exploitable détecté sur ce tag.", "Amiibo: no usable identifier detected on this tag.", "Amiibo: no se detecto ningun identificador util en esta etiqueta.", "Amiibo: Keine nutzbare Kennung auf diesem Tag erkannt."));
            txtAmiiboInfo.Text = builder.ToString().TrimEnd();
            return;
        }

        builder.AppendLine($"ID API : {identity.ApiId}");
        builder.AppendLine($"Head : {identity.Head}");
        builder.AppendLine($"Tail : {identity.Tail}");
        builder.AppendLine($"{L("Variant", "Variant", "Variante", "Variante")} : {identity.Variant ?? NoneText()}");

        var localMatch = FindMatchingAmiiboBinItem(identity.ApiId, identity.Variant);
        if (localMatch is not null)
        {
            builder.AppendLine($"{L("Bin local", "Local bin", "Bin local", "Lokale Bin")} : {localMatch.RelativePath}");
        }

        builder.AppendLine();
        builder.AppendLine($"API : {L("chargement des métadonnées...", "loading metadata...", "cargando metadatos...", "lade Metadaten...")}");
        txtAmiiboInfo.Text = builder.ToString().TrimEnd();

        try
        {
            var apiEntry = await GetAmiiboApiEntryAsync(identity.ApiId, identity.Variant);
            if (selectionVersion != _amiiboSelectionVersion)
            {
                return;
            }

            builder.Length = 0;
            builder.AppendLine($"{L("Tag lu", "Tag read", "Etiqueta leida", "Tag gelesen")} : {uid}");
            builder.AppendLine($"ID API : {identity.ApiId}");
            builder.AppendLine($"Head : {identity.Head}");
            builder.AppendLine($"Tail : {identity.Tail}");
            builder.AppendLine($"{L("Variant", "Variant", "Variante", "Variante")} : {identity.Variant ?? NoneText()}");
            if (localMatch is not null)
            {
                builder.AppendLine($"{L("Bin local", "Local bin", "Bin local", "Lokale Bin")} : {localMatch.RelativePath}");
            }
            builder.AppendLine();

            if (apiEntry is null)
            {
                lblAmiiboSelectedName.Text = localMatch?.DisplayName ?? L("Amiibo inconnu", "Unknown amiibo", "Amiibo desconocido", "Unbekanntes amiibo");
                ReplaceAmiiboImage(null);
                builder.AppendLine($"API : {L("aucun amiibo correspondant trouve.", "no matching amiibo found.", "no se encontro ningun amiibo correspondiente.", "kein passendes amiibo gefunden.")}");
            }
            else
            {
                lblAmiiboSelectedName.Text = apiEntry.Name;
                await LoadAmiiboImageAsync(apiEntry.Image, selectionVersion);
                toolTipAmiibo.SetToolTip(picAmiiboImage, BuildAmiiboToolTip(localMatch, apiEntry));
                builder.AppendLine($"{L("Nom API", "API name", "Nombre API", "API-Name")} : {apiEntry.Name}");
                builder.AppendLine($"{L("Personnage", "Character", "Personaje", "Charakter")} : {apiEntry.Character}");
                builder.AppendLine($"{L("Jeu", "Game", "Juego", "Spiel")} : {apiEntry.GameSeries}");
                builder.AppendLine($"{L("Serie amiibo", "Amiibo series", "Serie amiibo", "Amiibo-Serie")} : {apiEntry.AmiiboSeries}");
                builder.AppendLine($"Type : {apiEntry.Type}");
                builder.AppendLine($"{L("Image", "Image", "Imagen", "Bild")} : {apiEntry.Image}");
                builder.AppendLine($"{L("Sortie JP", "Release JP", "Lanzamiento JP", "Veroffentlichung JP")} : {apiEntry.Release?.Jp ?? "-"}");
                builder.AppendLine($"{L("Sortie US", "Release US", "Lanzamiento US", "Veroffentlichung US")} : {apiEntry.Release?.Na ?? "-"}");
                builder.AppendLine($"{L("Sortie EU", "Release EU", "Lanzamiento EU", "Veroffentlichung EU")} : {apiEntry.Release?.Eu ?? "-"}");
                builder.AppendLine($"{L("Sortie AU", "Release AU", "Lanzamiento AU", "Veroffentlichung AU")} : {apiEntry.Release?.Au ?? "-"}");
            }

            txtAmiiboInfo.Text = builder.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            if (selectionVersion != _amiiboSelectionVersion)
            {
                return;
            }

            lblAmiiboSelectedName.Text = localMatch?.DisplayName ?? L("Amiibo inconnu", "Unknown amiibo", "Amiibo desconocido", "Unbekanntes amiibo");
            ReplaceAmiiboImage(null);
            builder.AppendLine();
            builder.AppendLine($"API : {L("erreur lors du chargement", "load error", "error de carga", "Ladefehler")} ({ex.Message})");
            txtAmiiboInfo.Text = builder.ToString().TrimEnd();
        }
    }

    private string BuildAmiiboToolTip(AmiiboBinItem? item, AmiiboApiEntry? apiEntry)
    {
        var builder = new StringBuilder();
        builder.AppendLine(apiEntry?.Name ?? item?.DisplayName ?? "Amiibo");

        if (apiEntry is not null)
        {
            builder.AppendLine($"{apiEntry.GameSeries} / {apiEntry.AmiiboSeries}");
            builder.AppendLine($"{L("Personnage", "Character", "Personaje", "Charakter")} : {apiEntry.Character}");
            builder.AppendLine($"Type : {apiEntry.Type}");
        }

        if (!string.IsNullOrWhiteSpace(item?.RelativePath))
        {
            builder.AppendLine($"{L("Fichier", "File", "Archivo", "Datei")} : {item.RelativePath}");
        }

        if (!string.IsNullOrWhiteSpace(item?.Head) && !string.IsNullOrWhiteSpace(item?.Tail))
        {
            builder.AppendLine($"ID : {item.Head}{item.Tail}");
        }

        if (!string.IsNullOrWhiteSpace(item?.Variant))
        {
            builder.AppendLine($"{L("Variant", "Variant", "Variante", "Variante")} : {item.Variant}");
        }

        return builder.ToString().TrimEnd();
    }

    private AmiiboBinItem? FindMatchingAmiiboBinItem(string apiId, string? variant)
    {
        var exactVariant = !string.IsNullOrWhiteSpace(variant)
            ? _amiiboBinItems.FirstOrDefault(item =>
                string.Equals(item.ApiId, apiId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Variant, variant, StringComparison.OrdinalIgnoreCase))
            : null;

        return exactVariant ?? _amiiboBinItems.FirstOrDefault(item =>
            string.Equals(item.ApiId, apiId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ShowAmiiboSelectionDetailsAsync(AmiiboBinItem item)
    {
        var selectionVersion = ++_amiiboSelectionVersion;
        var builder = new StringBuilder();
        lblAmiiboSelectedName.Text = item.DisplayName;
        ReplaceAmiiboImage(null);
        builder.AppendLine($"{L("Fichier", "File", "Archivo", "Datei")} : {item.RelativePath}");
        builder.AppendLine($"{L("Nom local", "Local name", "Nombre local", "Lokaler Name")} : {item.DisplayName}");
        builder.AppendLine($"{L("Taille", "Size", "Tamano", "GroBe")} : {item.FileSize} {L("octets", "bytes", "bytes", "Bytes")}");
        builder.AppendLine($"Head : {item.Head ?? UnavailableText()}");
        builder.AppendLine($"Tail : {item.Tail ?? UnavailableText()}");
        builder.AppendLine($"{L("Variant", "Variant", "Variante", "Variante")} : {item.Variant ?? NoneText()}");

        if (string.IsNullOrWhiteSpace(item.ApiId))
        {
            builder.AppendLine();
            builder.AppendLine($"API : {L("impossible de construire l'identifiant amiibo a partir du binaire.", "unable to build the amiibo identifier from the binary.", "no se pudo construir el identificador amiibo a partir del binario.", "die Amiibo-Kennung konnte nicht aus der Binardatei erstellt werden.")}");
            txtAmiiboInfo.Text = builder.ToString().TrimEnd();
            return;
        }

        builder.AppendLine();
        builder.AppendLine($"API : {L("chargement des metadonnees...", "loading metadata...", "cargando metadatos...", "lade Metadaten...")}");
        txtAmiiboInfo.Text = builder.ToString().TrimEnd();

        try
        {
            var apiEntry = await GetAmiiboApiEntryAsync(item);
            if (selectionVersion != _amiiboSelectionVersion)
            {
                return;
            }

            item.ApiEntry = apiEntry;
            RefreshAmiiboListItem(item);
            builder.Length = 0;
            builder.AppendLine($"{L("Fichier", "File", "Archivo", "Datei")} : {item.RelativePath}");
            builder.AppendLine($"{L("Nom local", "Local name", "Nombre local", "Lokaler Name")} : {item.DisplayName}");
            builder.AppendLine($"{L("Taille", "Size", "Tamano", "GroBe")} : {item.FileSize} {L("octets", "bytes", "bytes", "Bytes")}");
            builder.AppendLine($"ID API : {item.ApiId}");
            builder.AppendLine($"Head : {item.Head ?? UnavailableText()}");
            builder.AppendLine($"Tail : {item.Tail ?? UnavailableText()}");
            builder.AppendLine($"{L("Variant", "Variant", "Variante", "Variante")} : {item.Variant ?? NoneText()}");
            builder.AppendLine();

            if (apiEntry is null)
            {
                lblAmiiboSelectedName.Text = item.DisplayName;
                ReplaceAmiiboImage(null);
                builder.AppendLine($"API : {L("aucun amiibo correspondant trouve.", "no matching amiibo found.", "no se encontro ningun amiibo correspondiente.", "kein passendes amiibo gefunden.")}");
            }
            else
            {
                lblAmiiboSelectedName.Text = apiEntry.Name;
                await LoadAmiiboImageAsync(apiEntry.Image, selectionVersion);
                builder.AppendLine($"{L("Nom API", "API name", "Nombre API", "API-Name")} : {apiEntry.Name}");
                builder.AppendLine($"{L("Personnage", "Character", "Personaje", "Charakter")} : {apiEntry.Character}");
                builder.AppendLine($"{L("Jeu", "Game", "Juego", "Spiel")} : {apiEntry.GameSeries}");
                builder.AppendLine($"{L("Serie amiibo", "Amiibo series", "Serie amiibo", "Amiibo-Serie")} : {apiEntry.AmiiboSeries}");
                builder.AppendLine($"Type : {apiEntry.Type}");
                builder.AppendLine($"{L("Image", "Image", "Imagen", "Bild")} : {apiEntry.Image}");
                builder.AppendLine($"{L("Sortie JP", "Release JP", "Lanzamiento JP", "Veroffentlichung JP")} : {apiEntry.Release?.Jp ?? "-"}");
                builder.AppendLine($"{L("Sortie US", "Release US", "Lanzamiento US", "Veroffentlichung US")} : {apiEntry.Release?.Na ?? "-"}");
                builder.AppendLine($"{L("Sortie EU", "Release EU", "Lanzamiento EU", "Veroffentlichung EU")} : {apiEntry.Release?.Eu ?? "-"}");
                builder.AppendLine($"{L("Sortie AU", "Release AU", "Lanzamiento AU", "Veroffentlichung AU")} : {apiEntry.Release?.Au ?? "-"}");
            }

            txtAmiiboInfo.Text = builder.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            if (selectionVersion != _amiiboSelectionVersion)
            {
                return;
            }

            lblAmiiboSelectedName.Text = item.DisplayName;
            ReplaceAmiiboImage(null);
            builder.AppendLine();
            builder.AppendLine($"API : {L("erreur lors du chargement", "load error", "error de carga", "Ladefehler")} ({ex.Message})");
            txtAmiiboInfo.Text = builder.ToString().TrimEnd();
        }
    }

    private async Task<AmiiboApiEntry?> GetAmiiboApiEntryAsync(AmiiboBinItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ApiId))
        {
            return null;
        }

        return await GetAmiiboApiEntryAsync(item.ApiId, item.Variant);
    }

    private async Task<AmiiboApiEntry?> GetAmiiboApiEntryAsync(string apiId, string? variant)
    {
        var cacheKey = $"{apiId}|{variant ?? "-"}";
        if (_amiiboApiCache.TryGetValue(cacheKey, out var cachedEntry))
        {
            return cachedEntry;
        }

        var response = await AmiiboApiClient.GetAsync($"api/amiibo/?id={apiId}");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync();
        var matches = ParseAmiiboApiEntries(payload);
        AmiiboApiEntry? selectedEntry = null;

        if (!string.IsNullOrWhiteSpace(variant))
        {
            selectedEntry = matches.FirstOrDefault(entry =>
                string.Equals(entry.Variant, variant, StringComparison.OrdinalIgnoreCase));
        }

        selectedEntry ??= matches.FirstOrDefault(entry => string.IsNullOrWhiteSpace(entry.Variant));
        selectedEntry ??= matches.FirstOrDefault();
        _amiiboApiCache[cacheKey] = selectedEntry;
        return selectedEntry;
    }

    private static List<AmiiboApiEntry> ParseAmiiboApiEntries(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("amiibo", out var amiiboElement))
        {
            return [];
        }

        return amiiboElement.ValueKind switch
        {
            JsonValueKind.Array => JsonSerializer.Deserialize<List<AmiiboApiEntry>>(amiiboElement.GetRawText()) ?? [],
            JsonValueKind.Object => JsonSerializer.Deserialize<AmiiboApiEntry>(amiiboElement.GetRawText()) is { } single
                ? [single]
                : [],
            _ => []
        };
    }

    private async Task LoadAmiiboImageAsync(string? imageUrl, int selectionVersion)
    {
        if (selectionVersion != _amiiboSelectionVersion)
        {
            return;
        }

        ReplaceAmiiboImage(await CreateAmiiboImageAsync(imageUrl));
    }

    private static string? NormalizeApiUrl(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('`');
    }

    private async Task<Image?> CreateAmiiboImageAsync(string? imageUrl)
    {
        var normalizedUrl = NormalizeApiUrl(imageUrl);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return null;
        }

        if (!_amiiboImageCache.TryGetValue(normalizedUrl, out var imageBytes))
        {
            imageBytes = await AmiiboApiClient.GetByteArrayAsync(normalizedUrl);
            _amiiboImageCache[normalizedUrl] = imageBytes;
        }

        using var stream = new MemoryStream(imageBytes);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private void ReplaceAmiiboImage(Image? image)
    {
        var previous = picAmiiboImage.Image;
        picAmiiboImage.Image = image;
        previous?.Dispose();
    }

    private void RefreshAmiiboListItem(AmiiboBinItem item)
    {
        if (_amiiboCards.TryGetValue(item, out var card) &&
            card.Controls.OfType<Label>().FirstOrDefault() is { } label)
        {
            label.Text = item.ApiEntry?.Name ?? item.DisplayName;
        }
    }

    private AmiiboWriteResult PerformAmiiboWrite(string binPath, bool dryRun)
    {
        EnsureAmiiboKeysLoaded();
        var keySet = _amiiboKeys!;

        var readerName = cbReaders.InvokeRequired
            ? (string?)cbReaders.Invoke(() => cbReaders.SelectedItem as string)
            : cbReaders.SelectedItem as string;

        if (string.IsNullOrWhiteSpace(readerName))
        {
            throw new InvalidOperationException(L("Selectionne un lecteur ACR122 avant d'ecrire un amiibo.", "Select an ACR122 reader before writing an amiibo.", "Selecciona un lector ACR122 antes de escribir un amiibo.", "Wahle einen ACR122-Leser aus, bevor du ein amiibo schreibst."));
        }

        using var context = _contextFactory.Establish(SCardScope.System);
        var readers = context.GetReaders() ?? Array.Empty<string>();
        if (!readers.Contains(readerName))
        {
            throw new InvalidOperationException(L("Le lecteur selectionne n'est plus disponible.", "The selected reader is no longer available.", "El lector seleccionado ya no esta disponible.", "Der ausgewahlte Leser ist nicht mehr verfugbar."));
        }

        using var isoReader = new IsoReader(
            context,
            readerName,
            SCardShareMode.Shared,
            SCardProtocol.Any,
            false);

        var version = ReadGetVersion(isoReader);
        if (!IsLikelyNtag215(version, 20))
        {
            throw new InvalidOperationException(
                L($"Le tag détecté n'est pas reconnu comme NTAG215 ({DescribeTagType(version)}).", $"The detected tag is not recognized as NTAG215 ({DescribeTagType(version)}).", $"La etiqueta detectada no se reconoce como NTAG215 ({DescribeTagType(version)}).", $"Das erkannte Tag wird nicht als NTAG215 erkannt ({DescribeTagType(version)})."));
        }

        var uidBytes = ReadUidBytes(isoReader);
        if (uidBytes.Length != 7)
        {
            throw new InvalidOperationException(
                L($"UID inattendu ({uidBytes.Length} octet(s)). Un NTAG215 amiibo doit exposer 7 octets.", $"Unexpected UID ({uidBytes.Length} byte(s)). An NTAG215 amiibo must expose 7 bytes.", $"UID inesperado ({uidBytes.Length} byte(s)). Un amiibo NTAG215 debe exponer 7 bytes.", $"Unerwartete UID ({uidBytes.Length} Byte). Ein NTAG215-amiibo muss 7 Byte bereitstellen."));
        }

        var firstPages = ReadBinary(isoReader, 0, 16);
        var protectionPages = ReadBinary(isoReader, DynamicLockPage, 20);
        var staticLockBytes = firstPages.Skip(10).Take(2).ToArray();
        var dynamicLockBytes = protectionPages.Take(3).ToArray();
        var currentPage2 = firstPages.Skip(8).Take(4).ToArray();
        var auth0 = protectionPages[7];
        var access = protectionPages[8];
        var readProtected = (access & 0x80) != 0;
        var writableStatus = BuildWritableStatus(staticLockBytes, dynamicLockBytes, auth0, readProtected);
        string? authenticationSummary = null;
        if (writableStatus != YesText())
        {
            var amiiboPassword = BuildAmiiboPassword(uidBytes);
            var canTryKnownAmiiboPassword =
                auth0 <= LastUserPage &&
                !readProtected &&
                staticLockBytes.All(b => b == 0x00) &&
                dynamicLockBytes.All(b => b == 0x00);

            if (!canTryKnownAmiiboPassword)
            {
                throw new InvalidOperationException($"{L("Le tag courant n'est pas ecrivable", "The current tag is not writable", "La etiqueta actual no es escribible", "Das aktuelle Tag ist nicht beschreibbar")}: {writableStatus}.");
            }

            var rawAuth = AuthenticateWithPassword(
                isoReader,
                amiiboPassword,
                $"{L("amiibo calcule", "computed amiibo", "amiibo calculado", "berechnetes amiibo")} ({BitConverter.ToString(amiiboPassword)})");
            authenticationSummary = $"PWD_AUTH {L("amiibo courant OK", "current amiibo OK", "amiibo actual OK", "aktuelles amiibo OK")} -> {rawAuth}";
        }

        var prepared = AmiiboDumpPreparer.Prepare(binPath, uidBytes, keySet);
        var retriedPages = new List<string>();

        if (dryRun)
        {
            var dryRunDiagnostics = AnalyzeNtag215(isoReader);
            var dryRunDetails = BuildAmiiboWriteOutput(
                binPath,
                readerName,
                keySet.SourceDescription,
                uidBytes,
                prepared.AmiiboPassword,
                true,
                string.IsNullOrWhiteSpace(authenticationSummary)
                    ? L("Dry run non destructif: preparation du dump et verification du tag OK, aucune ecriture envoyee.", "Non-destructive dry run: dump preparation and tag verification OK, no write sent.", "Dry run no destructivo: preparacion del dump y verificacion de la etiqueta OK, no se envio ninguna escritura.", "Nicht destruktiver Dry Run: Dump-Vorbereitung und Tag-Prufung OK, kein Schreibvorgang gesendet.")
                    : $"{authenticationSummary}; {L("Dry run non destructif: preparation du dump OK, aucune ecriture envoyee.", "Non-destructive dry run: dump preparation OK, no write sent.", "Dry run no destructivo: preparacion del dump OK, no se envio ninguna escritura.", "Nicht destruktiver Dry Run: Dump-Vorbereitung OK, kein Schreibvorgang gesendet.")}",
                null,
                retriedPages,
                dryRunDiagnostics);

            return new AmiiboWriteResult(L("terminee en mode dry run non destructif", "completed in non-destructive dry run mode", "terminado en modo dry run no destructivo", "im nicht destruktiven Dry-Run-Modus abgeschlossen"), dryRunDetails);
        }

        for (byte page = 3; page < PasswordPage; page++)
        {
            var pageData = prepared.EncryptedPages.Skip(page * 4).Take(4).ToArray();
            var attempts = WritePageIfNeededWithVerify(isoReader, page, pageData);
            if (attempts > 1)
            {
                retriedPages.Add($"{page:X2}(x{attempts})");
            }
        }

        var passwordPageData = prepared.EncryptedPages.Skip(PasswordPage * 4).Take(4).ToArray();
        var packPageData = prepared.EncryptedPages.Skip(PackPage * 4).Take(4).ToArray();
        var securitySummary = WriteAmiiboSecurityPages(isoReader, passwordPageData, packPageData, retriedPages);
        var preLockSummary = VerifyAmiiboDumpBeforeLocking(isoReader, prepared);
        authenticationSummary = string.IsNullOrWhiteSpace(authenticationSummary)
            ? $"{securitySummary}; {preLockSummary}"
            : $"{authenticationSummary}; {securitySummary}; {preLockSummary}";

        if (!dryRun)
        {
            WritePageIfNeededWithVerify(isoReader, DynamicLockPage, AmiiboDynamicLockBytes);
            WritePageIfNeededWithVerify(isoReader, 2, BuildLockedStaticPage(currentPage2));
        }

        var sourceComparisonSummary = VerifyWrittenAmiiboMatchesSourceBin(isoReader, prepared, currentPage2);
        var diagnostics = AnalyzeNtag215(isoReader);
        var details = BuildAmiiboWriteOutput(
            binPath,
            readerName,
            keySet.SourceDescription,
            uidBytes,
            prepared.AmiiboPassword,
            dryRun,
            authenticationSummary,
            sourceComparisonSummary,
            retriedPages,
            diagnostics);

        return new AmiiboWriteResult(
            dryRun
                ? L("terminee en mode dry run", "completed in dry run mode", "terminado en modo dry run", "im Dry-Run-Modus abgeschlossen")
                : L("terminee avec succes", "completed successfully", "terminado con exito", "erfolgreich abgeschlossen"),
            details);
    }

    private string BuildAmiiboWriteOutput(
        string binPath,
        string readerName,
        string keySource,
        byte[] uidBytes,
        byte[] amiiboPassword,
        bool dryRun,
        string? authenticationSummary,
        string? sourceComparisonSummary,
        IReadOnlyList<string> retriedPages,
        TagAnalysis diagnostics)
    {
        var builder = new StringBuilder();
        builder.AppendLine(dryRun
            ? L("Ecriture amiibo terminee en mode dry run non destructif.", "Amiibo write completed in non-destructive dry run mode.", "La escritura amiibo termino en modo dry run no destructivo.", "Amiibo-Schreiben im nicht destruktiven Dry-Run-Modus abgeschlossen.")
            : L("Ecriture amiibo terminee avec succes.", "Amiibo write completed successfully.", "La escritura amiibo termino con exito.", "Amiibo-Schreiben erfolgreich abgeschlossen."));
        builder.AppendLine();
        builder.AppendLine($"{L("Lecteur", "Reader", "Lector", "Leser")}: {readerName}");
        builder.AppendLine($"{L("Fichier source", "Source file", "Archivo fuente", "Quelldatei")}: {binPath}");
        builder.AppendLine($"{L("Cles amiibo", "Amiibo keys", "Claves amiibo", "Amiibo-Schlussel")}: {keySource}");
        builder.AppendLine($"{L("Mode", "Mode", "Modo", "Modus")}: {(dryRun ? L("dry run non destructif", "non-destructive dry run", "dry run no destructivo", "nicht destruktiver Dry Run") : L("complet", "full", "completo", "vollstandig"))}");
        builder.AppendLine($"{L("UID du tag", "Tag UID", "UID de la etiqueta", "Tag-UID")}: {BitConverter.ToString(uidBytes)}");
        builder.AppendLine($"{L("Mot de passe amiibo calcule", "Computed amiibo password", "Contrasena amiibo calculada", "Berechnetes Amiibo-Passwort")}: {BitConverter.ToString(amiiboPassword)}");
        if (!string.IsNullOrWhiteSpace(authenticationSummary))
        {
            builder.AppendLine($"{L("Authentification prealable", "Preliminary authentication", "Autenticacion previa", "Vorherige Authentifizierung")}: {authenticationSummary}");
        }
        if (!string.IsNullOrWhiteSpace(sourceComparisonSummary))
        {
            builder.AppendLine(sourceComparisonSummary);
        }
        if (dryRun)
        {
            builder.AppendLine($"{L("Note", "Note", "Nota", "Hinweis")}: {L("aucune page n'a ete ecrite sur le tag et aucun verrou n'a ete pose.", "no page was written to the tag and no lock was applied.", "no se escribio ninguna pagina en la etiqueta y no se aplico ningun bloqueo.", "es wurde keine Seite auf das Tag geschrieben und keine Sperre gesetzt.")}");
        }
        builder.AppendLine($"{L("Verdict final", "Final verdict", "Veredicto final", "Endgultiges Urteil")}: {diagnostics.Verdict}");
        builder.AppendLine($"{L("Diagnostic final", "Final diagnostics", "Diagnostico final", "Enddiagnose")}: {diagnostics.Diagnostics}");

        if (retriedPages.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(L("Pages ayant necessite une nouvelle tentative:", "Pages that required another attempt:", "Paginas que requirieron otro intento:", "Seiten mit erneutem Versuch:"));
            builder.AppendLine(string.Join(", ", retriedPages));
        }

        return builder.ToString().TrimEnd();
    }

    private async Task<ExternalToolResult> RunPythonScriptAsync(string scriptPath, IReadOnlyList<string> arguments)
    {
        Exception? lastError = null;
        var commands = new[]
        {
            new ExternalCommand("python", Array.Empty<string>()),
            new ExternalCommand("py", new[] { "-3" }),
            new ExternalCommand("py", Array.Empty<string>())
        };

        foreach (var command in commands)
        {
            try
            {
                return await RunProcessAsync(command, scriptPath, arguments);
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            L($"Impossible de lancer Python pour executer {Path.GetFileName(scriptPath)}.", $"Unable to start Python to run {Path.GetFileName(scriptPath)}.", $"No se pudo iniciar Python para ejecutar {Path.GetFileName(scriptPath)}.", $"Python konnte nicht gestartet werden, um {Path.GetFileName(scriptPath)} auszufuhren."),
            lastError);
    }

    private static async Task<ExternalToolResult> RunProcessAsync(
        ExternalCommand command,
        string scriptPath,
        IReadOnlyList<string> arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        process.StartInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        process.StartInfo.Environment["PYTHONUTF8"] = "1";

        foreach (var argument in command.PrefixArguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ExternalToolResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static string? FindWorkspaceFile(string fileName)
    {
        var roots = new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
            Application.StartupPath
        };

        foreach (var root in roots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var current = new DirectoryInfo(root);
            for (var depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
            {
                var candidate = Path.Combine(current.FullName, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string? FindWorkspaceDirectory(string directoryName)
    {
        var roots = new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
            Application.StartupPath
        };

        foreach (var root in roots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var current = new DirectoryInfo(root);
            for (var depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
            {
                var candidate = Path.Combine(current.FullName, directoryName);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string GetAppStatePath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Acr122NfcReader");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "app-state.json");
    }

    private static AppState LoadAppState()
    {
        try
        {
            var path = GetAppStatePath();
            if (!File.Exists(path))
            {
                return new AppState(null, null, false, "Français");
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppState>(json, AppStateJsonOptions) ?? new AppState(null, null, false, "Français");
        }
        catch
        {
            return new AppState(null, null, false, "Français");
        }
    }

    private void SaveAppState()
    {
        try
        {
            var path = GetAppStatePath();
            var state = new AppState(
                _keyFolderPath,
                _amiiboFolderPath,
                chkAmiiboDryRun.Checked,
                _uiLanguage);
            var json = JsonSerializer.Serialize(state, AppStateJsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Ignore persistence errors; the UI must keep working even if local state cannot be saved.
        }
    }

    private void SetBusyState(bool isBusy)
    {
        _isRunningTool = isBusy;
        btnRefreshReaders.Enabled = !isBusy;
        btnDump.Enabled = !isBusy;
        btnResetTag.Enabled = !isBusy;
        btnWriteTest.Enabled = !isBusy;
        btnWriteAmiibo.Enabled = !isBusy;
        chkAmiiboDryRun.Enabled = !isBusy;
        btnRefreshLibrary.Enabled = !isBusy;
        cbAmiiboFolders.Enabled = !isBusy;
        pnlAmiiboLibrary.Enabled = !isBusy;
        menuItemSettings.Enabled = !isBusy;
        menuItemAbout.Enabled = !isBusy;
        cbReaders.Enabled = !isBusy;
        Cursor = isBusy ? Cursors.WaitCursor : Cursors.Default;

        if (isBusy)
        {
            pollTimer.Stop();
        }
        else if (!pollTimer.Enabled)
        {
            pollTimer.Start();
        }
    }

    private void SetActionOutput(string title, string content)
    {
        var firstLine = content
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        AddLog($"{title}: {(string.IsNullOrWhiteSpace(firstLine) ? L("mise a jour disponible.", "update available.", "actualizacion disponible.", "Aktualisierung verfugbar.") : firstLine)}");
    }

    private string BuildToolOutput(string actionDescription, ExternalToolResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(actionDescription);
        builder.AppendLine();
        builder.AppendLine(result.ExitCode == 0
            ? L("Etat: termine avec succes.", "Status: completed successfully.", "Estado: terminado con exito.", "Status: erfolgreich abgeschlossen.")
            : L($"Etat: termine avec code {result.ExitCode}.", $"Status: completed with code {result.ExitCode}.", $"Estado: terminado con codigo {result.ExitCode}.", $"Status: mit Code {result.ExitCode} abgeschlossen."));

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            builder.AppendLine();
            builder.AppendLine(L("Sortie standard:", "Standard output:", "Salida estandar:", "Standardausgabe:"));
            builder.AppendLine(result.StandardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            builder.AppendLine();
            builder.AppendLine(L("Erreurs standard:", "Standard error:", "Errores estandar:", "Standardfehler:"));
            builder.AppendLine(result.StandardError.TrimEnd());
        }

        return builder.ToString().TrimEnd();
    }

    private static string FirstLine(string text)
    {
        using var reader = new StringReader(text);
        return reader.ReadLine() ?? text;
    }

    private static Color GetStatusColor(string status)
    {
        if (StartsWithAny(status, "Erreur", "Error", "Fehler"))
        {
            return Color.Firebrick;
        }

        if (ContainsAny(status, "En attente", "Waiting", "Esperando", "Warte"))
        {
            return Color.DarkGoldenrod;
        }

        if (ContainsAny(status, "pret", "ready", "listo", "bereit"))
        {
            return Color.SteelBlue;
        }

        if (ContainsAny(status, "Tag detecte", "Detected tag", "Etiqueta detectada", "Erkanntes Tag"))
        {
            return Color.SeaGreen;
        }

        return SystemColors.ControlText;
    }

    private static Color GetVerdictColor(string verdict)
    {
        if (ContainsAny(verdict, "authentique", "authentic", "autentico", "authentisch") &&
            ContainsAny(verdict, "inscriptible", "writable", "escribible", "beschreibbar") &&
            !ContainsAny(verdict, "protegee", "protected", "protegida", "geschutzt"))
        {
            return Color.SeaGreen;
        }

        if (ContainsAny(verdict, "a confirmer", "to confirm", "por confirmar", "bestatigen") ||
            ContainsAny(verdict, "atypique", "unusual", "atipica", "ungewohnlich") ||
            ContainsAny(verdict, "protegee", "protected", "protegida", "geschutzt") ||
            ContainsAny(verdict, "verrouille", "locked", "bloquead", "gesperrt"))
        {
            return Color.DarkOrange;
        }

        if (ContainsAny(verdict, "non inscriptible", "not writable", "no escribible", "nicht beschreibbar"))
        {
            return Color.Firebrick;
        }

        return SystemColors.ControlText;
    }

    private static Color GetBlankColor(string blankStatus)
    {
        return StartsWithAny(blankStatus, "Oui", "Yes", "Si", "Ja")
            ? Color.SteelBlue
            : SystemColors.ControlText;
    }

    private static Color GetWritableColor(string writableStatus)
    {
        if (StartsWithAny(writableStatus, "Oui", "Yes", "Si", "Ja"))
        {
            return Color.SeaGreen;
        }

        if (StartsWithAny(writableStatus, "Partiellement", "Partially", "Parcial", "Teilweise"))
        {
            return Color.DarkOrange;
        }

        if (StartsWithAny(writableStatus, "Non", "No", "Nein"))
        {
            return Color.Firebrick;
        }

        return SystemColors.ControlText;
    }

    private void UpdateDisplay(string status, string verdict, string uid, string atr, string blankStatus, string writableStatus)
    {
        lblStatusValue.Text = status;
        lblVerdictValue.Text = verdict;
        lblUidValue.Text = uid;
        lblAtrValue.Text = atr;
        lblBlankValue.Text = blankStatus;
        lblWritableValue.Text = writableStatus;
        lblStatusValue.ForeColor = GetStatusColor(status);
        lblVerdictValue.ForeColor = GetVerdictColor(verdict);
        lblBlankValue.ForeColor = GetBlankColor(blankStatus);
        lblWritableValue.ForeColor = GetWritableColor(writableStatus);

        if (!string.Equals(status, _lastStatus, StringComparison.Ordinal))
        {
            AddLog(status);
            _lastStatus = status;
        }
    }

    private void AddLog(string message)
    {
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private sealed class SettingsDialog : Form
    {
        private readonly TextBox _txtKeyFolder;
        private readonly TextBox _txtAmiiboFolder;
        private readonly ComboBox _cbLanguage;

        private static string DL(string selectedLanguage, string french, string english, string spanish, string german)
        {
            return selectedLanguage switch
            {
                "Espagnol" => spanish,
                "Allemand" => german,
                "Anglais" => english,
                _ => french
            };
        }

        public SettingsDialog(string? keyFolderPath, string? amiiboFolderPath, string keyStatus, string selectedLanguage)
        {
            Text = DL(selectedLanguage, "Paramètres", "Settings", "Configuración", "Einstellungen");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(640, 274);

            var lblKeyFolder = new Label
            {
                AutoSize = true,
                Location = new Point(16, 20),
                Text = DL(selectedLanguage, "Dossier des clés amiibo", "Amiibo keys folder", "Carpeta de claves amiibo", "Amiibo-Schlusselordner")
            };

            _txtKeyFolder = new TextBox
            {
                Location = new Point(16, 42),
                Size = new Size(500, 23),
                Text = keyFolderPath ?? string.Empty
            };

            var btnBrowseKeys = new Button
            {
                Location = new Point(526, 41),
                Size = new Size(96, 25),
                Text = DL(selectedLanguage, "Choisir...", "Browse...", "Elegir...", "Auswahlen...")
            };
            btnBrowseKeys.Click += (_, _) =>
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = DL(selectedLanguage, "Choisis le dossier contenant key_retail.bin ou unfixed-info.bin + locked-secret.bin", "Choose the folder containing key_retail.bin or unfixed-info.bin + locked-secret.bin", "Elige la carpeta que contiene key_retail.bin o unfixed-info.bin + locked-secret.bin", "Wahle den Ordner mit key_retail.bin oder unfixed-info.bin + locked-secret.bin"),
                    InitialDirectory = Directory.Exists(_txtKeyFolder.Text) ? _txtKeyFolder.Text : Environment.CurrentDirectory,
                    UseDescriptionForTitle = true
                };

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _txtKeyFolder.Text = dialog.SelectedPath;
                }
            };

            var lblKeyStatus = new Label
            {
                AutoSize = false,
                Location = new Point(16, 72),
                Size = new Size(606, 32),
                Text = $"{DL(selectedLanguage, "État actuel", "Current status", "Estado actual", "Aktueller Status")} : {keyStatus}"
            };

            var lblAmiiboFolder = new Label
            {
                AutoSize = true,
                Location = new Point(16, 114),
                Text = DL(selectedLanguage, "Dossier principal des .bin amiibo", "Main amiibo .bin folder", "Carpeta principal de .bin amiibo", "Hauptordner fur amiibo-.bin-Dateien")
            };

            _txtAmiiboFolder = new TextBox
            {
                Location = new Point(16, 136),
                Size = new Size(500, 23),
                Text = amiiboFolderPath ?? string.Empty
            };

            var btnBrowseAmiibo = new Button
            {
                Location = new Point(526, 135),
                Size = new Size(96, 25),
                Text = DL(selectedLanguage, "Choisir...", "Browse...", "Elegir...", "Auswahlen...")
            };
            btnBrowseAmiibo.Click += (_, _) =>
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = DL(selectedLanguage, "Choisis le dossier principal contenant les fichiers .bin amiibo", "Choose the main folder containing amiibo .bin files", "Elige la carpeta principal que contiene los archivos .bin amiibo", "Wahle den Hauptordner mit den amiibo-.bin-Dateien"),
                    InitialDirectory = Directory.Exists(_txtAmiiboFolder.Text) ? _txtAmiiboFolder.Text : Environment.CurrentDirectory,
                    UseDescriptionForTitle = true
                };

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _txtAmiiboFolder.Text = dialog.SelectedPath;
                }
            };

            var lblLanguage = new Label
            {
                AutoSize = true,
                Location = new Point(16, 172),
                Text = DL(selectedLanguage, "Langue de l'interface", "Interface language", "Idioma de la interfaz", "Sprache der Oberflache")
            };

            _cbLanguage = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(16, 194),
                Size = new Size(220, 23)
            };
            _cbLanguage.Items.AddRange(["Français", "Anglais", "Espagnol", "Allemand"]);
            _cbLanguage.SelectedItem = _cbLanguage.Items.Contains(selectedLanguage) ? selectedLanguage : "Français";

            var btnCancel = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(446, 228),
                Size = new Size(84, 28),
                Text = DL(selectedLanguage, "Annuler", "Cancel", "Cancelar", "Abbrechen")
            };

            var btnOk = new Button
            {
                DialogResult = DialogResult.OK,
                Location = new Point(538, 228),
                Size = new Size(84, 28),
                Text = "OK"
            };

            Controls.AddRange(
            [
                lblKeyFolder,
                _txtKeyFolder,
                btnBrowseKeys,
                lblKeyStatus,
                lblAmiiboFolder,
                _txtAmiiboFolder,
                btnBrowseAmiibo,
                lblLanguage,
                _cbLanguage,
                btnCancel,
                btnOk
            ]);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        public string KeyFolderPath => _txtKeyFolder.Text.Trim();

        public string AmiiboFolderPath => _txtAmiiboFolder.Text.Trim();

        public string SelectedLanguage => _cbLanguage.SelectedItem?.ToString() ?? "Français";
    }

    private sealed record TagSnapshot(
        string Status,
        string Verdict,
        string Uid,
        string Atr,
        string BlankStatus,
        string WritableStatus,
        bool AnalysisComplete,
        AmiiboTagIdentity? AmiiboIdentity);

    private sealed record TagAnalysis(
        string Status,
        string Verdict,
        string BlankStatus,
        string WritableStatus,
        string Diagnostics,
        AmiiboTagIdentity? AmiiboIdentity);

    private sealed record Pn532Response(
        byte? Pn532Status,
        byte[] Data,
        string RawHex,
        bool HasExpectedHeader);

    private sealed class AmiiboBinItem(
        string FullPath,
        string RelativePath,
        string DisplayName,
        int FileSize,
        string? Head,
        string? Tail,
        string? Variant)
    {
        public string FullPath { get; } = FullPath;
        public string RelativePath { get; } = RelativePath;
        public string DisplayName { get; } = DisplayName;
        public int FileSize { get; } = FileSize;
        public string? Head { get; } = Head;
        public string? Tail { get; } = Tail;
        public string? Variant { get; } = Variant;
        public AmiiboApiEntry? ApiEntry { get; set; }
        public string? ApiId => Head is not null && Tail is not null ? $"{Head}{Tail}" : null;

        private string FolderHint =>
            Path.GetDirectoryName(RelativePath)?.Replace(Path.DirectorySeparatorChar, ' ')?.Replace(Path.AltDirectorySeparatorChar, ' ')?.Trim() ?? string.Empty;

        public override string ToString()
        {
            if (ApiEntry is not null)
            {
                return string.IsNullOrWhiteSpace(FolderHint)
                    ? $"{ApiEntry.Name} - {ApiEntry.GameSeries}"
                    : $"{ApiEntry.Name} - {ApiEntry.GameSeries} [{FolderHint}]";
            }

            return string.IsNullOrWhiteSpace(FolderHint)
                ? DisplayName
                : $"{DisplayName} [{FolderHint}]";
        }
    }

    private sealed record AmiiboApiEntry(
        [property: JsonPropertyName("amiiboSeries")] string AmiiboSeries,
        [property: JsonPropertyName("character")] string Character,
        [property: JsonPropertyName("gameSeries")] string GameSeries,
        [property: JsonPropertyName("head")] string Head,
        [property: JsonPropertyName("image")] string Image,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("release")] AmiiboApiRelease? Release,
        [property: JsonPropertyName("tail")] string Tail,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("variant")] string? Variant);

    private sealed record AmiiboApiRelease(
        [property: JsonPropertyName("au")] string? Au,
        [property: JsonPropertyName("eu")] string? Eu,
        [property: JsonPropertyName("jp")] string? Jp,
        [property: JsonPropertyName("na")] string? Na);

    private sealed record AmiiboTagIdentity(string Head, string Tail, string? Variant)
    {
        public string ApiId => $"{Head}{Tail}";
    }

    private sealed record ExternalCommand(string FileName, IReadOnlyList<string> PrefixArguments);

    private sealed record ExternalToolResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record AppState(string? KeyFolder, string? AmiiboFolder, bool AmiiboDryRun, string? UiLanguage);

    private sealed record AmiiboWriteResult(string Status, string Details);
}
