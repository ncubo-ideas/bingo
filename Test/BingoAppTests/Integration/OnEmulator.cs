using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoAppTests.Integration;

public abstract class OnEmulator
{
    protected static readonly TimeSpan AWhile = TimeSpan.FromSeconds(60);
    protected static readonly TimeSpan OverTheNetwork = TimeSpan.FromSeconds(180);

    protected const string BingoName = "ADB Test";

    private const string Plan =
        "{\"name\":\"" + BingoName + "\",\"cards\":30,\"price\":2000,\"date\":\"\",\"time\":\"\","
        + "\"rounds\":[{\"games\":[{\"figure\":\"AnyLine\",\"cash\":true,\"prize\":\"50000\"}]}]}";

    protected EmulatorSettings Settings { get; private set; }

    protected Phone Organizer { get; private set; }

    public TestContext TestContext { get; set; }

    [TestInitialize]
    public void PrepareEmulator()
    {
        Settings = EmulatorSettings.Load();

        if (!Settings.IsConfigured)
        {
            Assert.Inconclusive(
                "This test needs an Android emulator. Fill in the \"Emulator\" section "
                + "(Adb, Serial, Apk) in BingoAppTests/appsettings.local.json; see appsettings.json.");
        }

        Organizer = Ready(Settings.Serial);
    }

    protected Phone Collaborator() => Another(Settings.CollaboratorSerial, "CollaboratorSerial", "the collaborator");

    protected Phone Player() => Another(Settings.PlayerSerial, "PlayerSerial", "the player");

    private Phone Another(string serial, string key, string who)
    {
        if (serial.Length == 0)
        {
            Assert.Inconclusive(
                "This test needs another emulator for " + who + ". Fill in \"Emulator:" + key
                + "\" in BingoAppTests/appsettings.local.json.");
        }

        return Ready(serial);
    }

    private Phone Ready(string serial)
    {
        Phone phone = new Phone(Settings.Adb, serial, Settings.Package);
        string state = phone.State();

        if (state != "device")
            Assert.Fail("Emulator " + serial + " is configured but does not answer adb: " + state);

        if (Settings.Apk.Length > 0)
        {
            if (!File.Exists(Settings.Apk)) Assert.Fail("The configured APK does not exist: " + Settings.Apk);
            phone.Install(Settings.Apk);
        }

        phone.SetEnvironment(Settings.Environment);
        return phone;
    }

    protected void FromScratch(Phone phone)
    {
        phone.Stop();
        phone.ClearData();

        if (!phone.RecycleWifi(TimeSpan.FromSeconds(30)))
            Note(phone.Serial + ": no network after recycling the wifi; tests that need other phones will fail");

        phone.ClearLog();
    }

    protected Cdp OpenAsOrganizer(int devToolsPort)
    {
        FromScratch(Organizer);
        Organizer.Launch();

        Organizer.TapText(AWhile, "Organizer", "Organizador");
        Note("organizer: tapped Organizer");

        Cdp pages = Organizer.DevTools(devToolsPort);
        pages.Evaluate("Inicio", "document.title");
        return pages;
    }

    protected string CreateTheBingoAndEnter(Cdp pages)
    {
        pages.Send("Inicio", "new");
        pages.Evaluate("Crear bingo", "document.title");

        pages.Send("Crear bingo", "create|" + Plan);

        string born = Organizer.WaitForLog("new bingo: " + BingoName + " [", AWhile);
        Note("organizer: " + born);

        pages.Send("Crear bingo", "open");
        pages.Evaluate("El bingo", "document.title");
        return born;
    }

    protected static string CodeOnScreen(Cdp pages, string title, TimeSpan timeout) =>
        Phone.WaitFor(() =>
        {
            string code = pages.Evaluate(title, "(document.querySelector('#codeText')||{}).textContent||''").Trim();
            return code.Length > 0 ? code : null;
        }, timeout)
        ?? throw new AssertFailedException("page \"" + title + "\" never showed a code");

    protected static long FirstNumberAfter(string line, string marker)
    {
        int at = line.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) Assert.Fail("\"" + marker + "\" is not in: " + line);

        Match number = Regex.Match(line.Substring(at + marker.Length), @"\d+");
        if (!number.Success) Assert.Fail("there is no number after \"" + marker + "\" in: " + line);
        return long.Parse(number.Value);
    }

    protected void Note(string what) => TestContext?.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " " + what);
}
