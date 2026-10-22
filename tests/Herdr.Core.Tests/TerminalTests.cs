namespace Herdr.Tests;

public class TerminalTests
{
    private static Dictionary<int, ProcessInfo> Table(params (int Pid, int Ppid, string Comm)[] rows) =>
        Terminal.ParseProcessTable(string.Join("\n", rows.Select(r => $"{r.Pid} {r.Ppid} {r.Comm}")) + "\n");

    [Fact]
    public void ConfigDefaultsToAutoDetectionAndAcceptsATerminalAppOverride()
    {
        var dir = Directory.CreateTempSubdirectory("herdr-logi-").FullName;
        try
        {
            Assert.Null(Config.Load(Path.Combine(dir, "missing.json")).TerminalApp);
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, """{"terminalApp":"Ghostty"}""");
            Assert.Equal("Ghostty", Config.Load(path).TerminalApp);
            File.WriteAllText(path, """{"terminalApp":""}""");
            Assert.Equal("", Config.Load(path).TerminalApp); // empty string disables activation
            File.WriteAllText(path, """{"terminalApp":42}""");
            Assert.Null(Config.Load(path).TerminalApp); // non-string value is ignored
            File.WriteAllText(path, "{not json");
            Assert.Null(Config.Load(path).TerminalApp); // malformed file is ignored
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FindsTheTerminalBundleAboveTheHerdrClientIgnoringTheServer()
    {
        var iterm = Table(
            (1, 0, "/sbin/launchd"),
            (17297, 1, "/Users/n/.local/bin/herdr"),
            (25172, 1, "/Applications/iTerm.app/Contents/MacOS/iTerm2"),
            (25174, 25172, "/Users/n/Library/Application Support/iTerm2/iTermServer-3.6.9"),
            (25490, 25174, "/usr/bin/login"),
            (25502, 25490, "-zsh"),
            (87506, 25502, "herdr"));
        Assert.Equal("/Applications/iTerm.app", Terminal.BundleFromProcessTable(iterm));

        var ghostty = Table(
            (17297, 1, "/Users/n/.local/bin/herdr"),
            (900, 1, "/Applications/Ghostty.app/Contents/MacOS/ghostty"),
            (901, 900, "/usr/bin/login"),
            (902, 901, "-zsh"),
            (903, 902, "/opt/homebrew/bin/herdr"));
        Assert.Equal("/Applications/Ghostty.app", Terminal.BundleFromProcessTable(ghostty));
    }

    [Fact]
    public void TheNewestClientWinsAndAnXpcHelperResolvesToItsOuterBundle()
    {
        var two = Table(
            (10, 1, "/Applications/Warp.app/Contents/MacOS/stable"),
            (11, 10, "-zsh"),
            (12, 11, "herdr"),
            (20, 1, "/Applications/kitty.app/Contents/MacOS/kitty"),
            (21, 20, "-zsh"),
            (22, 21, "herdr"));
        Assert.Equal("/Applications/kitty.app", Terminal.BundleFromProcessTable(two));

        var xpc = Table(
            (30, 1, "/Applications/iTerm.app/Contents/XPCServices/pidinfo.xpc/Contents/MacOS/pidinfo"),
            (31, 30, "herdr"));
        Assert.Equal("/Applications/iTerm.app", Terminal.BundleFromProcessTable(xpc));
    }

    [Fact]
    public void NoTerminalIsFoundWhenOnlyTheServerRunsOrTheChainIsBroken()
    {
        Assert.Null(Terminal.BundleFromProcessTable(Table((17297, 1, "/Users/n/.local/bin/herdr"))));
        Assert.Null(Terminal.BundleFromProcessTable(Table((5, 4, "herdr")))); // missing parent
        Assert.Null(Terminal.BundleFromProcessTable(Terminal.ParseProcessTable("")));
    }

    [Fact]
    public void FallsBackToAKnownTerminalThatIsRunningByPreference()
    {
        var running = Table(
            (1, 0, "/sbin/launchd"),
            (40, 1, "/System/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal"),
            (41, 1, "/Users/n/Applications/WezTerm.app/Contents/MacOS/wezterm-gui"),
            (42, 1, "/Applications/Slack.app/Contents/MacOS/Slack"));
        Assert.Equal("/Users/n/Applications/WezTerm.app", Terminal.RunningBundleFromProcessTable(running));
        Assert.Null(Terminal.RunningBundleFromProcessTable(Table((42, 1, "/Applications/Slack.app/Contents/MacOS/Slack"))));
        Assert.Null(Terminal.RunningBundleFromProcessTable(Terminal.ParseProcessTable("")));
    }

    [Fact]
    public void FallsBackToAnInstalledKnownTerminalEndingWithTerminalApp()
    {
        Func<string, bool> Installed(params string[] paths) => paths.Contains;
        const string home = "/Users/n";
        Assert.Equal("/Applications/Ghostty.app", Terminal.InstalledBundle(Installed("/Applications/Ghostty.app", "/Applications/Alacritty.app"), home));
        Assert.Equal("/Users/n/Applications/kitty.app", Terminal.InstalledBundle(Installed("/Users/n/Applications/kitty.app"), home));
        Assert.Equal(
            "/System/Applications/Utilities/Terminal.app",
            Terminal.InstalledBundle(Installed("/System/Applications/Utilities/Terminal.app"), home));
        Assert.Null(Terminal.InstalledBundle(_ => false, home));
    }
}
