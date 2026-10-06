using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoAppTests.Integration;

public sealed class Phone
{
    private readonly string _adb;
    private readonly string _package;

    public Phone(string adb, string serial, string package)
    {
        _adb = adb;
        Serial = serial;
        _package = package;
    }

    public string Serial { get; }

    public string Adb(params string[] arguments) => Adb(TimeSpan.FromSeconds(30), arguments);

    public string Adb(TimeSpan timeout, params string[] arguments)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return RunAdb(timeout, arguments);
            }
            catch (InvalidOperationException e) when (attempt < 3 && CouldNotReachTheServer(e.Message))
            {
                Thread.Sleep(TimeSpan.FromSeconds(5));
            }
        }
    }

    private static bool CouldNotReachTheServer(string message) =>
        message.Contains("cannot connect to daemon", StringComparison.OrdinalIgnoreCase)
        || message.Contains("daemon not running", StringComparison.OrdinalIgnoreCase)
        || message.Contains("daemon still not running", StringComparison.OrdinalIgnoreCase)
        || message.Contains("could not read ok from ADB Server", StringComparison.OrdinalIgnoreCase);

    private string RunAdb(TimeSpan timeout, string[] arguments)
    {
        ProcessStartInfo start = new ProcessStartInfo(_adb)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-s");
        start.ArgumentList.Add(Serial);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("could not start " + _adb);

        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            throw new TimeoutException("adb " + string.Join(" ", arguments) + " did not finish in " + timeout.TotalSeconds + " s");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException("adb " + string.Join(" ", arguments) + " exited with " + process.ExitCode + ": " + error.Result.Trim());

        return output.Result;
    }

    public string State()
    {
        try { return Adb(TimeSpan.FromSeconds(10), "get-state").Trim(); }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException) { return e.Message; }
    }

    public void Install(string apk) => Adb(TimeSpan.FromMinutes(3), "install", "-r", "-g", apk);

    public void SetEnvironment(string variables)
    {
        if (string.IsNullOrWhiteSpace(variables)) return;
        Adb("shell", "setprop", "debug.mono.env", "'" + variables + "'");
    }

    public void ClearData() => Adb("shell", "pm", "clear", _package);

    public bool RecycleWifi(TimeSpan timeout)
    {
        Adb("shell", "svc", "wifi", "disable");
        Thread.Sleep(TimeSpan.FromSeconds(2));
        Adb("shell", "svc", "wifi", "enable");

        return WaitFor(() => Reaches("1.1.1.1") ? "yes" : null, timeout) is not null;
    }

    private bool Reaches(string host)
    {
        try
        {
            Adb(TimeSpan.FromSeconds(10), "shell", "ping", "-c", "1", "-W", "3", host);
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException)
        {
            return false;
        }
    }

    public void Stop() => Adb("shell", "am", "force-stop", _package);

    public void Launch() =>
        Adb("shell", "monkey", "-p", _package, "-c", "android.intent.category.LAUNCHER", "1");

    public void OpenLink(string link) =>
        Adb("shell", "am", "start", "-W", "-a", "android.intent.action.VIEW", "-d", "'" + link + "'", _package);

    public void Tap(int x, int y) => Adb("shell", "input", "tap", x.ToString(), y.ToString());

    public IReadOnlyList<Node> Screen()
    {
        string xml = Adb("exec-out", "uiautomator", "dump", "/dev/tty");
        List<Node> nodes = new List<Node>();

        foreach (Match node in Regex.Matches(xml, "<node [^>]*>"))
        {
            Match bounds = Regex.Match(Attribute(node.Value, "bounds"), @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
            if (!bounds.Success) continue;

            nodes.Add(new Node(
                Attribute(node.Value, "text"),
                Attribute(node.Value, "content-desc"),
                Attribute(node.Value, "clickable") == "true",
                (int.Parse(bounds.Groups[1].Value) + int.Parse(bounds.Groups[3].Value)) / 2,
                (int.Parse(bounds.Groups[2].Value) + int.Parse(bounds.Groups[4].Value)) / 2));
        }

        return nodes;
    }

    public void TapText(TimeSpan timeout, params string[] texts)
    {
        Node node = WaitFor(() => Find(texts), timeout)
            ?? throw new AssertFailedException("\"" + string.Join("\" or \"", texts)
                + "\" never showed up on " + Serial + " in " + timeout.TotalSeconds + " s");

        Tap(node.X, node.Y);
    }

    private Node Find(string[] texts)
    {
        IReadOnlyList<Node> nodes;
        try { nodes = Screen(); }
        catch (InvalidOperationException) { return null; }

        bool Matches(Node n) => texts.Any(text =>
            string.Equals(n.Text, text, StringComparison.OrdinalIgnoreCase)
            || string.Equals(n.Description, text, StringComparison.OrdinalIgnoreCase));

        return nodes.FirstOrDefault(n => Matches(n) && n.Clickable) ?? nodes.FirstOrDefault(Matches);
    }

    public void ClearLog() => Adb("logcat", "-c");

    public string Log() => Adb("logcat", "-d", "-v", "time", "-s", "DOTNET:I");

    public string WaitForLog(string fragment, TimeSpan timeout)
    {
        string line = WaitFor(() => Log()
            .Split('\n')
            .LastOrDefault(l => l.Contains(fragment, StringComparison.Ordinal)), timeout);

        return line?.Trim()
            ?? throw new AssertFailedException("\"" + fragment + "\" never showed up in the log of " + Serial
                + " in " + timeout.TotalSeconds + " s");
    }

    public int Pid()
    {
        string pid = WaitFor(() => NullIfEmpty(Adb("shell", "pidof", _package).Trim()), TimeSpan.FromSeconds(20))
            ?? throw new AssertFailedException(_package + " is not running on " + Serial);

        return int.Parse(pid.Split(' ')[0]);
    }

    public Cdp DevTools(int localPort)
    {
        Adb("forward", "tcp:" + localPort, "localabstract:webview_devtools_remote_" + Pid());
        return new Cdp(localPort);
    }

    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(2);

    public static T WaitFor<T>(Func<T> attempt, TimeSpan timeout) where T : class
    {
        Stopwatch clock = Stopwatch.StartNew();

        do
        {
            T found = attempt();
            if (found is not null) return found;
            Thread.Sleep(PollEvery);
        }
        while (clock.Elapsed < timeout);

        return null;
    }

    private static string NullIfEmpty(string text) => text.Length == 0 ? null : text;

    private static string Attribute(string node, string name)
    {
        Match value = Regex.Match(node, name + "=\"([^\"]*)\"");
        return value.Success ? System.Net.WebUtility.HtmlDecode(value.Groups[1].Value) : "";
    }

    public sealed record Node(string Text, string Description, bool Clickable, int X, int Y);
}
