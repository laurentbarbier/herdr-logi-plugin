namespace Loupedeck.HerdrPlugin;

// The Plugin Service will not load a plugin without a ClientApplication, even a
// universal one like this that is not tied to any app.
public class HerdrApplication : ClientApplication
{
    protected override string GetProcessName() => "";

    protected override string GetBundleName() => "";

    public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
}
