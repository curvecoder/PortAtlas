using System.Security.AccessControl;
using System.Security.Principal;

namespace PortAtlas.Tests.Resolution;

// Refuses the creation of files in a folder to the test account, as a folder the audit account cannot write to;
// the rule is removed on disposal so that the folder can be deleted.
internal sealed class FolderCreationDenial : IDisposable
{
    private readonly DirectoryInfo _folder;
    private readonly DirectorySecurity _security;
    private readonly FileSystemAccessRule _rule;

    private FolderCreationDenial(DirectoryInfo folder, DirectorySecurity security, FileSystemAccessRule rule)
    {
        _folder = folder;
        _security = security;
        _rule = rule;
    }

    public static FolderCreationDenial Apply(DirectoryInfo folder)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var rule = new FileSystemAccessRule(
            identity.User ?? throw new InvalidOperationException("The test account has no security identifier."),
            FileSystemRights.CreateFiles,
            AccessControlType.Deny);
        var security = folder.GetAccessControl();
        security.AddAccessRule(rule);
        folder.SetAccessControl(security);
        return new FolderCreationDenial(folder, security, rule);
    }

    public void Dispose()
    {
        _security.RemoveAccessRule(_rule);
        _folder.SetAccessControl(_security);
    }
}
