using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Herdr;

public sealed record Config(string? TerminalApp)
{
    public static readonly string DefaultPath =
        Environment.GetEnvironmentVariable("HERDR_LOGI_CONFIG")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config/herdr-logi-plugin/config.json");

    // TerminalApp is the macOS application to bring to the front when a tile is
    // pressed: a name or .app path for `open -a`. Null detects the terminal that
    // runs the herdr client; an empty string disables the activation step.
    public static Config Load(string? path = null)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path ?? DefaultPath));
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("terminalApp", out var app)
                && app.ValueKind == JsonValueKind.String
                ? new Config(app.GetString())
                : new Config((string?)null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Config((string?)null);
        }
    }
}

public sealed record ProcessInfo(int Ppid, string Comm);

public class Terminal
{
    // Terminal emulators recognised when the herdr client cannot be traced to its
    // host app, in order of preference. Terminal ships with macOS, so it is last
    // and always available.
    public static readonly string[] KnownTerminals =
        ["iTerm", "Ghostty", "WezTerm", "kitty", "Warp", "Alacritty", "Rio", "Hyper", "Tabby", "Terminal"];

    private static readonly TimeSpan DetectCache = TimeSpan.FromSeconds(30);
    // Activation runs outside the press queue, so overlapping presses can detect
    // at once; the gate makes them share one `ps` run and one cache entry.
    private readonly SemaphoreSlim detectGate = new(1, 1);
    private (string? Bundle, DateTime At)? detected;

    // Parses `ps -axo pid=,ppid=,comm=` output.
    public static Dictionary<int, ProcessInfo> ParseProcessTable(string table)
    {
        var processes = new Dictionary<int, ProcessInfo>();
        foreach (var line in table.Split('\n'))
        {
            var match = Regex.Match(line, @"^\s*(\d+)\s+(\d+)\s+(.+?)\s*$");
            if (match.Success) processes[int.Parse(match.Groups[1].Value)] = new ProcessInfo(int.Parse(match.Groups[2].Value), match.Groups[3].Value);
        }
        return processes;
    }

    // Walks up from each herdr client process to the first ancestor that lives
    // inside an .app bundle and returns that bundle's path. The herdr server is not
    // inside a terminal, so it never matches. The newest client wins when several
    // terminals are attached.
    public static string? BundleFromProcessTable(IReadOnlyDictionary<int, ProcessInfo> processes)
    {
        var clients = processes
            .Where(p => p.Value.Comm == "herdr" || p.Value.Comm.EndsWith("/herdr", StringComparison.Ordinal))
            .Select(p => p.Key)
            .OrderDescending();
        foreach (var client in clients)
        {
            var visited = new HashSet<int>();
            for (var pid = client; pid > 1 && visited.Add(pid);)
            {
                if (!processes.TryGetValue(pid, out var process)) break;
                var bundle = Regex.Match(process.Comm, @"^(.*?\.app)/");
                if (bundle.Success) return bundle.Groups[1].Value;
                pid = process.Ppid;
            }
        }
        return null;
    }

    // The most preferred known terminal that has a process running, wherever its
    // bundle is installed.
    public static string? RunningBundleFromProcessTable(IReadOnlyDictionary<int, ProcessInfo> processes)
    {
        foreach (var name in KnownTerminals)
        {
            var pattern = new Regex($@"^(.*?/{Regex.Escape(name)}\.app)/");
            foreach (var process in processes.Values)
            {
                var bundle = pattern.Match(process.Comm);
                if (bundle.Success) return bundle.Groups[1].Value;
            }
        }
        return null;
    }

    // The most preferred known terminal installed in the usual places.
    public static string? InstalledBundle(Func<string, bool>? exists = null, string? home = null)
    {
        exists ??= Directory.Exists;
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] roots = ["/Applications", Path.Combine(home, "Applications"), "/System/Applications/Utilities", "/Applications/Utilities"];
        foreach (var name in KnownTerminals)
        {
            foreach (var root in roots)
            {
                var bundle = Path.Combine(root, $"{name}.app");
                if (exists(bundle)) return bundle;
            }
        }
        return null;
    }

    // The terminal to raise, in order: the app hosting the herdr client, then a
    // known terminal that is running, then a known terminal that is installed.
    public async Task<string?> DetectBundleAsync()
    {
        await detectGate.WaitAsync();
        try
        {
            if (detected is { } cached && DateTime.UtcNow - cached.At < DetectCache) return cached.Bundle;
            string? bundle;
            try
            {
                var processes = ParseProcessTable(await RunAsync("/bin/ps", "-axo", "pid=,ppid=,comm="));
                bundle = BundleFromProcessTable(processes) ?? RunningBundleFromProcessTable(processes);
            }
            catch (Exception)
            {
                bundle = null;
            }
            bundle ??= InstalledBundle();
            detected = (bundle, DateTime.UtcNow);
            return bundle;
        }
        finally
        {
            detectGate.Release();
        }
    }

    // Never throws: a terminal that cannot be raised must not stop the focus
    // request that follows it.
    public async Task ActivateAsync()
    {
        try
        {
            var terminalApp = Config.Load().TerminalApp;
            if (terminalApp == "") return;
            var target = terminalApp ?? await DetectBundleAsync();
            if (target is null) return;
            await RunAsync("/usr/bin/open", "-a", target);
        }
        catch (Exception e)
        {
            Log.Warning($"could not raise the terminal: {e.Message}");
        }
    }

    protected virtual async Task<string> RunAsync(string command, params string[] args)
    {
        var start = new ProcessStartInfo(command) { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{command} did not start");
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{command} exited with {process.ExitCode}");
        return output;
    }
}
