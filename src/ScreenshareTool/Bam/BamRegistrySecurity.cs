using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;








internal static class BamRegistrySecurity
{
    private const string BamRoot = @"SYSTEM\CurrentControlSet\Services\bam";

    
    public static List<DeniedRegistryEntry> ScanDeniedPermissions()
    {
        var results = new List<DeniedRegistryEntry>();
        using RegistryKey? root = Registry.LocalMachine.OpenSubKey(
            BamRoot, RegistryKeyPermissionCheck.ReadSubTree,
            RegistryRights.ReadKey | RegistryRights.ReadPermissions);
        if (root is null)
            return results;

        Traverse(root, results, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return results;
    }

    private static void Traverse(
        RegistryKey key,
        List<DeniedRegistryEntry> results,
        HashSet<string> visited)
    {
        SecurityIdentifier? current = WindowsIdentity.GetCurrent().User;
        var currentGroups = CurrentGroupSids();
        try
        {
            var sec = key.GetAccessControl();
            foreach (AuthorizationRule rule in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule is not RegistryAccessRule ra || ra.AccessControlType != AccessControlType.Deny)
                    continue;

                
                
                if (!AffectsUs(ra.IdentityReference, current, currentGroups))
                    continue;

                results.Add(new DeniedRegistryEntry
                {
                    KeyPath = key.Name,
                    Permission = FormatMask(ra.RegistryRights)
                });
            }
        }
        catch
        {
            
        }

        string[] subKeys;
        try
        {
            subKeys = key.GetSubKeyNames();
        }
        catch
        {
            subKeys = Array.Empty<string>();
        }

        foreach (string sub in subKeys)
        {
            if (string.IsNullOrEmpty(sub))
                continue;
            
            
            if (!visited.Add(key.Name + "\\" + sub))
                continue;

            RegistryKey? child = null;
            try
            {
                
                child = key.OpenSubKey(sub, RegistryKeyPermissionCheck.ReadSubTree,
                    RegistryRights.ReadKey | RegistryRights.ReadPermissions);
            }
            catch
            {
            }

            if (child is null)
                continue;
            try
            {
                Traverse(child, results, visited);
            }
            finally
            {
                child.Dispose();
            }
        }
    }

    private static SecurityIdentifier[] CurrentGroupSids()
    {
        try
        {
            var groups = WindowsIdentity.GetCurrent().Groups;
            if (groups is null)
                return Array.Empty<SecurityIdentifier>();
            return groups.OfType<SecurityIdentifier>().ToArray();
        }
        catch
        {
            return Array.Empty<SecurityIdentifier>();
        }
    }

    
    
    
    
    
    private static bool AffectsUs(
        IdentityReference identity,
        SecurityIdentifier? currentUser,
        SecurityIdentifier[] currentGroups)
    {
        if (identity is not SecurityIdentifier sid)
            return false;

        if (sid == new SecurityIdentifier(WellKnownSidType.WorldSid, null))
            return true;
        if (sid == new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null))
            return true;
        if (sid == new SecurityIdentifier(WellKnownSidType.InteractiveSid, null))
            return true;
        if (currentUser is not null && sid == currentUser)
            return true;
        foreach (var group in currentGroups)
        {
            if (sid == group)
                return true;
        }
        return false;
    }

    
    internal static string FormatMask(RegistryRights rights)
    {
        const RegistryRights full =
            RegistryRights.QueryValues | RegistryRights.SetValue |
            RegistryRights.CreateSubKey | RegistryRights.EnumerateSubKeys |
            RegistryRights.Notify | RegistryRights.CreateLink | RegistryRights.Delete;
        if ((uint)(rights & full) == (uint)full)
            return "FullControl";
        if (rights.HasFlag(RegistryRights.Delete))
            return "Delete";
        if (rights.HasFlag(RegistryRights.SetValue) &&
            rights.HasFlag(RegistryRights.CreateSubKey))
            return "Write";
        if (rights.HasFlag(RegistryRights.QueryValues) &&
            rights.HasFlag(RegistryRights.EnumerateSubKeys) &&
            rights.HasFlag(RegistryRights.Notify))
            return "Read";
        if (rights.HasFlag(RegistryRights.CreateSubKey))
            return "CreateSubKey";
        if (rights.HasFlag(RegistryRights.SetValue))
            return "SetValue";

        return $"0x{(uint)rights:X8}";
    }
}
