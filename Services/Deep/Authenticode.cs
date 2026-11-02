using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Guardian.Services.Deep;

/// <summary>
/// Valid: verifies. Unsigned: no signature at all. Tampered: a signature exists but the file no longer matches it.
/// Untrusted: a signature exists and the file matches it, but the certificate isn't trusted (expired, unknown or self-made publisher).
/// </summary>
public enum SignatureStatus { Valid, Unsigned, Tampered, Untrusted, NotApplicable }

public sealed record SignatureInfo(SignatureStatus Status, string Signer)
{
    public bool IsValid => Status == SignatureStatus.Valid;
}

/// <summary>
/// Authenticode verification through WinVerifyTrust, with a catalog fallback for Windows files
/// (most system binaries and drivers are signed through catalogs, not embedded signatures).
/// Revocation checks are disabled so scanning never waits on the network.
/// </summary>
public static class Authenticode
{
    static readonly ConcurrentDictionary<string, SignatureInfo> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static SignatureInfo Check(string path)
    {
        if (string.IsNullOrEmpty(path)) return new SignatureInfo(SignatureStatus.NotApplicable, "");
        return Cache.GetOrAdd(path, Verify);
    }

    /// <summary>Publishers whose valid signature makes a file unremarkable.</summary>
    public static bool IsWellKnownPublisher(string signer)
    {
        if (string.IsNullOrEmpty(signer)) return false;
        string[] known =
        {
            "Microsoft", "Windows", "Intel", "NVIDIA", "Advanced Micro Devices", "AMD", "Realtek", "Google", "Mozilla",
            "Valve", "Epic Games", "Steam", "Logitech", "Corsair", "Razer", "ASUS", "Dell", "HP Inc", "Lenovo", "Apple",
            "Adobe", "Oracle", "Brave", "Discord", "Spotify", "Git", "GitHub", "JetBrains", "Qualcomm", "Broadcom",
        };
        return known.Any(k => signer.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    // ---- WinVerifyTrust plumbing ----

    const uint WTD_UI_NONE = 2, WTD_REVOKE_NONE = 0, WTD_CHOICE_FILE = 1, WTD_CHOICE_CATALOG = 2;
    const uint WTD_STATEACTION_VERIFY = 1, WTD_STATEACTION_CLOSE = 2, WTD_REVOCATION_CHECK_NONE = 0x10;
    static Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    const uint TRUST_E_NOSIGNATURE = 0x800B0100, TRUST_E_PROVIDER_UNKNOWN = 0x800B0001, TRUST_E_SUBJECT_FORM_UNKNOWN = 0x800B0003;
    const uint TRUST_E_EXPLICIT_DISTRUST = 0x800B0111, TRUST_E_BAD_DIGEST = 0x80096010;

    [StructLayout(LayoutKind.Sequential)]
    struct WINTRUST_FILE_INFO { public uint cbStruct; public IntPtr pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }

    [StructLayout(LayoutKind.Sequential)]
    struct WINTRUST_CATALOG_INFO
    {
        public uint cbStruct; public uint dwCatalogVersion; public IntPtr pcwszCatalogFilePath; public IntPtr pcwszMemberTag;
        public IntPtr pcwszMemberFilePath; public IntPtr hMemberFile; public IntPtr pbCalculatedFileHash;
        public uint cbCalculatedFileHash; public IntPtr pcCatalogContext; public IntPtr hCatAdmin;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WINTRUST_DATA
    {
        public uint cbStruct; public IntPtr pPolicyCallbackData; public IntPtr pSIPClientData; public uint dwUIChoice;
        public uint fdwRevocationChecks; public uint dwUnionChoice; public IntPtr pInfo; public uint dwStateAction;
        public IntPtr hWVTStateData; public IntPtr pwszURLReference; public uint dwProvFlags; public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CATALOG_INFO { public uint cbStruct; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string wszCatalogFile; }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    static extern bool CryptCATAdminAcquireContext2(out IntPtr hCatAdmin, IntPtr pgSubsystem,
        [MarshalAs(UnmanagedType.LPWStr)] string hashAlgorithm, IntPtr strongHashPolicy, uint flags);

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr hCatAdmin, IntPtr hFile, ref uint cbHash, byte[]? hash, uint flags);

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr hCatAdmin, byte[] hash, uint cbHash, uint flags, ref IntPtr prev);

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    static extern bool CryptCATCatalogInfoFromContext(IntPtr hCatInfo, ref CATALOG_INFO info, uint flags);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    static extern bool CryptCATAdminReleaseCatalogContext(IntPtr hCatAdmin, IntPtr hCatInfo, uint flags);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    static extern bool CryptCATAdminReleaseContext(IntPtr hCatAdmin, uint flags);

    static SignatureInfo Verify(string path)
    {
        try
        {
            if (!File.Exists(path)) return new SignatureInfo(SignatureStatus.NotApplicable, "");

            var embedded = VerifyEmbedded(path);
            if (embedded == 0) return new SignatureInfo(SignatureStatus.Valid, SignerName(path));

            if (embedded == TRUST_E_NOSIGNATURE || embedded == TRUST_E_PROVIDER_UNKNOWN || embedded == TRUST_E_SUBJECT_FORM_UNKNOWN)
            {
                if (VerifyCatalog(path)) return new SignatureInfo(SignatureStatus.Valid, "Microsoft Windows (catalog signed)");
                return embedded == TRUST_E_SUBJECT_FORM_UNKNOWN
                    ? new SignatureInfo(SignatureStatus.NotApplicable, "")
                    : new SignatureInfo(SignatureStatus.Unsigned, "");
            }

            // A signature is present. Only a digest mismatch means the file was changed after signing;
            // an expired, unknown or untrusted certificate is far more often just an old or niche publisher.
            var tampered = embedded == TRUST_E_BAD_DIGEST || embedded == TRUST_E_EXPLICIT_DISTRUST;
            return new SignatureInfo(tampered ? SignatureStatus.Tampered : SignatureStatus.Untrusted, SignerName(path));
        }
        catch
        {
            return new SignatureInfo(SignatureStatus.NotApplicable, "");
        }
    }

    static uint VerifyEmbedded(string path)
    {
        var filePath = Marshal.StringToHGlobalUni(path);
        var fileInfoPtr = IntPtr.Zero;
        try
        {
            var fi = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = filePath };
            fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            Marshal.StructureToPtr(fi, fileInfoPtr, false);

            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(), dwUIChoice = WTD_UI_NONE, fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE, pInfo = fileInfoPtr, dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_REVOCATION_CHECK_NONE,
            };
            var result = WinVerifyTrust(IntPtr.Zero, ref GenericVerifyV2, ref data);
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            WinVerifyTrust(IntPtr.Zero, ref GenericVerifyV2, ref data);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(filePath);
            if (fileInfoPtr != IntPtr.Zero) Marshal.FreeHGlobal(fileInfoPtr);
        }
    }

    static bool VerifyCatalog(string path)
    {
        if (!CryptCATAdminAcquireContext2(out var admin, IntPtr.Zero, "SHA256", IntPtr.Zero, 0)) return false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var handle = stream.SafeFileHandle.DangerousGetHandle();

            uint size = 0;
            CryptCATAdminCalcHashFromFileHandle2(admin, handle, ref size, null, 0);
            if (size == 0) return false;
            var hash = new byte[size];
            if (!CryptCATAdminCalcHashFromFileHandle2(admin, handle, ref size, hash, 0)) return false;

            var prev = IntPtr.Zero;
            var catalog = CryptCATAdminEnumCatalogFromHash(admin, hash, size, 0, ref prev);
            if (catalog == IntPtr.Zero) return false;

            var info = new CATALOG_INFO { cbStruct = (uint)Marshal.SizeOf<CATALOG_INFO>(), wszCatalogFile = "" };
            if (!CryptCATCatalogInfoFromContext(catalog, ref info, 0))
            {
                CryptCATAdminReleaseCatalogContext(admin, catalog, 0);
                return false;
            }

            var tag = Convert.ToHexString(hash);
            var tagPtr = Marshal.StringToHGlobalUni(tag);
            var catPtr = Marshal.StringToHGlobalUni(info.wszCatalogFile);
            var memberPtr = Marshal.StringToHGlobalUni(path);
            var hashPtr = Marshal.AllocHGlobal(hash.Length);
            var catInfoPtr = IntPtr.Zero;
            try
            {
                Marshal.Copy(hash, 0, hashPtr, hash.Length);
                var ci = new WINTRUST_CATALOG_INFO
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_CATALOG_INFO>(), pcwszCatalogFilePath = catPtr, pcwszMemberTag = tagPtr,
                    pcwszMemberFilePath = memberPtr, hMemberFile = IntPtr.Zero, pbCalculatedFileHash = hashPtr,
                    cbCalculatedFileHash = size, hCatAdmin = admin,
                };
                catInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_CATALOG_INFO>());
                Marshal.StructureToPtr(ci, catInfoPtr, false);

                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(), dwUIChoice = WTD_UI_NONE, fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_CATALOG, pInfo = catInfoPtr, dwStateAction = WTD_STATEACTION_VERIFY,
                    dwProvFlags = WTD_REVOCATION_CHECK_NONE,
                };
                var result = WinVerifyTrust(IntPtr.Zero, ref GenericVerifyV2, ref data);
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(IntPtr.Zero, ref GenericVerifyV2, ref data);
                CryptCATAdminReleaseCatalogContext(admin, catalog, 0);
                return result == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(tagPtr); Marshal.FreeHGlobal(catPtr); Marshal.FreeHGlobal(memberPtr);
                Marshal.FreeHGlobal(hashPtr);
                if (catInfoPtr != IntPtr.Zero) Marshal.FreeHGlobal(catInfoPtr);
            }
        }
        catch { return false; }
        finally { CryptCATAdminReleaseContext(admin, 0); }
    }

    static string SignerName(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // CreateFromSignedFile is the simplest way to read the embedded signer
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return cert.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch { return ""; }
    }
}
