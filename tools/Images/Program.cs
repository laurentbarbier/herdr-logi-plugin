// Draws the plugin's pictures with the plugin's own ring renderer.
//
//   dotnet run --project tools/Images -- icon          package/metadata/Icon256x256.png
//   dotnet run --project tools/Images -- deck          the README key images, demo session
//   dotnet run --project tools/Images -- deck --live   the herdr session running right now

using Herdr.Images;

var root = FindRoot();
switch (args.FirstOrDefault())
{
    case "icon":
        Icon.Write(root);
        return 0;
    case "deck":
        return await Deck.WriteAsync(root, live: args.Contains("--live"));
    default:
        Console.Error.WriteLine("usage: dotnet run --project tools/Images -- icon | deck [--live]");
        return 2;
}

// The repository root, wherever the tool is run from inside it.
static string FindRoot()
{
    for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Herdr.slnx"))) return dir.FullName;
    }
    throw new InvalidOperationException("run this from inside the herdr-logi-plugin repository");
}
