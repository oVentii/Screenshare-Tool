using System.Runtime.InteropServices;
using System.Text;

internal static class PrefetchSignature
{
    private static readonly string[] CheatSubjects =
    {
        "manthe industries, llc",
        "slinkware",
        "amstion limited",
        "newfakeco",
        "faked signatures inc"
    };

    private static readonly string[] CertStores =
    {
        "MY", "Root", "Trust", "CA", "UserDS",
        "TrustedPublisher", "Disallowed", "AuthRoot",
        "TrustedPeople", "ClientAuthIssuer",
        "CertificateEnrollment", "SmartCardRoot"
    };

    public static bool IsFileSignatureValid(string filePath)
    {
        try
        {
            return VerifySignatureCore(filePath).Valid;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Signature check failed for {Path}", filePath);
            return false;
        }
    }

    public static string GetBamStatus(string filePath)
    {
        try
        {
            var core = VerifySignatureCore(filePath);
            if (core.Valid)
                return "Signed";
            return core.Kind switch
            {
                "cheat" => "Cheat Signature",
                "fake" => "Fake Signature",
                _ => "Not signed"
            };
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Signature check failed for {Path}", filePath);
            return "Not signed";
        }
    }

    private static (bool Valid, string Kind) VerifySignatureCore(string filePath)
    {
        IntPtr filePathPtr = Marshal.StringToHGlobalUni(filePath);
        try
        {
            var fileInfo = new PrefetchNative.WinTrustFileInfo
            {
                StructSize = (uint)Marshal.SizeOf<PrefetchNative.WinTrustFileInfo>(),
                FilePath = filePathPtr
            };
            IntPtr fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PrefetchNative.WinTrustFileInfo>());
            try
            {
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
                var data = new PrefetchNative.WinTrustData
                {
                    StructSize = (uint)Marshal.SizeOf<PrefetchNative.WinTrustData>(),
                    UIChoice = PrefetchNative.WTD_UI_NONE,
                    RevocationChecks = PrefetchNative.WTD_REVOKE_NONE,
                    UnionChoice = PrefetchNative.WTD_CHOICE_FILE,
                    UnionInfo = fileInfoPtr,
                    StateAction = PrefetchNative.WTD_STATEACTION_VERIFY
                };

                Guid action = PrefetchNative.ActionGenericVerifyV2;
                int status = PrefetchNative.VerifyTrust(IntPtr.Zero, ref action, ref data);
                (bool Valid, string Kind) outcome = (true, "signed");

                if (status == 0)
                {
                    string kind = CheckSignerChain(data.StateData);
                    outcome = kind == "signed" ? (true, "signed") : (false, kind);
                }
                else
                {
                    outcome = VerifyFileViaCatalog(filePath)
                        ? (true, "signed")
                        : (false, "unsigned");
                }

                try
                {
                    data.StateAction = PrefetchNative.WTD_STATEACTION_CLOSE;
                    PrefetchNative.VerifyTrust(IntPtr.Zero, ref action, ref data);
                }
                catch
                {
                }

                return outcome;
            }
            finally
            {
                Marshal.FreeHGlobal(fileInfoPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(filePathPtr);
        }
    }

    private static string CheckSignerChain(IntPtr stateData)
    {
        try
        {
            IntPtr provData = PrefetchNative.ProvDataFromStateData(stateData);
            if (provData == IntPtr.Zero)
                return "signed";
            IntPtr signerPtr = PrefetchNative.GetProvSignerFromChain(provData, 0, false, 0);
            if (signerPtr == IntPtr.Zero)
                return "signed";
            IntPtr certPtr = PrefetchNative.GetProvCertFromChain(signerPtr, 0);
            if (certPtr == IntPtr.Zero)
                return "signed";

            var cert = Marshal.PtrToStructure<PrefetchNative.ProvCert>(certPtr);
            IntPtr certContext = cert.Cert;
            if (certContext == IntPtr.Zero)
                return "signed";

            string? subjectName = GetCertDisplayName(certContext)
                ?? GetCertSubjectLegacy(certContext);
            if (!string.IsNullOrEmpty(subjectName))
            {
                string lower = subjectName.ToLowerInvariant();
                foreach (string cheat in CheatSubjects)
                {
                    if (lower.Contains(cheat, StringComparison.Ordinal))
                        return "cheat";
                }
            }

            return IsCertHashInSystemStores(certContext) ? "fake" : "signed";
        }
        catch
        {
            return "signed";
        }
    }

    private static string? GetCertDisplayName(IntPtr certContext)
    {
        try
        {
            var sb = new StringBuilder(512);
            int len = PrefetchNative.GetCertNameString(
                certContext, PrefetchNative.CERT_NAME_SIMPLE_DISPLAY_TYPE,
                0, IntPtr.Zero, sb, (uint)sb.Capacity);
            if (len > 1)
                return sb.ToString(0, len - 1);
        }
        catch
        {
        }
        return null;
    }

    private static string? GetCertSubjectLegacy(IntPtr certContext)
    {
        try
        {
            uint encoding = (uint)Marshal.ReadInt32(certContext, 0);
            IntPtr certInfo = Marshal.ReadIntPtr(certContext, IntPtr.Size * 3);
            if (certInfo == IntPtr.Zero)
                return null;
            int subjectOffset = IntPtr.Size == 8 ? 80 : 48;
            IntPtr subject = IntPtr.Add(certInfo, subjectOffset);
            var nameBuilder = new StringBuilder(512);
            int len = PrefetchNative.CertNameToStr(
                encoding, subject,
                PrefetchNative.CERT_X500_NAME_STR, nameBuilder, (uint)nameBuilder.Capacity);
            if (len > 1)
                return nameBuilder.ToString(0, len - 1);
        }
        catch
        {
        }
        return null;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> StoreCache =
        new(StringComparer.Ordinal);

    private static bool IsCertHashInSystemStores(IntPtr certContext)
    {
        try
        {
            uint hashSize = 0;
            if (!PrefetchNative.GetCertContextProperty(
                    certContext, PrefetchNative.CERT_SHA1_HASH_PROP_ID, IntPtr.Zero, ref hashSize)
                || hashSize == 0 || hashSize > 64)
                return false;

            IntPtr hashPtr = Marshal.AllocHGlobal((int)hashSize);
            try
            {
                if (!PrefetchNative.GetCertContextProperty(
                        certContext, PrefetchNative.CERT_SHA1_HASH_PROP_ID, hashPtr, ref hashSize))
                    return false;

                byte[] hash = new byte[hashSize];
                Marshal.Copy(hashPtr, hash, 0, (int)hashSize);
                string key = BitConverter.ToString(hash);

                if (StoreCache.TryGetValue(key, out bool cached))
                    return cached;

                bool found = SweepStores(certContext, hashPtr, hashSize);
                StoreCache.TryAdd(key, found);
                return found;
            }
            finally
            {
                Marshal.FreeHGlobal(hashPtr);
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool SweepStores(IntPtr certContext, IntPtr hashPtr, uint hashSize)
    {
        try
        {
            uint encoding = (uint)Marshal.ReadInt32(certContext, 0);
            uint[] scopes =
            {
                PrefetchNative.CERT_SYSTEM_STORE_CURRENT_USER | PrefetchNative.CERT_STORE_OPEN_EXISTING_FLAG,
                PrefetchNative.CERT_SYSTEM_STORE_LOCAL_MACHINE | PrefetchNative.CERT_STORE_OPEN_EXISTING_FLAG
            };

            foreach (uint scope in scopes)
            {
                foreach (string storeName in CertStores)
                {
                    IntPtr store = PrefetchNative.OpenStore(
                        PrefetchNative.CERT_STORE_PROV_SYSTEM_W,
                        PrefetchNative.X509_ASN_ENCODING | PrefetchNative.PKCS_7_ASN_ENCODING,
                        IntPtr.Zero, scope, storeName);
                    if (store == IntPtr.Zero)
                        continue;
                    try
                    {
                        var blob = new PrefetchNative.CryptHashBlob
                        {
                            Size = hashSize,
                            Data = hashPtr
                        };
                        IntPtr found = PrefetchNative.FindCertificateInStore(
                            store, encoding, 0,
                            PrefetchNative.CERT_FIND_SHA1_HASH,
                            ref blob, IntPtr.Zero);
                        if (found != IntPtr.Zero)
                        {
                            PrefetchNative.FreeCertificateContext(found);
                            return true;
                        }
                    }
                    finally
                    {
                        PrefetchNative.CloseStore(store, 0);
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool VerifyFileViaCatalog(string filePath)
    {
        Guid action = PrefetchNative.DriverActionVerify;
        if (!PrefetchNative.CatAdminAcquireContext(out IntPtr catAdmin, ref action, 0))
            return false;

        try
        {
            IntPtr file = PrefetchNative.CreateFile(
                filePath, PrefetchNative.GENERIC_READ,
                PrefetchNative.FILE_SHARE_READ | PrefetchNative.FILE_SHARE_WRITE | PrefetchNative.FILE_SHARE_DELETE,
                IntPtr.Zero, PrefetchNative.OPEN_EXISTING, 0, IntPtr.Zero);
            if (file == NativeMethods.InvalidHandleValue)
                return false;

            try
            {
                uint hashSize = 0;
                PrefetchNative.CatAdminCalcHashFromFileHandle(file, ref hashSize, IntPtr.Zero, 0);
                if (hashSize == 0 || hashSize > 256)
                    return false;

                IntPtr hashPtr = Marshal.AllocHGlobal((int)hashSize);
                try
                {
                    if (!PrefetchNative.CatAdminCalcHashFromFileHandle(file, ref hashSize, hashPtr, 0))
                        return false;

                    byte[] hash = new byte[hashSize];
                    Marshal.Copy(hashPtr, hash, 0, (int)hashSize);

                    IntPtr catalog = PrefetchNative.CatAdminEnumCatalogFromHash(
                        catAdmin, hash, hashSize, 0, IntPtr.Zero);
                    bool signed = false;

                    while (catalog != IntPtr.Zero)
                    {
                        var catalogInfo = new PrefetchNative.CatalogInfo
                        {
                            StructSize = (uint)Marshal.SizeOf<PrefetchNative.CatalogInfo>()
                        };
                        string catalogFile = "";
                        if (PrefetchNative.CatCatalogInfoFromContext(catalog, ref catalogInfo, 0))
                            catalogFile = catalogInfo.CatalogFile ?? "";

                        bool verified = VerifyCatalogMember(
                            catalogFile, filePath, hash, catalog, ref signed);
                        if (verified)
                            break;

                        IntPtr next = PrefetchNative.CatAdminEnumCatalogFromHash(
                            catAdmin, hash, hashSize, 0, catalog);
                        PrefetchNative.CatAdminReleaseCatalogContext(catAdmin, catalog, 0);
                        catalog = next;
                    }

                    if (catalog != IntPtr.Zero)
                        PrefetchNative.CatAdminReleaseCatalogContext(catAdmin, catalog, 0);

                    return signed;
                }
                finally
                {
                    Marshal.FreeHGlobal(hashPtr);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(file);
            }
        }
        finally
        {
            PrefetchNative.CatAdminReleaseContext(catAdmin, 0);
        }
    }

    private static bool VerifyCatalogMember(
        string catalogFile, string memberPath, byte[] hash, IntPtr catalog, ref bool signed)
    {
        IntPtr hashPtr = Marshal.AllocHGlobal(hash.Length);
        try
        {
            Marshal.Copy(hash, 0, hashPtr, hash.Length);
            var catalogInfo = new PrefetchNative.WinTrustCatalogInfo
            {
                StructSize = (uint)Marshal.SizeOf<PrefetchNative.WinTrustCatalogInfo>(),
                CatalogFilePath = catalogFile,
                MemberFilePath = memberPath,
                CalculatedFileHash = hashPtr,
                CalculatedFileHashSize = (uint)hash.Length
            };
            IntPtr catalogInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PrefetchNative.WinTrustCatalogInfo>());
            try
            {
                Marshal.StructureToPtr(catalogInfo, catalogInfoPtr, false);
                var data = new PrefetchNative.WinTrustData
                {
                    StructSize = (uint)Marshal.SizeOf<PrefetchNative.WinTrustData>(),
                    UIChoice = PrefetchNative.WTD_UI_NONE,
                    RevocationChecks = PrefetchNative.WTD_REVOKE_NONE,
                    UnionChoice = PrefetchNative.WTD_CHOICE_CATALOG,
                    UnionInfo = catalogInfoPtr,
                    StateAction = PrefetchNative.WTD_STATEACTION_VERIFY
                };

                Guid action = PrefetchNative.ActionGenericVerifyV2;
                int status = PrefetchNative.VerifyTrust(IntPtr.Zero, ref action, ref data);

                try
                {
                    data.StateAction = PrefetchNative.WTD_STATEACTION_CLOSE;
                    PrefetchNative.VerifyTrust(IntPtr.Zero, ref action, ref data);
                }
                catch
                {
                }

                if (status == 0)
                {
                    signed = true;
                    return true;
                }

                return false;
            }
            finally
            {
                Marshal.DestroyStructure<PrefetchNative.WinTrustCatalogInfo>(catalogInfoPtr);
                Marshal.FreeHGlobal(catalogInfoPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(hashPtr);
        }
    }
}
