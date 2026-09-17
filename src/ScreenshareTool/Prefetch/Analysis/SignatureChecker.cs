using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Serilog;







internal static class SignatureChecker
{
    private static readonly ILogger Logger = Log.ForContext(typeof(SignatureChecker));

    private const long CatalogHashSizeLimit = 16L * 1024 * 1024;
    private const int MaxCatalogEnum = 6;
    private const int MaxPeOffset = 16 * 1024 * 1024;
    
    private const int CacheLimit = 30000;

    private static readonly ConcurrentDictionary<string, CacheEntry> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    
    
    
    private static readonly SemaphoreSlim VerifyGate = new(2, 2);

    private static readonly Lazy<HashSet<string>> AuthRootThumbprints =
        new(LoadAuthRootThumbprints, LazyThreadSafetyMode.ExecutionAndPublication);

    
    
    
    
    private static readonly string[] CheatPublishers =
    {
        "manthe industries, llc", "manthe industries", "slinkware", "amstion limited", "manticore oy",
        "vape.gg", "slinky.gg", "faked signatures inc", "newfakeco", "intent.store", "raven b+", "riseclient",
        "meteordevelopment", "liquidbounce", "wurst client", "wurst", "aristois", "futureclient", "future client", "entropy.gg", "whiteout.gg",
        "novoline", "tenacity", "rusrush", "meteor client", "vape lite", "xynox", "null sense",
        "phobos client", "pyro client", "konas client", "sigma client",
        "raccoon4", "impact client", "dream client", "lavaclient",
        "methclient", "blueclient", "monsoonclient", "kaijuclient",
        "thunderhack", "salhack", "rusherhack", "feather client",
        "lunar client", "badlion client", "labymod", "essential mod",
        "risinghack", "astolfo client", "novoline v3", "tenacity client",
        "celestial client", "axion client", "azura client",
        "celes client", "zephyr client", "earthclient", "void client",
        "netnovoline", "nettenacity", "netcelestial", "netaxion",
        "brainless", "exploit", "injection", "xnull", "zenith",
        "skeet", "gamesense", "onetap", "neverlose", "solenoid", "flarial", "exhibition", "moon client",
    };

    
    private static readonly HashSet<string> TrustedPublishers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft Corporation", "Microsoft Windows", "NVIDIA Corporation", "Intel Corporation",
        "Google LLC", "Mozilla Corporation", "Oracle Corporation", "Adobe Inc.",
        "Valve Corporation", "Steam", "Discord Inc.", "Brave Software, Inc.",
        "Epic Games, Inc.", "Amazon Technologies Inc.", "Cloudflare, Inc.",
        "Notepad++", "VideoLAN", "Blender Foundation", "Unity Technologies",
        "Riot Games, Inc.", "Electronic Arts Inc.", "Ubisoft Entertainment",
        "Qualcomm Incorporated", "Advanced Micro Devices, Inc.", "Broadcom Inc.",
        "Realtek Semiconductor Corp.", "Synaptics Incorporated",
        "VMware, Inc.", "Red Hat, Inc.", "Canonical Ltd.", "SUSE LLC", "Linux Foundation",
    };

    
    private static readonly HashSet<string> BadThumbprints =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct CacheEntry(long Length, long WriteUtcTicks, SignatureStatus Status);

    private readonly record struct PeProbe(
        bool Exists,
        bool IsPe,
        bool HasSecurityDirectory,
        long Length,
        long WriteUtcTicks);

    private static void PutCache(string path, CacheEntry entry)
    {
        if (Cache.Count >= CacheLimit)
            Cache.Clear();
        Cache[path] = entry;
    }

    
    public static void ClearCache() => Cache.Clear();

    
    
    
    
    
    public static SignatureStatus GetSignatureStatus(string path)
        => Evaluate(path).Status;

    public readonly record struct Verdict(SignatureStatus Status, string Detail);

    
    
    
    
    public static Verdict Evaluate(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || ForensicUtil.IsUnresolved(path))
                return new Verdict(SignatureStatus.NotFound, "File not found — signature not evaluated");

            return EvaluateCore(path);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Signature check failed for {Path}", path);
            if (ex is FileNotFoundException or DirectoryNotFoundException)
                return new Verdict(SignatureStatus.NotFound, "File not found — signature not evaluated");
            return new Verdict(SignatureStatus.Unsigned, "Signature check failed: " + ex.GetType().Name);
        }
    }

    private static Verdict EvaluateCore(string path)
    {
        string resolved = ForensicUtil.ResolveExistingPath(path);
        if (string.IsNullOrWhiteSpace(resolved) || ForensicUtil.IsUnresolved(resolved))
            return new Verdict(SignatureStatus.NotFound, "File not found — signature not evaluated");
        if (!ForensicUtil.FileExistsNative(resolved))
            return new Verdict(SignatureStatus.NotFound, "File not found — signature not evaluated");

        PeProbe probe;
        try
        {
            probe = ProbePe(resolved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new Verdict(SignatureStatus.NotFound, "File not readable — signature not evaluated");
        }

        if (!probe.Exists)
            return new Verdict(SignatureStatus.NotFound, "File not found — signature not evaluated");
        if (!probe.IsPe)
            return new Verdict(SignatureStatus.NotMZ, "Not a PE (no MZ header)");

        if (Cache.TryGetValue(resolved, out var cached) &&
            cached.Length == probe.Length &&
            cached.WriteUtcTicks == probe.WriteUtcTicks)
        {
            return new Verdict(cached.Status, DetailFor(cached.Status, catalogHint: false, resolved));
        }

        
        
        
        
        SignatureStatus status;
        VerifyGate.Wait();
        try
        {
            status = Evaluate(resolved, probe);
        }
        finally
        {
            VerifyGate.Release();
        }
        PutCache(resolved, new CacheEntry(probe.Length, probe.WriteUtcTicks, status));
        return new Verdict(status, DetailFor(status, catalogHint: !probe.HasSecurityDirectory && status == SignatureStatus.Signed, resolved));
    }

    private static string DetailFor(SignatureStatus status, bool catalogHint, string path)
        => status switch
        {
            SignatureStatus.Signed when catalogHint => "Catalog-signed (WinVerifyTrust)" + SignerSuffix(path),
            SignatureStatus.Signed => "Authenticode valid (WinVerifyTrust)" + SignerSuffix(path),
            SignatureStatus.Unsigned => "No embedded or catalog signature",
            SignatureStatus.Cheat => "Known cheat publisher" + CheatPublisherDetail(path),
            SignatureStatus.Fake => "Untrusted / hash mismatch / revoked" + FakeDetail(path),
            SignatureStatus.NotMZ => "Not a PE (no MZ header)",
            _ => "File not found — signature not evaluated"
        };

    private static string CheatPublisherDetail(string path)
    {
        try
        {
            if (TryGetSignerCertificate(path, out var cert) && cert is not null)
            {
                using (cert)
                {
                    string? cn = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
                    if (!string.IsNullOrWhiteSpace(cn))
                        return " — " + cn;
                }
            }
        }
        catch { }
        return "";
    }

    private static string FakeDetail(string path)
    {
        try
        {
            if (TryGetSignerCertificate(path, out var cert) && cert is not null)
            {
                using (cert)
                {
                    var details = new List<string>(2);
                    string? cn = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
                    if (!string.IsNullOrWhiteSpace(cn))
                        details.Add("CN: " + cn);
                    try
                    {
                        using var rsaPub = cert.GetRSAPublicKey();
                        if (rsaPub is not null)
                            details.Add("RSA-" + rsaPub.KeySize);
                    }
                    catch { }
                    return details.Count > 0 ? " — " + string.Join(", ", details) : "";
                }
            }
        }
        catch { }
        return "";
    }

    
    
    private static string SignerSuffix(string path)
    {
        try
        {
            if (TryGetSignerCertificate(path, out var cert) && cert is not null)
            {
                using (cert)
                {
                    string? cn = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
                    if (!string.IsNullOrWhiteSpace(cn))
                        return " — " + cn;
                }
            }
        }
        catch
        {
            
        }
        return "";
    }

    private static SignatureStatus Evaluate(string path, PeProbe probe)
    {
        IntPtr hFile = OpenRead(path);
        try
        {
            int embeddedHr = NativeMethods.TRUST_E_NOSIGNATURE;
            if (hFile != IntPtr.Zero)
                embeddedHr = VerifyEmbedded(path, hFile);
            else
                embeddedHr = VerifyEmbedded(path, IntPtr.Zero);

            bool catalogOk = false;
            if (embeddedHr != 0 &&
                hFile != IntPtr.Zero &&
                probe.Length > 0 &&
                probe.Length <= CatalogHashSizeLimit)
            {
                
                
                
                
                catalogOk = TryVerifyCatalog(path, hFile);
            }

            return Classify(path, embeddedHr, catalogOk, probe.HasSecurityDirectory);
        }
        finally
        {
            if (hFile != IntPtr.Zero)
                NativeMethods.CloseHandle(hFile);
        }
    }

    private static SignatureStatus Classify(
        string path,
        int embeddedHr,
        bool catalogOk,
        bool hasSecurityDirectory)
    {
        bool trustOk = embeddedHr == 0 || catalogOk;

        if (TryGetSignerCertificate(path, out var cert) && cert is not null)
        {
            using (cert)
            {
                if (IsCheatPublisher(cert))
                    return SignatureStatus.Cheat;

                
                if (trustOk)
                    return SignatureStatus.Signed;

                if (IsFakeCertificate(cert))
                    return SignatureStatus.Fake;
            }
        }
        else if (trustOk)
        {
            
            return SignatureStatus.Signed;
        }

        if (IsHashMismatch(embeddedHr) && hasSecurityDirectory)
            return SignatureStatus.Fake;
        if (IsRevoked(embeddedHr))
            return SignatureStatus.Fake;

        return SignatureStatus.Unsigned;
    }

    private static bool IsFakeCertificate(X509Certificate2 cert)
    {
        if (ChainTouchesAuthRoot(cert))
            return false;
        if (ChainStructurallyInvalid(cert))
            return true;
        if (!string.IsNullOrEmpty(cert.Thumbprint) && BadThumbprints.Contains(cert.Thumbprint))
            return true;

        
        try
        {
            if (cert.Subject == cert.Issuer && cert.NotAfter > DateTime.Now.AddYears(5))
                return true;
            
            if (cert.Subject == cert.Issuer && (cert.NotAfter - cert.NotBefore).TotalDays > 365 * 15)
                return true;
        }
        catch { }

        string subject = (cert.Subject ?? "").Trim();
        if (subject.Length == 0 ||
            subject.Equals("CN=", StringComparison.OrdinalIgnoreCase) ||
            subject.Equals("CN=\"\"", StringComparison.OrdinalIgnoreCase))
            return true;

        string simple = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false) ?? "";
        if (string.IsNullOrWhiteSpace(simple) && subject.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
        {
            string cn = subject[3..].Trim().Trim('"');
            if (cn.Length == 0) return true;
        }

        string hay = (subject + " " + simple).ToLowerInvariant();

        
        
        
        if (HasConfusableVendorName(subject, simple))
            return true;

        
        
        if (AuthRootThumbprints.Value.Count > 0 &&
            (hay.Contains("microsoft") || hay.Contains("windows publisher")))
            return true;

        
        try
        {
            using var rsa = cert.GetRSAPublicKey();
            if (rsa is not null && rsa.KeySize < 2048)
                return true;
        }
        catch { }

        
        try
        {
            string sigAlg = cert.SignatureAlgorithm.FriendlyName ?? "";
            if (string.Equals(sigAlg, "sha1RSA", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sigAlg, "md5RSA", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch { }

        
        
        try
        {
            string issuerSimple = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: true) ?? "";
            string issuerLower = issuerSimple.ToLowerInvariant();
            if (issuerLower.Contains("test") || issuerLower.Contains("example") ||
                issuerLower.Contains("demo") || issuerLower.Contains("sample"))
                return true;
        }
        catch { }

        
        try
        {
            if (cert.NotAfter < DateTime.Now.AddMonths(-6) && hay.Length < 10)
                return true;
        }
        catch { }

        return false;
    }

    private static bool IsCheatPublisher(X509Certificate2 cert)
    {
        string subject = cert.Subject ?? "";
        string simple = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false) ?? "";
        string haystack = (subject + "\n" + simple).ToLowerInvariant();
        foreach (string publisher in CheatPublishers)
            if (haystack.Contains(publisher, StringComparison.Ordinal)) return true;
        return false;
    }

    
    
    
    private static readonly string[] MajorVendorNames =
    {
        "microsoft", "google", "nvidia", "intel", "adobe", "valve",
        "oracle", "apple", "discord", "epic games", "amazon",
        "qualcomm", "vmware", "mozilla", "samsung", "amd",
    };

    
    
    
    
    
    
    internal static bool HasConfusableVendorName(string subject, string simple)
    {
        string hay = (subject + " " + simple).ToLowerInvariant();
        string mapped = MapConfusables(hay);
        if (mapped == hay)
            return false;
        foreach (string vendor in MajorVendorNames)
            if (mapped.Contains(vendor, StringComparison.Ordinal))
                return true;
        return false;
    }

    
    
    
    private static string MapConfusables(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
        {
            char m = c switch
            {
                '\u0430' or '\u03b1' => 'a',          
                '\u0432' => 'b',                      
                '\u0441' or '\u0455' => 'c',          
                '\u0435' or '\u0454' => 'e',          
                '\u0434' => 'g',                      
                '\u043d' or '\u04bb' => 'h',          
                '\u0456' or '\u0457' or '\u0131' or '\u03b9' or '\u2170' or '\u2160' or '\u0406' => 'i', 
                '\u0458' => 'j',                      
                '\u043a' => 'k',                      
                '\u2113' or '\u217c' => 'l',          
                '\u043c' => 'm',                      
                '\u043f' => 'n',                      
                '\u043e' or '\u03bf' => 'o',          
                '\u0440' => 'p',                      
                '\u0433' or '\u044f' => 'r',          
                '\u0442' => 't',                      
                '\u0445' => 'x',                      
                '\u0443' => 'y',                      
                '\u0437' => 'z',                      
                _ when c >= '\uFF01' && c <= '\uFF5E' => (char)(c - 0xFEE0), 
                _ => c,
            };
            sb.Append(m);
        }
        return sb.ToString();
    }
    public static bool IsTrustedPublisher(X509Certificate2 cert)
    {
        string n = cert.GetNameInfo(X509NameType.SimpleName, false) ?? cert.Subject ?? "";
        return TrustedPublishers.Contains(n.Trim());
    }

    

    private static int VerifyEmbedded(string path, IntPtr hFile)
    {
        int hr = VerifyEmbeddedSimple(path, hFile, extraProv: 0);
        if (hr == 0)
            return 0;

        if (hr == NativeMethods.CERT_E_EXPIRED)
        {
            int lifetime = VerifyEmbeddedSimple(path, hFile, NativeMethods.WTD_LIFETIME_SIGNING_FLAG);
            if (lifetime == 0)
                return 0;
        }

        if (IsNoSignature(hr))
            return hr;

        
        int nestedHr = VerifyEmbeddedIndex(path, hFile, 0, getSecondaryCount: true, out uint secondary, extraProv: 0);
        if (nestedHr == 0)
            return 0;

        for (uint i = 1; i <= secondary && i < 8; i++)
        {
            int nested = VerifyEmbeddedIndex(path, hFile, i, getSecondaryCount: false, out _, extraProv: 0);
            if (nested == 0)
                return 0;
            if (nested == NativeMethods.CERT_E_EXPIRED)
            {
                int lifetime = VerifyEmbeddedIndex(
                    path, hFile, i, getSecondaryCount: false, out _, NativeMethods.WTD_LIFETIME_SIGNING_FLAG);
                if (lifetime == 0)
                    return 0;
            }
        }

        return hr;
    }

    private static int VerifyEmbeddedSimple(string path, IntPtr hFile, uint extraProv)
    {
        var fileInfo = new NativeMethods.WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>(),
            pcwszFilePath = path,
            hFile = hFile,
            pgKnownSubject = IntPtr.Zero,
        };

        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, fDeleteOld: false);
            var data = NewTrustData(pFile, NativeMethods.WTD_CHOICE_FILE, extraProv);
            return VerifyAndClose(ref data);
        }
        finally
        {
            Marshal.DestroyStructure<NativeMethods.WINTRUST_FILE_INFO>(pFile);
            Marshal.FreeHGlobal(pFile);
        }
    }

    private static int VerifyEmbeddedIndex(
        string path,
        IntPtr hFile,
        uint index,
        bool getSecondaryCount,
        out uint secondaryCount,
        uint extraProv)
    {
        secondaryCount = 0;
        var fileInfo = new NativeMethods.WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>(),
            pcwszFilePath = path,
            hFile = hFile,
            pgKnownSubject = IntPtr.Zero,
        };

        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>());
        IntPtr pSettings = IntPtr.Zero;
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, fDeleteOld: false);

            var settings = new NativeMethods.WINTRUST_SIGNATURE_SETTINGS
            {
                cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_SIGNATURE_SETTINGS>(),
                dwIndex = index,
                dwFlags = NativeMethods.WSS_VERIFY_SPECIFIC,
            };
            if (getSecondaryCount)
                settings.dwFlags |= NativeMethods.WSS_GET_SECONDARY_SIG_COUNT;

            pSettings = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WINTRUST_SIGNATURE_SETTINGS>());
            Marshal.StructureToPtr(settings, pSettings, fDeleteOld: false);

            var data = NewTrustData(pFile, NativeMethods.WTD_CHOICE_FILE, extraProv);
            data.pSignatureSettings = pSettings;

            int hr = VerifyAndClose(ref data);
            if (getSecondaryCount)
            {
                var updated = Marshal.PtrToStructure<NativeMethods.WINTRUST_SIGNATURE_SETTINGS>(pSettings);
                secondaryCount = updated.cSecondarySigs;
            }

            return hr;
        }
        finally
        {
            if (pSettings != IntPtr.Zero)
                Marshal.FreeHGlobal(pSettings);
            Marshal.DestroyStructure<NativeMethods.WINTRUST_FILE_INFO>(pFile);
            Marshal.FreeHGlobal(pFile);
        }
    }

    

    private static bool TryVerifyCatalog(string path, IntPtr hFile)
    {
        try
        {
            if (TryVerifyCatalogAlgo(path, hFile, "SHA256"))
                return true;
            if (TryVerifyCatalogAlgo(path, hFile, "SHA1"))
                return true;
            return TryVerifyCatalogLegacy(path, hFile);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Catalog verify failed for {Path}", path);
            return false;
        }
    }

    private static bool TryVerifyCatalogAlgo(string path, IntPtr hFile, string algorithm)
    {
        if (!TryAcquireCatAdmin2(algorithm, NativeMethods.DRIVER_ACTION_VERIFY, out IntPtr hCat))
            return false;

        byte[]? hash;
        try
        {
            hash = CalcHashV2(hCat, hFile);
        }
        catch (EntryPointNotFoundException)
        {
            NativeMethods.CryptCATAdminReleaseContext(hCat, 0);
            return false;
        }

        if (hash is null || hash.Length == 0)
        {
            NativeMethods.CryptCATAdminReleaseContext(hCat, 0);
            return false;
        }

        try
        {
            if (TryVerifyFromHash(path, hFile, hCat, hash))
                return true;
        }
        finally
        {
            NativeMethods.CryptCATAdminReleaseContext(hCat, 0);
        }

        if (!TryAcquireCatAdmin2(algorithm, null, out hCat))
            return false;
        try
        {
            return TryVerifyFromHash(path, hFile, hCat, hash);
        }
        finally
        {
            NativeMethods.CryptCATAdminReleaseContext(hCat, 0);
        }
    }

    private static bool TryVerifyCatalogLegacy(string path, IntPtr hFile)
    {
        Guid subsystem = NativeMethods.DRIVER_ACTION_VERIFY;
        if (!NativeMethods.CryptCATAdminAcquireContext(out IntPtr hCat, ref subsystem, 0) ||
            hCat == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            byte[]? hash = CalcHashV1(hFile);
            if (hash is null || hash.Length == 0)
                return false;
            return TryVerifyFromHash(path, hFile, hCat, hash);
        }
        finally
        {
            NativeMethods.CryptCATAdminReleaseContext(hCat, 0);
        }
    }

    private static bool TryAcquireCatAdmin2(string algorithm, Guid? subsystem, out IntPtr hCat)
    {
        hCat = IntPtr.Zero;
        IntPtr pGuid = IntPtr.Zero;
        try
        {
            if (subsystem is Guid g)
            {
                pGuid = Marshal.AllocHGlobal(16);
                Marshal.StructureToPtr(g, pGuid, fDeleteOld: false);
            }

            if (!NativeMethods.CryptCATAdminAcquireContext2(out hCat, pGuid, algorithm, IntPtr.Zero, 0))
            {
                hCat = IntPtr.Zero;
                return false;
            }

            return hCat != IntPtr.Zero;
        }
        catch (EntryPointNotFoundException)
        {
            hCat = IntPtr.Zero;
            return false;
        }
        finally
        {
            if (pGuid != IntPtr.Zero)
                Marshal.FreeHGlobal(pGuid);
        }
    }

    private static byte[]? CalcHashV2(IntPtr hCatAdmin, IntPtr hFile)
    {
        uint cb = 0;
        if (!NativeMethods.CryptCATAdminCalcHashFromFileHandle2(hCatAdmin, hFile, ref cb, null, 0) || cb == 0)
            return null;
        var hash = new byte[cb];
        if (!NativeMethods.CryptCATAdminCalcHashFromFileHandle2(hCatAdmin, hFile, ref cb, hash, 0))
            return null;
        if (cb != (uint)hash.Length && cb > 0 && cb <= hash.Length)
            Array.Resize(ref hash, (int)cb);
        return hash;
    }

    private static byte[]? CalcHashV1(IntPtr hFile)
    {
        uint cb = 0;
        if (!NativeMethods.CryptCATAdminCalcHashFromFileHandle(hFile, ref cb, null, 0) || cb == 0)
            return null;
        var hash = new byte[cb];
        if (!NativeMethods.CryptCATAdminCalcHashFromFileHandle(hFile, ref cb, hash, 0))
            return null;
        return hash;
    }

    private static bool TryVerifyFromHash(string path, IntPtr hFile, IntPtr hCatAdmin, byte[] hash)
    {
        IntPtr prev = IntPtr.Zero;
        IntPtr hCatInfo = NativeMethods.CryptCATAdminEnumCatalogFromHash(
            hCatAdmin, hash, (uint)hash.Length, 0, ref prev);

        try
        {
            int seen = 0;
            while (hCatInfo != IntPtr.Zero && seen++ < MaxCatalogEnum)
            {
                var info = new NativeMethods.CATALOG_INFO
                {
                    cbStruct = (uint)Marshal.SizeOf<NativeMethods.CATALOG_INFO>(),
                    wszCatalogFile = "",
                };

                if (NativeMethods.CryptCATCatalogInfoFromContext(hCatInfo, ref info, 0) &&
                    !string.IsNullOrEmpty(info.wszCatalogFile))
                {
                    int hr = VerifyCatalogMember(path, info.wszCatalogFile, hFile, hash, hCatAdmin);
                    if (hr == 0)
                        return true;
                    if (hr == NativeMethods.CERT_E_EXPIRED)
                    {
                        hr = VerifyCatalogMember(
                            path, info.wszCatalogFile, hFile, hash, hCatAdmin,
                            NativeMethods.WTD_LIFETIME_SIGNING_FLAG);
                        if (hr == 0)
                            return true;
                    }
                }

                prev = hCatInfo;
                hCatInfo = NativeMethods.CryptCATAdminEnumCatalogFromHash(
                    hCatAdmin, hash, (uint)hash.Length, 0, ref prev);
            }

            return false;
        }
        finally
        {
            if (hCatInfo != IntPtr.Zero)
                NativeMethods.CryptCATAdminReleaseCatalogContext(hCatAdmin, hCatInfo, 0);
        }
    }

    private static int VerifyCatalogMember(
        string filePath,
        string catalogPath,
        IntPtr hFile,
        byte[] hash,
        IntPtr hCatAdmin,
        uint extraProv = 0)
    {
        IntPtr pHash = Marshal.AllocHGlobal(hash.Length);
        IntPtr pCat = IntPtr.Zero;
        try
        {
            Marshal.Copy(hash, 0, pHash, hash.Length);
            var cat = new NativeMethods.WINTRUST_CATALOG_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_CATALOG_INFO>(),
                dwCatalogVersion = 0,
                pcwszCatalogFilePath = catalogPath,
                pcwszMemberTag = Convert.ToHexString(hash),
                pcwszMemberFilePath = filePath,
                hMemberFile = hFile,
                pbCalculatedFileHash = pHash,
                cbCalculatedFileHash = (uint)hash.Length,
                pcCatalogContext = IntPtr.Zero,
                hCatAdmin = hCatAdmin,
            };

            pCat = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WINTRUST_CATALOG_INFO>());
            Marshal.StructureToPtr(cat, pCat, fDeleteOld: false);

            var data = NewTrustData(pCat, NativeMethods.WTD_CHOICE_CATALOG, extraProv);
            return VerifyAndClose(ref data);
        }
        finally
        {
            if (pCat != IntPtr.Zero)
            {
                Marshal.DestroyStructure<NativeMethods.WINTRUST_CATALOG_INFO>(pCat);
                Marshal.FreeHGlobal(pCat);
            }

            Marshal.FreeHGlobal(pHash);
        }
    }

    

    private static uint DefaultProvFlags =>
        NativeMethods.WTD_REVOCATION_CHECK_NONE |
        NativeMethods.WTD_CACHE_ONLY_URL_RETRIEVAL |
        NativeMethods.WTD_DISABLE_MD2_MD4;

    private static NativeMethods.WINTRUST_DATA NewTrustData(IntPtr pChoice, uint unionChoice, uint extraProv)
    {
        return new NativeMethods.WINTRUST_DATA
        {
            cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_DATA>(),
            pPolicyCallbackData = IntPtr.Zero,
            pSIPClientData = IntPtr.Zero,
            dwUIChoice = NativeMethods.WTD_UI_NONE,
            fdwRevocationChecks = NativeMethods.WTD_REVOKE_NONE,
            dwUnionChoice = unionChoice,
            pFile = pChoice,
            dwStateAction = NativeMethods.WTD_STATEACTION_VERIFY,
            hWVTStateData = IntPtr.Zero,
            pwszURLReference = IntPtr.Zero,
            dwProvFlags = DefaultProvFlags | extraProv,
            dwUIContext = 0,
            pSignatureSettings = IntPtr.Zero,
        };
    }

    private static int VerifyAndClose(ref NativeMethods.WINTRUST_DATA data)
    {
        Guid action = NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2;
        int hr = NativeMethods.TRUST_E_NOSIGNATURE;
        try
        {
            data.dwStateAction = NativeMethods.WTD_STATEACTION_VERIFY;
            hr = NativeMethods.WinVerifyTrust(IntPtr.Zero, ref action, ref data);
        }
        finally
        {
            data.dwStateAction = NativeMethods.WTD_STATEACTION_CLOSE;
            _ = NativeMethods.WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            data.hWVTStateData = IntPtr.Zero;
        }

        return hr;
    }

    private static bool IsNoSignature(int hr) => hr == NativeMethods.TRUST_E_NOSIGNATURE;

    private static bool IsHashMismatch(int hr) =>
        hr == NativeMethods.TRUST_E_BAD_DIGEST || hr == NativeMethods.NTE_BAD_SIGNATURE;

    private static bool IsRevoked(int hr) =>
        hr == NativeMethods.CERT_E_REVOKED || hr == NativeMethods.TRUST_E_EXPLICIT_DISTRUST;

    

    private static bool TryGetSignerCertificate(string path, out X509Certificate2? cert)
    {
        cert = null;
        try
        {
            using X509Certificate raw = X509Certificate.CreateFromSignedFile(path);
            cert = new X509Certificate2(raw);
            return true;
        }
        catch
        {
            cert?.Dispose();
            cert = null;
            return false;
        }
    }

    
    
    
    
    
    private static bool ChainStructurallyInvalid(X509Certificate2 cert)
    {
        try
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromMilliseconds(250);
            chain.ChainPolicy.VerificationTime = DateTime.Now;
            chain.ChainPolicy.VerificationFlags =
                X509VerificationFlags.IgnoreNotTimeValid |
                X509VerificationFlags.IgnoreCtlNotTimeValid |
                X509VerificationFlags.IgnoreWrongUsage |
                X509VerificationFlags.IgnoreEndRevocationUnknown |
                X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown |
                X509VerificationFlags.IgnoreRootRevocationUnknown |
                X509VerificationFlags.AllowUnknownCertificateAuthority;

            _ = chain.Build(cert);
            foreach (var s in chain.ChainStatus)
            {
                if (s.Status is X509ChainStatusFlags.PartialChain or X509ChainStatusFlags.NotSignatureValid)
                    return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "X509Chain structure check failed");
        }
        return false;
    }

    private static bool ChainTouchesAuthRoot(X509Certificate2 cert)
    {
        HashSet<string> roots = AuthRootThumbprints.Value;
        if (roots.Count == 0)
            return false;

        try
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromMilliseconds(250);
            chain.ChainPolicy.VerificationTime = DateTime.Now;
            chain.ChainPolicy.VerificationFlags =
                X509VerificationFlags.IgnoreNotTimeValid |
                X509VerificationFlags.IgnoreCtlNotTimeValid |
                X509VerificationFlags.IgnoreWrongUsage |
                X509VerificationFlags.AllowUnknownCertificateAuthority |
                X509VerificationFlags.IgnoreEndRevocationUnknown |
                X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown |
                X509VerificationFlags.IgnoreRootRevocationUnknown;

            _ = chain.Build(cert);
            foreach (var element in chain.ChainElements)
            {
                string? thumb = element.Certificate.Thumbprint;
                if (!string.IsNullOrEmpty(thumb) && roots.Contains(thumb))
                    return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "X509Chain build failed");
        }

        return !string.IsNullOrEmpty(cert.Thumbprint) && roots.Contains(cert.Thumbprint);
    }

    private static HashSet<string> LoadAuthRootThumbprints()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var store = new X509Store(StoreName.AuthRoot, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            foreach (X509Certificate2 c in store.Certificates)
            {
                try
                {
                    if (!string.IsNullOrEmpty(c.Thumbprint))
                        set.Add(c.Thumbprint);
                }
                finally
                {
                    c.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "AuthRoot store enumeration failed");
        }

        return set;
    }

    

    private static PeProbe ProbePe(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            return default;
        if ((info.Attributes & FileAttributes.Directory) != 0)
            return default;

        var probe = new PeProbe(
            Exists: true,
            IsPe: false,
            HasSecurityDirectory: false,
            Length: info.Length,
            WriteUtcTicks: info.LastWriteTimeUtc.Ticks);

        if (info.Length < 64)
            return probe;

        using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        Span<byte> dos = stackalloc byte[64];
        if (fs.Read(dos) < 64 || dos[0] != (byte)'M' || dos[1] != (byte)'Z')
            return probe;

        int eLfanew = BinaryPrimitives.ReadInt32LittleEndian(dos.Slice(0x3C));
        if (eLfanew < 64 || eLfanew > MaxPeOffset || eLfanew + 24L > info.Length)
            return probe;

        fs.Seek(eLfanew, SeekOrigin.Begin);
        Span<byte> peSig = stackalloc byte[4];
        if (fs.Read(peSig) < 4 ||
            peSig[0] != (byte)'P' || peSig[1] != (byte)'E' || peSig[2] != 0 || peSig[3] != 0)
        {
            return probe;
        }

        Span<byte> coff = stackalloc byte[20];
        if (fs.Read(coff) < 20)
            return probe with { IsPe = true };

        int optSize = BinaryPrimitives.ReadUInt16LittleEndian(coff.Slice(16));
        if (optSize < 96)
            return probe with { IsPe = true };

        byte[] opt = new byte[optSize];
        int got = fs.Read(opt, 0, opt.Length);
        if (got < 96)
            return probe with { IsPe = true };

        int magic = BinaryPrimitives.ReadUInt16LittleEndian(opt);
        if (magic is not (0x10B or 0x20B))
            return probe with { IsPe = true };

        int dirOff = magic == 0x20B ? 112 : 96;
        if (dirOff + 40 > got)
            return probe with { IsPe = true };

        int secOff = BinaryPrimitives.ReadInt32LittleEndian(opt.AsSpan(dirOff + 32));
        int secSize = BinaryPrimitives.ReadInt32LittleEndian(opt.AsSpan(dirOff + 36));
        return probe with
        {
            IsPe = true,
            HasSecurityDirectory = secOff != 0 && secSize > 0,
        };
    }

    private static IntPtr OpenRead(string path)
    {
        IntPtr h = NativeMethods.CreateFileW(
            ToNativePath(path),
            NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_SEQUENTIAL_SCAN,
            IntPtr.Zero);

        return h == NativeMethods.InvalidHandleValue ? IntPtr.Zero : h;
    }

    private static string ToNativePath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return path;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + path[2..];

        return @"\\?\" + path;
    }
}
