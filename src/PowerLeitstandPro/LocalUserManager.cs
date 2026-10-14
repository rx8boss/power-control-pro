using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PowerControlProWpf;

internal static class LocalUserManager
{
    private const uint NormalAccount = 0x0200;
    private const uint LogonScript = 0x0001;
    private const uint Disabled = 0x0002;
    private const uint PasswordNotRequired = 0x0020;

    internal static void Create(string name, string password, string fullName, string description, bool withoutPassword, bool enabled, bool administrator)
    {
        ValidateName(name);
        if (!withoutPassword && string.IsNullOrEmpty(password)) throw new ArgumentException("Ein Kennwort ist erforderlich.");
        if (withoutPassword && !string.IsNullOrEmpty(password)) throw new ArgumentException("Ein kennwortloses Konto darf kein Kennwort enthalten.");
        var info = new UserInfo1 { Name = name, Password = password, Privilege = 1, Flags = AccountFlags(enabled, withoutPassword) };
        var result = NetUserAdd(null, 1, ref info, out _);
        info.Password = string.Empty;
        Check(result);
        // A partial creation is reported; an account is never deleted silently on a later failure.
        SetProfile(name, fullName, description);
        if (administrator) SetAdministrator(name, true);
    }

    internal static void Edit(string name, string newName, string expectedSid, string fullName, string description, bool enabled, bool administrator, bool wasAdministrator)
    {
        ValidateName(newName);
        VerifyIdentity(name, expectedSid, !name.Equals(newName, StringComparison.Ordinal));
        if (!name.Equals(newName, StringComparison.OrdinalIgnoreCase))
        {
            var lookup = NetUserGetInfo(null, newName, 0, out var existing);
            if (lookup == 0)
            {
                NetApiBufferFree(existing);
                throw new ArgumentException("Der neue Benutzername ist bereits vergeben.");
            }
            if (lookup != 2221) Check(lookup); // NERR_UserNotFound
        }
        SetProfile(name, fullName, description);
        if (administrator != wasAdministrator) SetAdministrator(name, administrator);
        var result = NetUserGetInfo(null, name, 1, out var pointer);
        Check(result);
        try
        {
            var flags = Marshal.PtrToStructure<UserInfo1>(pointer).Flags;
            flags = (enabled ? flags & ~Disabled : flags | Disabled) | LogonScript;
            var updated = new UserInfo1008 { Flags = flags };
            Check(NetUserSetFlags(null, name, 1008, ref updated, out _));
        }
        finally { NetApiBufferFree(pointer); }
        if (!name.Equals(newName, StringComparison.Ordinal))
        {
            var renamed = new UserInfo0 { Name = newName };
            Check(NetUserRename(null, name, 0, ref renamed, out _));
        }
    }

    internal static void Delete(string name, string expectedSid)
    {
        VerifyIdentity(name, expectedSid, true);
        Check(NetUserDel(null, name));
    }

    internal static void ChangePassword(string name, string expectedSid, string password, bool removePassword)
    {
        VerifyIdentity(name, expectedSid, false);
        if (removePassword && password.Length != 0) throw new ArgumentException("Beim Entfernen darf kein neues Kennwort angegeben werden.");
        if (!removePassword && password.Length == 0) throw new ArgumentException("Ein neues Kennwort ist erforderlich.");

        Check(NetUserGetInfo(null, name, 1, out var pointer));
        uint originalFlags;
        try { originalFlags = Marshal.PtrToStructure<UserInfo1>(pointer).Flags; }
        finally { NetApiBufferFree(pointer); }
        var updatedFlags = PasswordFlags(originalFlags, removePassword);
        var changedFlagsBeforePassword = removePassword && updatedFlags != originalFlags;
        if (changedFlagsBeforePassword) SetFlags(name, updatedFlags);
        try
        {
            var info = new UserInfo1003 { Password = password };
            try { Check(NetUserSetPassword(null, name, 1003, ref info, out _)); }
            finally { info.Password = string.Empty; }
        }
        catch
        {
            if (changedFlagsBeforePassword)
            {
                try { SetFlags(name, originalFlags); }
                catch (Exception rollbackError)
                {
                    throw new InvalidOperationException("Kennwortänderung fehlgeschlagen; auch die ursprünglichen Kontoflags konnten nicht wiederhergestellt werden.", rollbackError);
                }
            }
            throw;
        }
        if (!removePassword && updatedFlags != originalFlags) SetFlags(name, updatedFlags);
    }

    private static void ValidateName(string name)
    {
        if (!IsValidName(name)) throw new ArgumentException("Ungültiger Benutzername.");
    }

    internal static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name == name.Trim() && name.Length <= 20 && !name.EndsWith('.') &&
        !name.Any(c => c < 32 || "\"/\\[]:;|=,+*?<>@".Contains(c));

    internal static uint AccountFlags(bool enabled, bool withoutPassword) =>
        NormalAccount | LogonScript | (enabled ? 0u : Disabled) | (withoutPassword ? PasswordNotRequired : 0u);

    internal static uint PasswordFlags(uint originalFlags, bool removePassword) =>
        removePassword ? originalFlags | PasswordNotRequired : originalFlags & ~PasswordNotRequired;

    private static void SetFlags(string name, uint flags)
    {
        var updated = new UserInfo1008 { Flags = flags };
        Check(NetUserSetFlags(null, name, 1008, ref updated, out _));
    }

    private static void VerifyIdentity(string name, string expectedSid, bool blockCurrent)
    {
        var sid = (SecurityIdentifier)new NTAccount(Environment.MachineName, name).Translate(typeof(SecurityIdentifier));
        if (string.IsNullOrWhiteSpace(expectedSid) || !sid.Value.Equals(expectedSid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Das ausgewählte Konto hat sich seit dem Laden geändert.");
        if (sid.Value.EndsWith("-500", StringComparison.Ordinal) || sid.Value.EndsWith("-501", StringComparison.Ordinal) ||
            sid.Value.EndsWith("-503", StringComparison.Ordinal) || sid.Value.EndsWith("-504", StringComparison.Ordinal))
            throw new InvalidOperationException("Dieses integrierte Windows-Konto ist geschützt.");
        if (blockCurrent && sid.Value.Equals(WindowsIdentity.GetCurrent().User?.Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Das aktuell verwendete Konto kann hier nicht umbenannt oder gelöscht werden.");
    }

    private static void SetProfile(string name, string fullName, string description)
    {
        var full = new UserInfo1011 { FullName = fullName };
        Check(NetUserSetFullName(null, name, 1011, ref full, out _));
        var comment = new UserInfo1007 { Comment = description };
        Check(NetUserSetComment(null, name, 1007, ref comment, out _));
    }

    private static void SetAdministrator(string name, bool administrator)
    {
        var group = new SecurityIdentifier("S-1-5-32-544").Translate(typeof(NTAccount)).Value.Split('\\').Last();
        var member = new LocalGroupMember3 { Name = Environment.MachineName + "\\" + name };
        var result = administrator
            ? NetLocalGroupAddMembers(null, group, 3, ref member, 1)
            : NetLocalGroupDelMembers(null, group, 3, ref member, 1);
        Check(result);
    }

    private static void Check(int result)
    {
        if (result != 0) throw new Win32Exception(result);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct UserInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string Password;
        public uint PasswordAge;
        public uint Privilege;
        [MarshalAs(UnmanagedType.LPWStr)] public string? HomeDirectory;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ScriptPath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct UserInfo1007 { [MarshalAs(UnmanagedType.LPWStr)] public string Comment; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct UserInfo1003 { [MarshalAs(UnmanagedType.LPWStr)] public string Password; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct UserInfo0 { [MarshalAs(UnmanagedType.LPWStr)] public string Name; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct UserInfo1011 { [MarshalAs(UnmanagedType.LPWStr)] public string FullName; }
    [StructLayout(LayoutKind.Sequential)] private struct UserInfo1008 { public uint Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct LocalGroupMember3 { [MarshalAs(UnmanagedType.LPWStr)] public string Name; }
    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)] private static extern int NetUserAdd(string? server, int level, ref UserInfo1 info, out int parameterError);
    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)] private static extern int NetUserDel(string? server, string name);
    [DllImport("Netapi32.dll", EntryPoint = "NetUserSetInfo", CharSet = CharSet.Unicode)] private static extern int NetUserRename(string? server, string name, int level, ref UserInfo0 info, out int parameterError);
    [DllImport("Netapi32.dll", EntryPoint = "NetUserSetInfo", CharSet = CharSet.Unicode)] private static extern int NetUserSetFullName(string? server, string name, int level, ref UserInfo1011 info, out int parameterError);
    [DllImport("Netapi32.dll", EntryPoint = "NetUserSetInfo", CharSet = CharSet.Unicode)] private static extern int NetUserSetComment(string? server, string name, int level, ref UserInfo1007 info, out int parameterError);
    [DllImport("Netapi32.dll", EntryPoint = "NetUserSetInfo", CharSet = CharSet.Unicode)] private static extern int NetUserSetFlags(string? server, string name, int level, ref UserInfo1008 info, out int parameterError);
    [DllImport("Netapi32.dll", EntryPoint = "NetUserSetInfo", CharSet = CharSet.Unicode)] private static extern int NetUserSetPassword(string? server, string name, int level, ref UserInfo1003 info, out int parameterError);
    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)] private static extern int NetUserGetInfo(string? server, string name, int level, out IntPtr buffer);
    [DllImport("Netapi32.dll")] private static extern int NetApiBufferFree(IntPtr buffer);
    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)] private static extern int NetLocalGroupAddMembers(string? server, string group, int level, ref LocalGroupMember3 members, int count);
    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)] private static extern int NetLocalGroupDelMembers(string? server, string group, int level, ref LocalGroupMember3 members, int count);
}
