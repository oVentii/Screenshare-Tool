using System.IO;
using System.Security.Cryptography;
using System.Text;































internal static class YaraScanner
{
    private static readonly HashSet<string> DeepRuleSet = new(StringComparer.Ordinal)
    {
        "CHEAT", "INJECTOR_API", "MANUAL_MAP_HINTS", "KDMAPPER_LIKE",
        "JAVA_AGENT_CHEAT", "NULL_FORKED_RECOVERY", "SUSPICIOUS_MUTEX",
        "AUTOCLICKER", "CSHARP_CLICKER", "CLICK_INPUT_COMBO",
        "PE_INJECT_COMBO", "PE_DEBUG_ANTI", "PE_HIGH_ENTROPY_NO_SIG_HINT",
        "PE_RWX_SECTION", "GAME_OVERLAY_ABUSE",
        "NTDLL_UNDOCUMENTED", "THREAD_HIJACK_HINTS", "DRIVER_LOAD_ABUSE",
        "MEMORY_MODULE_HINTS", "TOKEN_STEAL_PRIV",
        "DYNAMIC_API_RESOLUTION", "NO_IMPORT_PE", "PE_OVERLAY_PAYLOAD",
        "PACKED_CODE_SECTION", "VIRTUALIZATION_DEBUGGING", "KNOWN_CHEAT_NAME",
        "ENCODED_CHEAT_STRING", "KNOWN_CHEAT_HASH",
        "LOW_LEVEL_INPUT_HOOK", "AIMBOT_HINTS", "NTDLL_UNHOOK",
        "D3D_RENDER_HOOK", "MEMORY_SCANNER", "STARTUP_PERSISTENCE",
        "MC_PACKET_MANIP",        "NETWORK_C2", "REGISTRY_PERSISTENCE",
        "DLL_SIDELOAD_TECHNIQUE", "WMI_PERSISTENCE", "PROCESS_DOPPELGANGING",
        "DOTNET_CHEAT_FRAMEWORK", "SCREENSHARE_EVASION", "KEYSTROKE_LOGGER",
        "WINDOW_CAPTURE_EVASION", "MC_ESP_HINTS", "VEH_INJECTION_HINTS",
        "POWERSHELL_STAGER", "ANTI_AMSI_ETW"
    };

    public static bool IsDeepRule(string name)
        => !string.IsNullOrEmpty(name) && DeepRuleSet.Contains(name);

    
    
    
    
    private const int StringScanLimit = 8 * 1024 * 1024;
    private const int MaxFileBytes = 8 * 1024 * 1024;
    private const int ChunkSize = 4 * 1024 * 1024;
    
    
    private const int ChunkOverlap = 1024;

    
    
    
    private static readonly System.Threading.SemaphoreSlim ScanGate = new(2, 2);

    
    
    private static readonly string DllKernel32 = "kernel32.dll";
    private static readonly string DllUser32 = "user32.dll";
    private static readonly string DllNtdll = "ntdll.dll";
    private static readonly string DllAdvapi32 = "advapi32.dll";
    
    
    
    
    private static string[] Dec(string b64)
    {
        byte[] bytes = Convert.FromBase64String(b64);
        int count = BitConverter.ToInt32(bytes, 0);
        var arr = new string[count];
        int at = 4;
        for (int i = 0; i < count; i++)
        {
            int len = BitConverter.ToInt32(bytes, at); at += 4;
            arr[i] = Encoding.UTF8.GetString(bytes, at, len);
            at += len;
        }
        return arr;
    }

    private static string S(string b64) => Encoding.UTF8.GetString(Convert.FromBase64String(b64));


    private static readonly string[] IocAutoclicker =
    Dec("BQAAAAsAAABBdXRvQ2xpY2tlcg4AAABDbGljayBJbnRlcnZhbA4AAABTdGFydCBDbGlja2luZw0AAABTdG9wIENsaWNraW5nDwAAAENsaWNrc1BlclNlY29uZA==");
    private static readonly string[] IocCsharpDotnet =
    Dec("BQAAAAgAAABtc2NvcmxpYhQAAABTeXN0ZW0uV2luZG93cy5Gb3JtcxAAAABTeXN0ZW0uVGhyZWFkaW5nEQAAAFN5c3RlbS5SZWZsZWN0aW9uHgAAAFN5c3RlbS5SdW50aW1lLkludGVyb3BTZXJ2aWNlcw==");
    private static readonly string[] IocCsharpInput =
    Dec("BAAAAAkAAABTZW5kSW5wdXQLAAAAbW91c2VfZXZlbnQMAAAAU2V0Q3Vyc29yUG9zCwAAAGtleWJkX2V2ZW50");
    private static readonly string[] IocCsharpClick =
    Dec("BQAAAAsAAABBdXRvQ2xpY2tlcgwAAABNb3VzZUNsaWNrZXINAAAAQ2xpY2tJbnRlcnZhbA0AAABTdGFydENsaWNraW5nDwAAAENsaWNrc1BlclNlY29uZA==");
    private static readonly string[] IocCheat =
    Dec("hgAAAAkAAABwZW5pcy5kbGwsAAAAWyFdIEdpdGh1YjogaHR0cHM6Ly9naXRodWIuY29tL0pvaG5YaW5hLXNwZWMMAAAALnZhcGVjbGllbnRUKgAAAChKTGNuL2dvdi92YXBlL3V0aWwvanZtdGkvQ2xhc3NMb2FkSG9vazspSRsAAABuZXQuY2NibHVleC5saXF1aWRib3VuY2UuVVQeAAAAbmljay5BdWd1c3R1c0NsYXNzTG9hZGVyLmNsYXNzGQAAAGNvbS5yaXNlY2xpZW50Lk1haW4uY2xhc3MSAAAAc2xpbmt5X2xpYnJhcnkuZGxsJQAAAGFzc2V0cy5taW5lY3JhZnQuaGFydS5pbWcuY2xpY2tndWkuUEspAAAAYXNzZXRzLm1pbmVjcmFmdC5zYWt1cmEuc291bmQud2VsY29tZS5tcDMMAAAAVlJPT01DTElDS0VSCwAAAHd3dy5rb2lkLmVzBwAAAHZhcGUuZ2cLAAAARG9wZUNsaWNrZXITAAAAQ3JhY2tlZCBieSBLYW5nYXJvbxUAAABTYXBwaGlyZSBMSVRFIENsaWNrZXIOAAAAZHJlYW0taW5qZWN0b3IMAAAARXhvZHVzLmNvZGVzCQAAAHNsaW5reS5nZxsAAABbIV0gRmFpbGVkIHRvIGZpbmQgVmFwZSBqYXINAAAAVmFwZSBMYXVuY2hlch8AAABPcGVuIE1pbmVjcmFmdCwgdGhlbiB0cnkgYWdhaW4uCwAAAFBFIEluamVjdG9yDgAAAHN0YXJsaWdodCB2MS4wDQAAAE1vbm9saXRoIExpdGUJAAAAQi5mYWdnMHQwBgAAAEIuZmFnMA4AAABVTklDT1JOIENMSUVOVBkAAABBZGRpbmcgZGVsYXkgdG8gTWluZWNyYWZ0HQAAAHJpZ2h0Q2xpY2tDaGsuQmFja2dyb3VuZEltYWdlCgAAAFV3VSBDbGllbnQRAAAAbGl0aGl1bWNsaWVudC53dGYaAAAAUzN0IDR1dDBDMWljazNyIHQwZ2dMZSBrZXkPAAAAUkVDT1ZFUllDTElDS0VSCwAAAHJhdmVuLmJwbHVzCAAAAFJhdmVuIEIrCQAAAHZhcGUubGl0ZQkAAABWYXBlIExpdGUKAAAAcmlzZWNsaWVudAsAAAB3aGl0ZW91dC5nZwoAAABlbnRyb3B5LmdnBQAAAHh5bm94CgAAAG51bGwgc2Vuc2UFAAAAYXp1cmEJAAAAY2VsZXN0aWFsBQAAAGF4aW9uHgAAAG1ldGVvcmRldmVsb3BtZW50Lm1ldGVvcmNsaWVudBgAAABuZXQuY2NibHVleC5saXF1aWRib3VuY2ULAAAAcmFpb25jbGllbnQNAAAAZGFya2ludGVncml0eQ8AAABuZXQud3Vyc3RjbGllbnQMAAAAaW50ZW50LnN0b3JlDgAAAG1lLnplcm8uYWxwaW5lEAAAAHRvZGF5LmZsdXguYWRkb24IAAAAdmFwZWxpdGUQAAAAb3JnLmxpcXVpZGJvdW5jZRcAAABtZXRlb3JkZXZlbG9wbWVudC5vcmJpdA4AAABvcmcucnVzaGVyaGFjaxAAAABuZXQuZnV0dXJlY2xpZW50FAAAAG1lLnplcm9laWdodHNpeC5rYW1pCwAAAGRldi5sdnN0cm5nCAAAAHd0Zi5vcGFsDAAAAG1lLmVsZG9kZWJ1Zw8AAABjYy51bmtub3duLmhhcnUTAAAAaW8uZ2l0aHViLnJhY29vbmRvZxAAAABtZS5pb25hci5zYWxoYWNrDwAAAG1lLmVhcnRoLnBob2JvcxQAAABjb20uZ2FtZXNlbnNlLmNsaWVudAsAAAB0aHVuZGVyaGFjawoAAAB0b2RheS5vcGFpEAAAAG5ldC5hcG9sbG9jbGllbnQRAAAAZGV2LmZpa2kuZm9yZ2VoYXgUAAAAbWUuamludGhpdW0uZG9wYW1pbmUJAAAAZmRwY2xpZW50DwAAAG5vdm9saW5lLmNsaWVudA8AAAB0ZW5hY2l0eS5jbGllbnQOAAAAYXN0b2xmby5jbGllbnQLAAAAbmV0LmNjYmx1ZXgLAAAAd2hpdGVvdXQuZ2cKAAAAZW50cm9weS5nZwgAAABtZS5rb25hcwkAAABtZS5waG9ib3MHAAAAbWUucHlybwoAAABtZS5lcHNpbG9uCAAAAG1lLnRlYXJzCQAAAG1lLmV4ZXRlcgwAAABtZS5hZnRlcm1hdGgLAAAAbWUud2lubmFibGUHAAAAbWUuaXJpcwgAAABtZS5qZWxsbwgAAAByYWNjb29uNBAAAABuZXQuaW1wYWN0Y2xpZW50DwAAAG5ldC5kcmVhbWNsaWVudAoAAABsYXZhY2xpZW50CgAAAG1ldGhjbGllbnQKAAAAYmx1ZWNsaWVudA0AAABtb25zb29uY2xpZW50CwAAAGF6dXJlY2xpZW50CwAAAGthaWp1Y2xpZW50BgAAAHNpZ21hNQcAAAB2YXBlIHY0BgAAAHZhcGV2NAwAAABkcmVhbS1jbGllbnQNAAAAaW1wYWN0IGNsaWVudAkAAAB3dXJzdHBsdXMNAAAAZnV0dXJlIGNsaWVudAwAAABrb25hcyBjbGllbnQNAAAAcGhvYm9zIGNsaWVudAsAAABweXJvIGNsaWVudBAAAABjb20uZ2l0aHViLnhtb3B4HQAAAG5ldC5jY2JsdWV4LmxpcXVpZGJvdW5jZS5uZXh0DQAAAG5ldGF0aGFuLm5leHQSAAAAbWUueuWbveWcn25vdGZvdW5kDAAAAGRldi5ib2xpY29kZRkAAABjb20ubGxhbWFsYWQ3Lm1peGluZXh0cmFzDAAAAGRldi5pc1hhbmRlchcAAABuZXQuZmFicmljbWMuZmFicmljLWFwaREAAABuZXQuZmFicmljbWMueWFybhIAAABtZS5jb3J0ZXgudnVsa2Ftb2QLAAAAbmV0LmJhZGxpb24KAAAAY29tLmxhbWJkYRQAAABkZXYudHdvdXNlLm9uZWNvbmZpZyAAAABkZXYuaXN4YW5kZXIueWV0YW5vdGhlcmNvbmZpZ2xpYh4AAABtZS5qZWxseXNxdWlkLm5vdGVub3VnaGNyYXNoZXMQAAAAY29tLmxvZ3cubW9kbWVudQwAAAB3dXJzdCBjbGllbnQFAAAAd3Vyc3QFAAAAc2tlZXQJAAAAZ2FtZXNlbnNlCQAAAG5ldmVybG9zZQYAAABvbmV0YXALAAAAbW9vbiBjbGllbnQKAAAAZXhoaWJpdGlvbggAAABzb2xlbm9pZA==");
    private static readonly string[] IocManualMap =
    Dec("BQAAAAkAAABNYW51YWxNYXAOAAAAbWFudWFsIG1hcHBpbmcQAAAAUmVmbGVjdGl2ZUxvYWRlchEAAAByZWZsZWN0aXZlIGxvYWRlcgwAAABMb2FkTGlicmFyeVI=");
    private static readonly string[] IocKdmapper =
    Dec("CAAAAAgAAABrZG1hcHBlcgsAAABpcXZ3NjRlLnN5cxAAAABWdWxuZXJhYmxlRHJpdmVyCgAAAENhcGNvbS5zeXMIAAAAQXNJTy5zeXMKAAAATXNJbzY0LnN5cw4AAABkYnV0aWxfMl8zLnN5cwgAAABnZHJ2LnN5cw==");
    private static readonly string[] IocJavaAgent =
    Dec("IAAAAAwAAABsaXF1aWRib3VuY2UNAAAAaW1wYWN0IGNsaWVudA0AAABtZXRlb3ItY2xpZW50EQAAAG1ldGVvcmRldmVsb3BtZW50BwAAAHJhdmVuIGIHAAAAdmFwZS5nZw0AAABWYXBlIExhdW5jaGVyCwAAAC52YXBlY2xpZW50CgAAAHJpc2VjbGllbnQJAAAAc2xpbmt5LmdnCwAAAHdoaXRlb3V0LmdnCgAAAHJ1c2hlcmhhY2sOAAAAb3JnLnJ1c2hlcmhhY2sQAAAAbmV0LmZ1dHVyZWNsaWVudA0AAABsYW1iZGEtY2xpZW50CwAAAHRodW5kZXJoYWNrDAAAAHd1cnN0LWNsaWVudAkAAABmZHBjbGllbnQIAAAAbm92b2xpbmUIAAAAdGVuYWNpdHkHAAAAYXN0b2xmbwoAAABlbnRyb3B5LmdnCgAAAC1qYXZhYWdlbnQIAAAAbWUua29uYXMJAAAAbWUucGhvYm9zBwAAAG1lLnB5cm8IAAAAcmFjY29vbjQMAAAAaW1wYWN0Y2xpZW50CwAAAGRyZWFtY2xpZW50DQAAAGZ1dHVyZSBjbGllbnQJAAAAd3Vyc3RwbHVzBgAAAHNpZ21hNQ==");
    private static readonly string[] IocDllSideload =
    Dec("BwAAAAsAAAB2ZXJzaW9uLmRsbAoAAABkd21hcGkuZGxsCQAAAHdpbm1tLmRsbAsAAABtc2ltZzMyLmRsbAsAAABkYmdoZWxwLmRsbAsAAAB3aW5odHRwLmRsbA0AAABDUllQVEJBU0UuZGxs");
    private static readonly string[] IocPacker =
    Dec("BwAAAA4AAABTdHJpbmcgQ2xlYW5lcgoAAABDb25mdXNlckV4BwAAAFRoZW1pZGEJAAAAVk1Qcm90ZWN0EAAAAEVuaWdtYSBQcm90ZWN0b3IIAAAALnRoZW1pZGEMAAAAT2JmdXNjYXRlZEJ5");
    private static readonly string[] IocMutex =
    Dec("BQAAAAsAAABHbG9iYWxcVmFwZQ0AAABHbG9iYWxcU2xpbmt5DAAAAEdsb2JhbFxEcmVhbQsAAABjaGVhdC1tdXRleAwAAABsb2FkZXItbXV0ZXg=");
    private static readonly string[] IocNullForked =
    Dec("BAAAAAsAAABOdWxsIGZvcmtlZA8AAABSZWNvdmVyeUNsaWNrZXIPAAAAUkVDT1ZFUllDTElDS0VSCwAAAE51bGxDbGlja2Vy");
    private static readonly string[] IocOverlayAbuse =
    Dec("BAAAAAoAAABkbGwgaW5qZWN0CgAAAG1hbnVhbCBtYXARAAAAcmVmbGVjdGl2ZSBsb2FkZXIIAAAAa2RtYXBwZXI=");
    private static readonly string[] IocDriverLoad =
    Dec("BQAAAAQAAABcXC5cDAAAAE50TG9hZERyaXZlcgwAAABad0xvYWREcml2ZXILAAAAU2VydmljZU5hbWUJAAAASW1hZ2VQYXRo");
    private static readonly string[] IocMemoryModule =
    Dec("BQAAAAwAAABNZW1vcnlNb2R1bGURAAAATWVtb3J5TG9hZExpYnJhcnkUAAAATWVtb3J5R2V0UHJvY0FkZHJlc3MQAAAAUmVmbGVjdGl2ZUxvYWRlcgwAAABMb2FkTGlicmFyeVI=");
    private static readonly string[] IocDebugApis =
    Dec("BAAAABEAAABJc0RlYnVnZ2VyUHJlc2VudBoAAABDaGVja1JlbW90ZURlYnVnZ2VyUHJlc2VudBkAAABOdFF1ZXJ5SW5mb3JtYXRpb25Qcm9jZXNzEQAAAE91dHB1dERlYnVnU3RyaW5n");
    private static readonly string[] IocVirt =
    Dec("CgAAAAYAAABWTXdhcmUKAAAAVmlydHVhbEJveAQAAABxZW11CgAAAGh5cGVydmlzb3IIAAAAdm10b29sc2QJAAAAVkJveEd1ZXN0CQAAAHNhbmRib3hpZQQAAAB3aW5lAwAAAGt2bQMAAAB4ZW4=");
    private static readonly string[] IocCheatName =
    Dec("QAAAAAgAAABrZG1hcHBlcgwAAABjaGVhdC1lbmdpbmULAAAAY2hlYXRlbmdpbmUJAAAAZGxsaW5qZWN0DAAAAGxpcXVpZGJvdW5jZQ0AAABtZXRlb3ItY2xpZW50BQAAAHJhaW9uBwAAAHZhcGUuZ2cGAAAAXHZhcGVcCwAAAGF1dG9jbGlja2VyDAAAAHd1cnN0LWNsaWVudA0AAABkYXJraW50ZWdyaXR5DAAAAG1lbW9yeW1vZHVsZQgAAABnZHJ2LnN5cwoAAABkYnV0aWxfMl8zCgAAAGNhcGNvbS5zeXMJAAAAc2xpbmt5LmdnCAAAAFxzbGlua3lcCgAAAHJhdmVuYnBsdXMKAAAAcmlzZWNsaWVudAgAAAB2YXBlbGl0ZQwAAABpbnRlbnQuc3RvcmUKAAAAcnVzaGVyaGFjawsAAAB0aHVuZGVyaGFjawwAAABmdXR1cmVjbGllbnQHAAAAXHd1cnN0XAsAAAB3aGl0ZW91dC5nZwoAAABlbnRyb3B5LmdnDAAAAGxpcXVpZGJvdW5jZQ0AAABtZXRlb3ItY2xpZW50CQAAAGZkcGNsaWVudAoAAABcbm92b2xpbmVcCAAAAHRlbmFjaXR5CAAAAHZhcGVsaXRlCAAAAHJhY2Nvb240DAAAAGltcGFjdGNsaWVudAUAAABrb25hcwYAAABwaG9ib3MEAAAAcHlybwYAAABzaWdtYTULAAAAZHJlYW1jbGllbnQGAAAAdmFwZXY0CgAAAGxhdmFjbGllbnQKAAAAbWV0aGNsaWVudAsAAABhenVyZWNsaWVudAUAAABrYWlqdQcAAABtb25zb29uBQAAAHRlYXJzBgAAAGV4ZXRlcggAAAB3aW5uYWJsZQkAAABhZnRlcm1hdGgHAAAAZXBzaWxvbhMAAAB5ZXRhbm90aGVyY29uZmlnbGliBwAAAGJhZGxpb24HAAAAbGl0ZW1vZAgAAABvcHRpZmluZQcAAABsYWJ5bW9kBQAAAGZvcmdlDQAAAGZhYnJpYy1sb2FkZXIMAAAAcXVpbHQtbG9hZGVyBgAAAHNvZGl1bQwAAABpcmlzLXNoYWRlcnMHAAAAbGl0aGl1bQkAAABzdGFybGlnaHQ=");
    private static readonly string[] IocEncodedBrands =
    Dec("DgAAAAgAAABrZG1hcHBlcgwAAABsaXF1aWRib3VuY2UNAAAAbWV0ZW9yLWNsaWVudAsAAABhdXRvY2xpY2tlcgkAAABtYW51YWxtYXARAAAAcmVmbGVjdGl2ZSBsb2FkZXIMAAAAY2hlYXQtZW5naW5lCQAAAHZtcHJvdGVjdAcAAAB0aGVtaWRhBwAAAHZhcGUuZ2cJAAAAc2xpbmt5LmdnDAAAAGxpcXVpZGJvdW5jZQoAAABydXNoZXJoYWNrCwAAAHRodW5kZXJoYWNr");
    private static readonly string[] IocAimbotVocab =
    Dec("CQAAAAsAAABsb29rIHZlY3RvcgYAAABhaW1ib3QKAAAAYWltIGFzc2lzdAoAAABzaWxlbnQgYWltCQAAAGFpbWFzc2lzdAoAAAB0cmlnZ2VyYm90CwAAAHRyaWdnZXIgYm90CgAAAHNtb290aCBhaW0IAAAAc25hcCBhaW0=");
    private static readonly string[] IocNtdllUnhook =
    Dec("CAAAAAsAAAB1bmhvb2tudGRsbA4AAABkaXJlY3Qgc3lzY2FsbA0AAABkaXJlY3RzeXNjYWxsCwAAAGhlbGwncyBnYXRlCgAAAGhlbGxzIGdhdGULAAAAaGFsbydzIGdhdGUMAAAAc3lzY2FsbCBzdHViCwAAAHJldC1zeXNjYWxs");

    
    private static readonly string[] IocRenderApis =
    Dec("CQAAAA8AAABEaXJlY3QzRENyZWF0ZTkRAAAARDNEMTFDcmVhdGVEZXZpY2UNAAAAZ2xTd2FwQnVmZmVycw4AAABnbERyYXdFbGVtZW50cwwAAABnbERyYXdBcnJheXMOAAAAd2dsU3dhcEJ1ZmZlcnMKAAAARDNEOUNyZWF0ZQgAAABkM2Q5LmRsbAkAAABkM2QxMS5kbGw=");
    private static readonly string[] IocRenderHooks =
    Dec("DAAAAAgAAABFbmRTY2VuZQgAAABlbmRzY2VuZQoAAABoa0VuZFNjZW5lCQAAAGhrUHJlc2VudAcAAABQcmVzZW50FAAAAERyYXdJbmRleGVkUHJpbWl0aXZlCAAAAGhvb2sgZDNkCwAAAGhvb2sgb3BlbmdsDQAAAGNyZWF0ZSBkZXZpY2ULAAAAZGV2aWNlIGhvb2sLAAAAcmVuZGVyIGhvb2sPAAAAT3ZlcmxheVJlbmRlcmVy");

    
    
    private static readonly string[] IocMemoryScanVocab =
    Dec("CgAAAAsAAABmaW5kcGF0dGVybgwAAABmaW5kIHBhdHRlcm4MAAAAcGF0dGVybiBzY2FuCAAAAGFvYiBzY2FuBwAAAGFvYnNjYW4OAAAAc2lnbmF0dXJlIHNjYW4MAAAAYnl0ZSBwYXR0ZXJuCAAAAHNpZyBzY2FuCwAAAG1lbW9yeSBzY2FuCwAAAHNjYW4gcmVnaW9u");

    
    
    private static readonly string[] IocStartupVocab =
    Dec("CgAAABIAAABDdXJyZW50VmVyc2lvblxSdW4HAAAAUnVuT25jZQgAAABzY2h0YXNrcw4AAABUYXNrIFNjaGVkdWxlcg8AAABTdGFydHVwQXBwcm92ZWQMAAAAZXhwbG9yZXIuZXhlBAAAAEhLQ1UEAAAASEtMTQ4AAABzdGFydHVwIGZvbGRlcgkAAABhdXRvc3RhcnQ=");

    
    
    
    private static readonly string[] IocMcPacketClasses =
    Dec("CgAAAA8AAABDMDNQYWNrZXRQbGF5ZXIWAAAAQzA3UGFja2V0UGxheWVyRGlnZ2luZx0AAABDMDhQYWNrZXRQbGF5ZXJCbG9ja1BsYWNlbWVudBQAAABDMEVQYWNrZXRDbGlja1dpbmRvdxIAAABDMDJQYWNrZXRVc2VFbnRpdHkSAAAAQzAwUGFja2V0S2VlcEFsaXZlFQAAAFNQYWNrZXRFbnRpdHlNZXRhZGF0YRAAAABTUGFja2V0UGFydGljbGVzIQAAAG5ldC5taW5lY3JhZnQubmV0d29yay5wbGF5LmNsaWVudCEAAABuZXQubWluZWNyYWZ0Lm5ldHdvcmsucGxheS5zZXJ2ZXI=");
    private static readonly string[] IocMcHackVocab =
    Dec("GgAAAAgAAABraWxsYXVyYQYAAABhaW1ib3QLAAAAYXV0b2NsaWNrZXIIAAAAdmVsb2NpdHkIAAAAc2NhZmZvbGQDAAAAZmx5BQAAAHRpbWVyBwAAAGNsaWNrZXIFAAAAcmVhY2gGAAAAaGl0Ym94BgAAAGFudGlrYgYAAABub3Nsb3cGAAAAc3RyYWZlBAAAAGJob3ALAAAAY3J5c3RhbGF1cmEIAAAAc3Vycm91bmQIAAAAaG9sZWZpbGwLAAAAYXV0b2NyeXN0YWwJAAAAY3JpdGljYWxzCgAAAHRyaWdnZXJib3QLAAAAdHJpZ2dlciBib3QKAAAAYWltIGFzc2lzdAkAAABhaW1hc3Npc3QHAAAAYW50aWJvdAYAAABub2ZhbGwHAAAAdHJhY2Vycw==");

    

    
    
    
    private static readonly string[] IocNetworkC2 =
    Dec("FwAAAAYAAAB3c3M6Ly8FAAAAd3M6Ly8MAAAAaHR0cHM6Ly9jZG4uCwAAAGh0dHA6Ly9jZG4uDQAAAC9kb3dubG9hZC5leGULAAAAL3VwZGF0ZS5leGULAAAAL2xvYWRlci5leGUJAAAAL2dhdGUucGhwCQAAAC9hdXRoLnBocAwAAAAvYXBpL3YxL2V4ZWMQAAAAVXNlci1BZ2VudDogY3VybBAAAABVc2VyLUFnZW50OiB3Z2V0CwAAAFgtQ2hlYXQtS2V5DAAAAFgtQXV0aC1Ub2tlbhUAAABBdXRob3JpemF0aW9uOiBCZWFyZXIIAAAAL3dlYmhvb2sYAAAAZGlzY29yZC5jb20vYXBpL3dlYmhvb2tzEgAAAC9hcGkvdjEvYXV0aC9sb2dpbhEAAAAvYXBpL3YxL3VzZXIvaW5mbxkAAAByYXcuZ2l0aHVidXNlcmNvbnRlbnQuY29tEAAAAHBhc3RlYmluLmNvbS9yYXcMAAAAL2V4ZWM/dG9rZW49CwAAAC9ydW4/dG9rZW49");

    
    
    private static readonly string[] IocRegistryPersist =
    Dec("CQAAABIAAABDdXJyZW50VmVyc2lvblxSdW4WAAAAQ3VycmVudFZlcnNpb25cUnVuT25jZRoAAABDdXJyZW50VmVyc2lvblxSdW5TZXJ2aWNlcx4AAABDdXJyZW50VmVyc2lvblxSdW5TZXJ2aWNlc09uY2UlAAAAQ3VycmVudFZlcnNpb25cRXhwbG9yZXJcU2hlbGwgRm9sZGVycyoAAABDdXJyZW50VmVyc2lvblxFeHBsb3JlclxVc2VyIFNoZWxsIEZvbGRlcnMvAAAAV2luZG93c1xDdXJyZW50VmVyc2lvblxFeHBsb3JlclxTdGFydHVwQXBwcm92ZWQ2AAAATWljcm9zb2Z0XFdpbmRvd3NcQ3VycmVudFZlcnNpb25cUG9saWNpZXNcRXhwbG9yZXJcUnVuNgAAAE1pY3Jvc29mdFxXaW5kb3dzXEN1cnJlbnRWZXJzaW9uXFBvbGljaWVzXFN5c3RlbVxTaGVsbA==");

    
    
    private static readonly string[] IocDllSideloadVocab =
    Dec("FAAAAAsAAAB2ZXJzaW9uLmRsbAoAAABkd21hcGkuZGxsCQAAAHdpbm1tLmRsbAsAAABtc2ltZzMyLmRsbAsAAABkYmdoZWxwLmRsbAsAAAB3aW5odHRwLmRsbA0AAABDUllQVEJBU0UuZGxsDAAAAFdUU0FQSTMyLmRsbAsAAABwcm9wc3lzLmRsbAwAAABzaGZvbGRlci5kbGwMAAAAbXNzaWduMzIuZGxsCwAAAG1pZGltYXAuZGxsCwAAAHdtdmNvcmUuZGxsCQAAAHdtYXNmLmRsbAYAAABtZi5kbGwKAAAAbWZwbGF0LmRsbAoAAABkbHBhcGkuZGxsCwAAAHByb2ZhcGkuZGxsBwAAAHdlci5kbGwMAAAAZmF1bHRyZXAuZGxs");

    
    
    private static readonly string[] IocWmiPersist =
    Dec("DgAAAA0AAABfX0V2ZW50RmlsdGVyDwAAAF9fRXZlbnRDb25zdW1lchgAAABDb21tYW5kTGluZUV2ZW50Q29uc3VtZXIZAAAAX19GaWx0ZXJUb0NvbnN1bWVyQmluZGluZxkAAABBY3RpdmVTY3JpcHRFdmVudENvbnN1bWVyDwAAAFdpbjMyX0xvY2FsVGltZRIAAABXaW4zMl9Mb2dvblNlc3Npb24lAAAAU0VMRUNUICogRlJPTSBfX0luc3RhbmNlQ3JlYXRpb25FdmVudCkAAABTRUxFQ1QgKiBGUk9NIF9fSW5zdGFuY2VNb2RpZmljYXRpb25FdmVudA8AAAB3bWljIC9uYW1lc3BhY2UYAAAAd21pYyBwcm9jZXNzIGNhbGwgY3JlYXRlDQAAAEdldC1XbWlPYmplY3QPAAAAU2V0LVdtaUluc3RhbmNlEQAAAFJlZ2lzdGVyLVdtaUV2ZW50");

    
    
    
    private static readonly string[] IocProcessDoppelganging =
    Dec("CgAAABkAAABOdENyZWF0ZVRyYW5zYWN0ZWRTZWN0aW9uFQAAAE50Um9sbGJhY2tUcmFuc2FjdGlvbhMAAABOdENvbW1pdFRyYW5zYWN0aW9uFAAAAFJ0bENyZWF0ZVRyYW5zYWN0aW9uDwAAAE50Q3JlYXRlU2VjdGlvbhIAAABOdE1hcFZpZXdPZlNlY3Rpb24UAAAATnRVbm1hcFZpZXdPZlNlY3Rpb24WAAAATnRTZXRJbmZvcm1hdGlvblRocmVhZA4AAABOdFJlc3VtZVRocmVhZBIAAABOdEdldENvbnRleHRUaHJlYWQ=");

    
    
    private static readonly string[] IocDotnetCheat =
    Dec("GAAAAAsAAABDaGVhdEVuZ2luZQsAAABHYW1lT3ZlcmxheQcAAABTaGFycERYDQAAAE92ZXJsYXlEb3ROZXQPAAAAR2FtZU92ZXJsYXkuTmV0CAAAAEVhc3lIb29rCQAAAFNoYXJwSG9vawcAAABNaW5Ib29rBgAAAERldG91cgcAAABNb25vTW9kCgAAAEhhcm1vbnlMaWIOAAAASW50ZXJuYWxnaW5nZXINAAAATXV4dGFwb3NpdGlvbgUAAABkbmxpYgUAAABjZWNpbBYAAABTeXN0ZW0uUmVmbGVjdGlvbi5FbWl0DQAAAEFzc2VtYmx5LkxvYWQXAAAAQXBwRG9tYWluLkN1cnJlbnREb21haW4lAAAATWFyc2hhbC5HZXREZWxlZ2F0ZUZvckZ1bmN0aW9uUG9pbnRlcgwAAABWaXJ0dWFsQWxsb2MSAAAAV3JpdGVQcm9jZXNzTWVtb3J5BQAAAG1ob29rCAAAAGVhc3lob29rCQAAAGRldmlydHVhbA==");

    

    
    
    
    
    private static readonly string[] IocScreenshareEvasion =
    Dec("DgAAAAkAAABwYW5pYyBrZXkKAAAAcGFuaWMgYmluZAwAAABwYW5pYyBidXR0b24KAAAAcGFuaWMgbW9kZRIAAABzY3JlZW5zaGFyZSBieXBhc3MRAAAAc2NyZWVuc2hhcmUgY2hlY2sRAAAAc2NyZWVuc2hhcmUgY2xlYW4JAAAAc3MgYnlwYXNzCAAAAHNzIGNoZWNrDAAAAHZlcmlmeSBjbGVhbhMAAABoaWRlIG9uIHNjcmVlbnNoYXJlFAAAAGNsb3NlIG9uIHNjcmVlbnNoYXJlFAAAAGNsZWFuIG9uIHNjcmVlbnNoYXJlFgAAAGRpc2FibGUgb24gc2NyZWVuc2hhcmU=");

    
    
    
    private static readonly string[] IocKeystroke =
    Dec("CQAAAAYAAABrZXlsb2cKAAAAa2V5IGxvZ2dlcgcAAABrZXkgbG9nCgAAAGtleWxvZ2dpbmcIAAAAbG9nIGtleXMOAAAAbG9nIGtleXN0cm9rZXMNAAAAa2V5c3Ryb2tlIGxvZw8AAABwYXNzd29yZCBsb2dnZXIKAAAAa2V5bG9nLnR4dA==");

    
    
    
    private static readonly string[] IocCaptureEvasion =
    Dec("BwAAABQAAABleGNsdWRlIGZyb20gY2FwdHVyZREAAABoaWRlIGZyb20gY2FwdHVyZQ4AAABjYXB0dXJlIGJ5cGFzcw8AAABhbnRpIHNjcmVlbnNob3QPAAAAYW50aS1zY3JlZW5zaG90BgAAAGFudGlzcxUAAABzY3JlZW4gY2FwdHVyZSBieXBhc3M=");

    
    
    
    private static readonly string[] IocMcEsp =
    Dec("DwAAAAcAAAB0cmFjZXJzCAAAAG5hbWV0YWdzCAAAAHdhbGxoYWNrCQAAAHdhbGwgaGFjawUAAABjaGFtcwQAAAB4cmF5CgAAAGZ1bGxicmlnaHQIAAAAbm9yZWNvaWwJAAAAbm8gcmVjb2lsBgAAAG5vZmFsbAcAAABubyBmYWxsCgAAAHBsYXllciBlc3AJAAAAY2hlc3QgZXNwCAAAAGl0ZW0gZXNwBwAAAG1vYiBlc3A=");

    
    
    
    
    private static readonly string[] IocVeh =
    Dec("CAAAABIAAAB2ZWN0b3JlZCBleGNlcHRpb24QAAAAdmVjdG9yZWQgaGFuZGxlchMAAABoYXJkd2FyZSBicmVha3BvaW50BAAAAGh3YnALAAAAc2luZ2xlIHN0ZXAQAAAAc2V0dGhyZWFkY29udGV4dBAAAABnZXR0aHJlYWRjb250ZXh0EQAAAGV4Y2VwdGlvbiBoYW5kbGVy");

    
    
    
    private static readonly string[] IocPowerShell =
    Dec("CwAAAA8AAABwb3dlcnNoZWxsIC1lbmMTAAAAcG93ZXJzaGVsbC5leGUgLWVuYxMAAAAtd2luZG93c3R5bGUgaGlkZGVuEQAAAGludm9rZS1leHByZXNzaW9uBAAAAGlleCgQAAAAZnJvbWJhc2U2NHN0cmluZw4AAABkb3dubG9hZHN0cmluZwwAAABkb3dubG9hZGZpbGUYAAAAbmV3LW9iamVjdCBuZXQud2ViY2xpZW50EAAAAGNlcnR1dGlsIC1kZWNvZGUOAAAALW5vcCAtdyBoaWRkZW4=");

    
    
    
    private static readonly string[] IocAmsiEtw =
    Dec("CAAAAAsAAABhbXNpIGJ5cGFzcwoAAABhbXNpIHBhdGNoDQAAAGFtc3NjYW5idWZmZXIJAAAAZXR3IHBhdGNoCgAAAGV0dyBieXBhc3MKAAAAcGF0Y2ggYW1zaQkAAABwYXRjaCBldHcJAAAAYW1zaWRlYnVn");

    
    private static readonly HashSet<string> KnownCheatHashes = LoadCheatHashes();

    private static HashSet<string> LoadCheatHashes()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string dir = AppContext.BaseDirectory;
            string path = Path.Combine(dir, "cheat-hashes.txt");
            if (!File.Exists(path))
                path = Path.Combine(dir, "Tools", "cheat-hashes.txt");
            if (!File.Exists(path)) return set;

            foreach (string line in File.ReadAllLines(path))
            {
                string t = line.Trim();
                if (t.Length == 64 && t.All(c => Uri.IsHexDigit(c)))
                    set.Add(t);
            }
        }
        catch
        {
            
        }
        return set;
    }

    
    
    
    
    
    

    
    private static readonly Dictionary<string, int[]> NeedleToPatterns = new(StringComparer.Ordinal);

    private static readonly Lazy<AcAutomaton> Automaton = new(
        BuildAutomaton, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly string[] ExtraPlainNeedles =
    {
        S("RGlzY29yZEhvb2s="), "GameOverlayRenderer",
        
        "UPX0", "UPX1"
    };

    private static int PatternCount;

    private static AcAutomaton BuildAutomaton()
    {
        var ac = new AcAutomaton();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddAll(string[] needles)
        {
            foreach (var raw in needles)
            {
                string folded = FoldNeedle(raw);
                if (folded.Length == 0 || !seen.Add(folded))
                    continue;
                NeedleToPatterns[folded] = RegisterPatterns(ac, folded);
            }
        }

        AddAll(IocAutoclicker);
        AddAll(IocCsharpDotnet);
        AddAll(IocCsharpInput);
        AddAll(IocCsharpClick);
        AddAll(IocCheat);
        AddAll(IocManualMap);
        AddAll(IocKdmapper);
        AddAll(IocJavaAgent);
        AddAll(IocDllSideload);
        AddAll(IocPacker);
        AddAll(IocMutex);
        AddAll(IocNullForked);
        AddAll(IocOverlayAbuse);
        AddAll(IocDriverLoad);
        AddAll(IocMemoryModule);
        AddAll(IocDebugApis);
        AddAll(IocVirt);
        AddAll(IocCheatName);
        AddAll(IocAimbotVocab);
        AddAll(IocNtdllUnhook);
        AddAll(IocRenderApis);
        AddAll(IocRenderHooks);
        AddAll(IocMemoryScanVocab);
        AddAll(IocStartupVocab);
        AddAll(IocMcPacketClasses);
        AddAll(IocMcHackVocab);
        AddAll(IocNetworkC2);
        AddAll(IocRegistryPersist);
        AddAll(IocDllSideloadVocab);
        AddAll(IocWmiPersist);
        AddAll(IocProcessDoppelganging);
        AddAll(IocDotnetCheat);
        AddAll(IocScreenshareEvasion);
        AddAll(IocKeystroke);
        AddAll(IocCaptureEvasion);
        AddAll(IocMcEsp);
        AddAll(IocVeh);
        AddAll(IocPowerShell);
        AddAll(IocAmsiEtw);
        AddAll(ExtraPlainNeedles);

        
        foreach (var brand in IocEncodedBrands)
        {
            foreach (string variant in EncodedVariants(FoldNeedle(brand)))
            {
                string folded = FoldNeedle(variant);
                if (folded.Length == 0 || !seen.Add(folded))
                    continue;
                NeedleToPatterns[folded] = RegisterPatterns(ac, folded);
            }
        }

        ac.Build();
        return ac;
    }

    private static int[] RegisterPatterns(AcAutomaton ac, string folded)
    {
        int asciiId = ac.AddNeedle(folded, isWide: false);
        int wideEvenId = ac.AddNeedle(folded, isWide: true, oddAlignment: false);
        int wideOddId = ac.AddNeedle(folded, isWide: true, oddAlignment: true);
        return new[] { asciiId, wideEvenId, wideOddId };
    }

    private static string FoldNeedle(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(c is >= 'A' and <= 'Z' ? (char)(c + 32) : c);
        return sb.ToString();
    }

    private static byte FoldByte(byte c)
        => c is >= (byte)'A' and <= (byte)'Z' ? (byte)(c + 32) : c;

    
    
    
    
    
    private sealed class AcAutomaton
    {
        private const int Alphabet = 256;
        private readonly List<int> _go = new();
        private readonly List<int> _fail = new();
        private readonly List<List<int>?> _output = new();

        public int StateCount => _fail.Count;

        public AcAutomaton()
        {
            NewNode();
        }

        private int NewNode()
        {
            _go.AddRange(Enumerable.Repeat(-1, Alphabet));
            _fail.Add(0);
            _output.Add(null);
            return _go.Count / Alphabet - 1;
        }

        
        public int AddNeedle(string folded, bool isWide, bool oddAlignment = false)
        {
            int state = 0;
            if (isWide)
            {
                
                
                if (oddAlignment)
                    state = GoStep(state, 0);
                foreach (char c in folded)
                {
                    state = GoStep(state, FoldByte((byte)c));
                    state = GoStep(state, 0);
                }
            }
            else
            {
                foreach (char c in folded)
                    state = GoStep(state, FoldByte((byte)c));
            }

            int id = PatternCount++;
            (_output[state] ??= new List<int>(1)).Add(id);
            return id;
        }

        private int GoStep(int state, int b)
        {
            int idx = state * Alphabet + b;
            int next = _go[idx];
            if (next >= 0)
                return next;
            _go[idx] = _go.Count / Alphabet;
            NewNode();
            return _go[idx];
        }

        public void Build()
        {
            var queue = new Queue<int>();
            for (int b = 0; b < Alphabet; b++)
            {
                int next = _go[b];
                if (next > 0)
                    queue.Enqueue(next);
                else
                    _go[b] = 0;
            }

            while (queue.Count > 0)
            {
                int state = queue.Dequeue();
                int fail = _fail[state];
                for (int b = 0; b < Alphabet; b++)
                {
                    int next = _go[state * Alphabet + b];
                    if (next > 0)
                    {
                        _fail[next] = _go[fail * Alphabet + b];
                        var outList = _output[_fail[next]];
                        if (outList is { Count: > 0 })
                        {
                            var mine = _output[next] ??= new List<int>(outList.Count);
                            mine.AddRange(outList);
                        }
                        queue.Enqueue(next);
                    }
                    else
                    {
                        _go[state * Alphabet + b] = _go[fail * Alphabet + b];
                    }
                }
            }
        }

        
        
        
        
        
        public void Scan(ReadOnlySpan<byte> data, bool[] matched, int[] lastVisit)
        {
            int state = 0;
            int n = data.Length;
            for (int i = 0; i < n; i++)
            {
                int token = i + 1;
                byte b = FoldByte(data[i]);
                state = _go[state * Alphabet + b];

                int t = state;
                while (t != 0 && lastVisit[t] != token)
                {
                    lastVisit[t] = token;
                    var outList = _output[t];
                    if (outList is { Count: > 0 })
                    {
                        foreach (int id in outList)
                            matched[id] = true;
                    }
                    t = _fail[t];
                }
            }
        }
    }

    private sealed class MatchEngine
    {
        private readonly bool[] _matched;
        private readonly int[] _lastVisit;
        private readonly PeAnalyzer? _pe;
        private readonly string? _path;

        public MatchEngine(byte[] data, PeAnalyzer? pe, string? path = null)
        {
            int scanLen = Math.Min(data.Length, StringScanLimit);
            _pe = pe;
            _path = path;
            var ac = Automaton.Value;
            _matched = new bool[PatternCount];
            _lastVisit = new int[ac.StateCount];
            ac.Scan(data.AsSpan(0, scanLen), _matched, _lastVisit);
        }

        public PeAnalyzer? Pe => _pe;
        public string? Path => _path;

        
        
        
        
        
        
        public void ScanMore(ReadOnlySpan<byte> span)
        {
            Array.Clear(_lastVisit, 0, _lastVisit.Length);
            Automaton.Value.Scan(span, _matched, _lastVisit);
        }

        public bool Present(string s, bool nocase)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            if (!NeedleToPatterns.TryGetValue(FoldNeedle(s), out var ids))
                return false;
            return _matched[ids[0]] || _matched[ids[1]] || _matched[ids[2]];
        }

        public bool PresentEncoded(string s)
        {
            if (s.Length < 8)
                return false;
            string needle = FoldNeedle(s);
            foreach (string variant in EncodedVariants(needle))
            {
                if (variant.Length >= 8 && Present(variant, nocase: true))
                    return true;
            }
            return false;
        }

        public int CountPresent(string[] strings, bool nocase)
        {
            int n = 0;
            foreach (var s in strings)
                if (Present(s, nocase)) n++;
            return n;
        }
    }

    
    private static IEnumerable<string> EncodedVariants(string lowerNeedle)
    {
        if (lowerNeedle.Length < 3) yield break;

        byte[] raw = Encoding.UTF8.GetBytes(lowerNeedle);
        string b64 = Convert.ToBase64String(raw);
        yield return b64;
        yield return b64.TrimEnd('=');
        yield return b64.Replace('+', '-').Replace('/', '_');          
        yield return b64.Replace('+', '-').Replace('/', '_').TrimEnd('=');

        yield return Rot13(lowerNeedle);
    }

    private static string Rot13(string s)
    {
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c >= 'a' && c <= 'z')
                chars[i] = (char)('a' + (c - 'a' + 13) % 26);
        }
        return new string(chars);
    }

    private static bool Imports(PeAnalyzer? pe, string dll, string func)
        => pe != null && pe.HasImport(dll, func);

    private const uint CNT_CODE = 0x00000020;
    private const uint MEM_EXECUTE = 0x20000000;
    private const uint MEM_WRITE = 0x80000000;

    private static bool HasExecutableSection(PeAnalyzer pe)
        => pe.Sections.Any(s =>
            (s.Characteristics & CNT_CODE) != 0 &&
            (s.Characteristics & MEM_EXECUTE) != 0);

    
    
    

    private static bool R_AUTOCLICKER(MatchEngine m)
    {
        return m.CountPresent(IocAutoclicker, true) >= 2;
    }

    private static bool R_CLICK_INPUT_COMBO(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null) return false;
        if (!Imports(pe, DllUser32, S("bW91c2VfZXZlbnQ="))) return false;
        if (!Imports(pe, DllUser32, S("R2V0QXN5bmNLZXlTdGF0ZQ=="))) return false;
        if (!Imports(pe, DllKernel32, S("U2xlZXA="))) return false;
        return Imports(pe, DllUser32, S("a2V5YmRfZXZlbnQ=")) ||
               Imports(pe, DllUser32, S("U2VuZElucHV0")) ||
               Imports(pe, DllUser32, S("U2V0Q3Vyc29yUG9z"));
    }

    private static bool R_CSHARP_CLICKER(MatchEngine m)
    {
        return m.CountPresent(IocCsharpDotnet, true) >= 2 &&
               m.CountPresent(IocCsharpInput, true) >= 1 &&
               m.CountPresent(IocCsharpClick, true) >= 1;
    }

    private static bool R_CHEAT(MatchEngine m)
    {
        foreach (var s in IocCheat)
            if (m.Present(s, true)) return true;
        return false;
    }

    private static bool R_HIGH_ENTROPY_UNSIGNED_PE(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || pe.HasEmbeddedSignature) return false;
        long fs = pe.FileSize;
        if (fs <= 80000 || fs >= 12000000) return false;
        return pe.FileEntropy() > 7.25;
    }

    private static bool R_HIGH_ENTROPY_SECTION(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || pe.HasEmbeddedSignature) return false;
        foreach (var s in pe.Sections)
        {
            if (s.RawDataSize > 4096 &&
                pe.Entropy(s.RawDataOffset, s.RawDataSize) > 7.2)
                return true;
        }
        return false;
    }

    private static bool R_INJECTOR_API(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null) return false;
        if (!Imports(pe, DllKernel32, S("V3JpdGVQcm9jZXNzTWVtb3J5"))) return false;
        if (!Imports(pe, DllKernel32, S("VmlydHVhbEFsbG9jRXg="))) return false;
        return Imports(pe, DllKernel32, S("Q3JlYXRlUmVtb3RlVGhyZWFk")) ||
               Imports(pe, DllKernel32, S("TnRDcmVhdGVUaHJlYWRFeA==")) ||
               Imports(pe, DllNtdll, S("TnRDcmVhdGVUaHJlYWRFeA=="));
    }

    private static bool R_MANUAL_MAP_HINTS(MatchEngine m)
        => m.CountPresent(IocManualMap, true) >= 2;

    private static bool R_KDMAPPER_LIKE(MatchEngine m)
    {
        foreach (var x in IocKdmapper)
            if (m.Present(x, true)) return true;
        return false;
    }

    private static bool R_JAVA_AGENT_CHEAT(MatchEngine m)
    {
        foreach (var x in IocJavaAgent)
            if (m.Present(x, true)) return true;
        return false;
    }

    private static bool R_DLL_SIDELOAD_NAMES(MatchEngine m)
        => m.CountPresent(IocDllSideload, true) >= 5;

    private static bool R_STRING_CLEANER_PACKER(MatchEngine m)
    {
        var pe = m.Pe;
        foreach (var x in IocPacker)
            if (m.Present(x, true)) return true;
        if (pe != null && !pe.HasEmbeddedSignature &&
            pe.HasSectionNamed("UPX0") && pe.HasSectionNamed("UPX1"))
            return true;
        return false;
    }

    private static bool R_SUSPICIOUS_MUTEX(MatchEngine m)
    {
        foreach (var x in IocMutex)
            if (m.Present(x, true)) return true;
        return false;
    }

    private static bool R_NULL_FORKED_RECOVERY(MatchEngine m)
    {
        foreach (var x in IocNullForked)
            if (m.Present(x, true)) return true;
        return false;
    }

    private static bool R_PE_INJECT_COMBO(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null) return false;
        if (!Imports(pe, DllKernel32, S("T3BlblByb2Nlc3M="))) return false;
        if (!Imports(pe, DllKernel32, S("V3JpdGVQcm9jZXNzTWVtb3J5"))) return false;
        if (!Imports(pe, DllKernel32, S("UmVhZFByb2Nlc3NNZW1vcnk="))) return false;
        return Imports(pe, DllKernel32, S("VmlydHVhbEFsbG9jRXg=")) ||
               Imports(pe, DllKernel32, S("VmlydHVhbFByb3RlY3RFeA=="));
    }

    private static bool R_PE_DEBUG_ANTI(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null) return false;
        bool anti = Imports(pe, DllKernel32, S("SXNEZWJ1Z2dlclByZXNlbnQ=")) ||
                    Imports(pe, DllKernel32, S("Q2hlY2tSZW1vdGVEZWJ1Z2dlclByZXNlbnQ=")) ||
                    Imports(pe, DllNtdll, S("TnRRdWVyeUluZm9ybWF0aW9uUHJvY2Vzcw=="));
        bool abusing = Imports(pe, DllKernel32, S("V3JpdGVQcm9jZXNzTWVtb3J5")) ||
                       Imports(pe, DllKernel32, S("Q3JlYXRlUmVtb3RlVGhyZWFk"));
        return anti && abusing;
    }

    private static bool R_PE_HIGH_ENTROPY_NO_SIG_HINT(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || pe.HasEmbeddedSignature) return false;
        long fs = pe.FileSize;
        if (fs <= 50000 || fs >= 15000000) return false;
        return pe.FileEntropy() > 7.2;
    }

    private static bool R_PE_RWX_SECTION(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null) return false;
        foreach (var s in pe.Sections)
        {
            if ((s.Characteristics & CNT_CODE) != 0 &&
                (s.Characteristics & MEM_EXECUTE) != 0 &&
                (s.Characteristics & MEM_WRITE) != 0)
                return true;
        }
        return false;
    }

    private static bool R_GAME_OVERLAY_ABUSE(MatchEngine m)
    {
        bool overlay = m.Present(S("RGlzY29yZEhvb2s="), true) || m.Present("GameOverlayRenderer", true);
        if (!overlay) return false;
        foreach (var x in IocOverlayAbuse)
            if (m.Present(x, true)) return true;
        return false;
    }

    private static bool R_NTDLL_UNDOCUMENTED(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null) return false;
        bool undocumented = Imports(pe, DllNtdll, S("TnRXcml0ZVZpcnR1YWxNZW1vcnk=")) ||
                            Imports(pe, DllNtdll, S("TnRNYXBWaWV3T2ZTZWN0aW9u")) ||
                            Imports(pe, DllNtdll, S("TnRDcmVhdGVUaHJlYWRFeA==")) ||
                            Imports(pe, DllNtdll, S("TnRQcm90ZWN0VmlydHVhbE1lbW9yeQ==")) ||
                            Imports(pe, DllNtdll, S("TnRRdWV1ZUFwY1RocmVhZA=="));
        bool open = Imports(pe, DllNtdll, S("TnRPcGVuUHJvY2Vzcw==")) ||
                    Imports(pe, DllKernel32, S("T3BlblByb2Nlc3M="));
        return undocumented && open;
    }

    private static bool R_THREAD_HIJACK_HINTS(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null) return false;
        if (!Imports(pe, DllKernel32, S("U3VzcGVuZFRocmVhZA=="))) return false;
        if (!Imports(pe, DllKernel32, S("U2V0VGhyZWFkQ29udGV4dA=="))) return false;
        if (!Imports(pe, DllKernel32, S("UmVzdW1lVGhyZWFk"))) return false;
        return Imports(pe, DllKernel32, S("R2V0VGhyZWFkQ29udGV4dA==")) ||
               Imports(pe, DllKernel32, S("V293NjRTZXRUaHJlYWRDb250ZXh0"));
    }

    private static bool R_DRIVER_LOAD_ABUSE(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null) return false;
        bool createService = Imports(pe, DllAdvapi32, S("Q3JlYXRlU2VydmljZUE=")) ||
                             Imports(pe, DllAdvapi32, S("Q3JlYXRlU2VydmljZVc=")) ||
                             Imports(pe, DllAdvapi32, S("U3RhcnRTZXJ2aWNlQQ==")) ||
                             Imports(pe, DllAdvapi32, S("U3RhcnRTZXJ2aWNlVw=="));
        if (!createService) return false;
        return m.CountPresent(IocDriverLoad, true) >= 2;
    }

    private static bool R_MEMORY_MODULE_HINTS(MatchEngine m)
        => m.CountPresent(IocMemoryModule, true) >= 2;

    private static bool R_TOKEN_STEAL_PRIV(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || pe.HasEmbeddedSignature) return false;
        bool tok = Imports(pe, DllAdvapi32, S("T3BlblByb2Nlc3NUb2tlbg==")) ||
                   Imports(pe, DllAdvapi32, S("RHVwbGljYXRlVG9rZW5FeA=="));
        bool adjust = Imports(pe, DllAdvapi32, S("QWRqdXN0VG9rZW5Qcml2aWxlZ2Vz")) ||
                      Imports(pe, DllAdvapi32, S("U2V0VG9rZW5JbmZvcm1hdGlvbg=="));
        return tok && adjust && Imports(pe, DllKernel32, S("T3BlblByb2Nlc3M="));
    }

    

    
    
    
    
    
    
    private static bool R_DYNAMIC_API_RESOLUTION(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null) return false;
        bool resolve = Imports(pe, DllKernel32, S("R2V0UHJvY0FkZHJlc3M=")) ||
                       Imports(pe, DllKernel32, S("R2V0TW9kdWxlSGFuZGxlQQ==")) ||
                       Imports(pe, DllKernel32, S("R2V0TW9kdWxlSGFuZGxlVw==")) ||
                       Imports(pe, DllNtdll, S("TGRyR2V0UHJvY2VkdXJlQWRkcmVzcw==")) ||
                       Imports(pe, DllKernel32, S("TG9hZExpYnJhcnlB")) ||
                       Imports(pe, DllKernel32, S("TG9hZExpYnJhcnlX"));
        if (!resolve) return false;
        bool alloc = Imports(pe, DllKernel32, S("VmlydHVhbEFsbG9jRXg=")) ||
                     Imports(pe, DllNtdll, S("TnRBbGxvY2F0ZVZpcnR1YWxNZW1vcnk="));
        bool act = Imports(pe, DllKernel32, S("V3JpdGVQcm9jZXNzTWVtb3J5")) ||
                   Imports(pe, DllKernel32, S("Q3JlYXRlUmVtb3RlVGhyZWFk")) ||
                   Imports(pe, DllNtdll, S("TnRDcmVhdGVUaHJlYWRFeA==")) ||
                   Imports(pe, DllKernel32, S("VmlydHVhbFByb3RlY3RFeA=="));
        return alloc && act;
    }

    
    
    
    
    
    
    private static bool R_NO_IMPORT_PE(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || pe.HasEmbeddedSignature) return false;
        if (!HasExecutableSection(pe)) return false;
        return pe.Imports.Count == 0;
    }

    
    
    
    
    
    private static bool R_PE_OVERLAY_PAYLOAD(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || pe.HasEmbeddedSignature) return false;
        long lastEnd = 0;
        foreach (var s in pe.Sections)
        {
            long end = (long)s.RawDataOffset + s.RawDataSize;
            if (end > lastEnd) lastEnd = end;
        }
        long overlay = pe.FileSize - lastEnd;
        if (overlay <= 4096) return false;
        return pe.Entropy(lastEnd, overlay) > 7.0;
    }

    
    
    
    
    
    
    private static bool R_PACKED_CODE_SECTION(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || pe.HasEmbeddedSignature) return false;
        foreach (var s in pe.Sections)
        {
            if ((s.Characteristics & CNT_CODE) == 0 || (s.Characteristics & MEM_EXECUTE) == 0)
                continue;
            if (s.RawDataSize > 0 && s.VirtualSize > (long)s.RawDataSize * 2)
                return true;
            if (s.RawDataSize > 4096 && pe.Entropy(s.RawDataOffset, s.RawDataSize) > 7.3)
                return true;
        }
        return false;
    }

    
    
    
    
    
    private static bool R_VIRTUALIZATION_DEBUGGING(MatchEngine m)
    {
        bool debug = m.CountPresent(IocDebugApis, true) >= 2;
        bool virt = m.CountPresent(IocVirt, true) >= 1;
        return debug && virt;
    }

    
    
    
    
    
    private static bool R_KNOWN_CHEAT_NAME(MatchEngine m)
    {
        string? path = m.Path;
        if (string.IsNullOrEmpty(path)) return false;

        string low = path.ToLowerInvariant();
        foreach (var p in IocCheatName)
            if (low.Contains(p, StringComparison.Ordinal)) return true;
        return false;
    }

    
    
    
    
    
    private static bool R_ENCODED_CHEAT_STRING(MatchEngine m)
    {
        foreach (var b in IocEncodedBrands)
            if (m.PresentEncoded(b)) return true;
        return false;
    }

    
    
    
    
    
    private static bool R_LOW_LEVEL_INPUT_HOOK(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || ForensicUtil.IsOsBinaryPath(m.Path ?? "")) return false;
        bool hook = Imports(pe, DllUser32, S("U2V0V2luZG93c0hvb2tFeEE=")) ||
                    Imports(pe, DllUser32, S("U2V0V2luZG93c0hvb2tFeFc="));
        if (!hook) return false;
        bool next = Imports(pe, DllUser32, S("Q2FsbE5leHRIb29rRXg="));
        bool input = Imports(pe, DllUser32, S("R2V0QXN5bmNLZXlTdGF0ZQ==")) ||
                     Imports(pe, DllUser32, S("R2V0Q3Vyc29yUG9z")) ||
                     Imports(pe, DllUser32, S("R2V0S2V5U3RhdGU=")) ||
                     Imports(pe, DllUser32, S("R2V0TWVzc2FnZVc="));
        return next && input;
    }

    
    
    
    
    
    private static bool R_AIMBOT_HINTS(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || !Imports(pe, DllUser32, S("U2V0Q3Vyc29yUG9z"))) return false;
        bool input = Imports(pe, DllUser32, S("R2V0QXN5bmNLZXlTdGF0ZQ==")) ||
                     Imports(pe, DllUser32, S("R2V0S2V5U3RhdGU=")) ||
                     Imports(pe, DllUser32, S("R2V0Q3Vyc29yUG9z"));
        if (!input) return false;
        return m.CountPresent(IocAimbotVocab, true) >= 1;
    }

    
    
    
    
    
    private static bool R_NTDLL_UNHOOK(MatchEngine m)
    {
        bool sig = false;
        foreach (var x in IocNtdllUnhook)
            if (m.Present(x, true)) { sig = true; break; }
        if (!sig) return false;

        var pe = m.Pe;
        if (pe == null) return false;
        return Imports(pe, DllKernel32, S("T3BlblByb2Nlc3M=")) ||
               Imports(pe, DllNtdll, S("TnRQcm90ZWN0VmlydHVhbE1lbW9yeQ==")) ||
               Imports(pe, DllKernel32, S("VmlydHVhbFByb3RlY3Q=")) ||
               Imports(pe, DllNtdll, S("TnRSZXN1bWVQcm9jZXNz"));
    }

    
    
    
    
    
    
    private static bool R_D3D_RENDER_HOOK(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || ForensicUtil.IsOsBinaryPath(m.Path ?? "")) return false;
        if (pe.HasEmbeddedSignature) return false;

        bool renderApi = false;
        foreach (var d in IocRenderApis)
            if (m.Present(d, true)) { renderApi = true; break; }
        if (!renderApi) return false;

        int hookHits = m.CountPresent(IocRenderHooks, true);
        return hookHits >= 2;
    }

    
    
    
    
    
    private static bool R_MEMORY_SCANNER(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || ForensicUtil.IsOsBinaryPath(m.Path ?? "")) return false;
        if (pe.HasEmbeddedSignature) return false;

        bool rw = Imports(pe, DllKernel32, S("UmVhZFByb2Nlc3NNZW1vcnk=")) &&
                  Imports(pe, DllKernel32, S("V3JpdGVQcm9jZXNzTWVtb3J5"));
        bool query = Imports(pe, DllKernel32, S("VmlydHVhbFF1ZXJ5RXg=")) ||
                     Imports(pe, DllKernel32, S("VmlydHVhbFByb3RlY3RFeA==")) ||
                     Imports(pe, DllNtdll, S("TnRRdWVyeVZpcnR1YWxNZW1vcnk="));
        if (!rw || !query) return false;
        return m.CountPresent(IocMemoryScanVocab, true) >= 1;
    }

    
    
    
    
    
    
    
    private static bool R_STARTUP_PERSISTENCE(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || ForensicUtil.IsOsBinaryPath(m.Path ?? "")) return false;
        if (pe.HasEmbeddedSignature) return false;
        if (!HasExecutableSection(pe)) return false;
        return m.CountPresent(IocStartupVocab, true) >= 2;
    }

    
    
    
    
    
    
    private static bool R_MC_PACKET_MANIP(MatchEngine m)
    {
        int packetHits = m.CountPresent(IocMcPacketClasses, true);
        if (packetHits < 2) return false;
        return m.CountPresent(IocMcHackVocab, true) >= 1;
    }

    

    
    
    
    private static bool R_NETWORK_C2(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || pe.HasEmbeddedSignature) return false;
        return m.CountPresent(IocNetworkC2, true) >= 2;
    }

    
    
    
    private static bool R_REGISTRY_PERSISTENCE(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || pe.HasEmbeddedSignature) return false;
        bool regApi = Imports(pe, DllAdvapi32, S("UmVnU2V0VmFsdWVFeEE=")) ||
                      Imports(pe, DllAdvapi32, S("UmVnU2V0VmFsdWVFeFc=")) ||
                      Imports(pe, DllAdvapi32, S("UmVnQ3JlYXRlS2V5RXhB")) ||
                      Imports(pe, DllAdvapi32, S("UmVnQ3JlYXRlS2V5RXhX"));
        if (!regApi) return false;
        int vocabHits = m.CountPresent(IocRegistryPersist, true);
        return vocabHits >= 1 && HasExecutableSection(pe);
    }

    
    
    
    private static bool R_DLL_SIDELOAD_TECHNIQUE(MatchEngine m)
    {
        string? path = m.Path;
        if (string.IsNullOrEmpty(path)) return false;
        if (ForensicUtil.IsOsBinaryPath(path)) return false;
        string low = path.ToLowerInvariant();
        foreach (var dll in IocDllSideloadVocab)
        {
            if (low.Contains(dll, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    
    
    
    private static bool R_WMI_PERSISTENCE(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || pe.HasEmbeddedSignature) return false;
        bool wmiApi = Imports(pe, DllAdvapi32, S("Q29DcmVhdGVJbnN0YW5jZQ==")) ||
                      Imports(pe, DllAdvapi32, S("Q29Jbml0aWFsaXplRXg=")) ||
                      Imports(pe, DllAdvapi32, S("U3lzQWxsb2NTdHJpbmc="));
        if (!wmiApi) return false;
        return m.CountPresent(IocWmiPersist, true) >= 2 && HasExecutableSection(pe);
    }

    
    
    
    
    private static bool R_PROCESS_DOPPELGANGING(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || pe.HasEmbeddedSignature) return false;
        bool procAccess = Imports(pe, DllKernel32, S("T3BlblByb2Nlc3M=")) ||
                          Imports(pe, DllNtdll, S("TnRPcGVuUHJvY2Vzcw=="));
        if (!procAccess) return false;
        bool transacted = m.CountPresent(IocProcessDoppelganging, true) >= 2;
        bool sectionMap = Imports(pe, DllNtdll, S("TnRDcmVhdGVTZWN0aW9u")) &&
                          Imports(pe, DllNtdll, S("TnRNYXBWaWV3T2ZTZWN0aW9u"));
        return transacted || sectionMap;
    }

    
    
    
    private static bool R_DOTNET_CHEAT_FRAMEWORK(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || pe.HasEmbeddedSignature) return false;
        int hits = m.CountPresent(IocDotnetCheat, true);
        
        return hits >= 3;
    }

    

    
    
    
    private static bool R_SCREENSHARE_EVASION(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || ForensicUtil.IsOsBinaryPath(m.Path ?? "")) return false;
        if (pe.HasEmbeddedSignature) return false;
        return m.CountPresent(IocScreenshareEvasion, true) >= 2;
    }

    
    
    
    private static bool R_KEYSTROKE_LOGGER(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || ForensicUtil.IsOsBinaryPath(m.Path ?? "")) return false;
        if (pe.HasEmbeddedSignature) return false;
        bool poll = Imports(pe, DllUser32, S("R2V0QXN5bmNLZXlTdGF0ZQ==")) ||  
                    Imports(pe, DllUser32, S("R2V0S2V5U3RhdGU=")) ||           
                    Imports(pe, DllUser32, S("R2V0S2V5Ym9hcmRTdGF0ZQ=="));     
        if (!poll) return false;
        bool steal = Imports(pe, DllUser32, S("R2V0Rm9yZWdyb3VuZFdpbmRvdw==")) || 
                     Imports(pe, DllUser32, S("R2V0V2luZG93VGV4dFc=")) ||          
                     Imports(pe, DllUser32, S("U2V0V2luZG93c0hvb2tFeFc="));        
        if (!steal) return false;
        return m.CountPresent(IocKeystroke, true) >= 1;
    }

    
    
    
    private static bool R_WINDOW_CAPTURE_EVASION(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || ForensicUtil.IsOsBinaryPath(m.Path ?? "")) return false;
        if (pe.HasEmbeddedSignature) return false;
        if (!Imports(pe, DllUser32, S("U2V0V2luZG93RGlzcGxheUFmZmluaXR5"))) return false;
        return m.CountPresent(IocCaptureEvasion, true) >= 1;
    }

    
    
    
    private static bool R_MC_ESP_HINTS(MatchEngine m)
    {
        int packetHits = m.CountPresent(IocMcPacketClasses, true);
        if (packetHits < 1) return false;
        return m.CountPresent(IocMcEsp, true) >= 1;
    }

    
    
    
    private static bool R_VEH_INJECTION_HINTS(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || ForensicUtil.IsOsBinaryPath(m.Path ?? "")) return false;
        if (pe.HasEmbeddedSignature) return false;
        if (!Imports(pe, DllKernel32, S("QWRkVmVjdG9yZWRFeGNlcHRpb25IYW5kbGVy"))) return false;
        if (!Imports(pe, DllKernel32, S("U2V0VGhyZWFkQ29udGV4dA==")) &&
            !Imports(pe, DllKernel32, S("R2V0VGhyZWFkQ29udGV4dA=="))) return false;
        return Imports(pe, DllKernel32, S("V3JpdGVQcm9jZXNzTWVtb3J5")) ||
               Imports(pe, DllKernel32, S("VmlydHVhbEFsbG9jRXg=")) ||
               m.CountPresent(IocVeh, true) >= 1;
    }

    
    
    
    private static bool R_POWERSHELL_STAGER(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || ForensicUtil.IsOsBinaryPath(m.Path ?? "")) return false;
        if (pe.HasEmbeddedSignature) return false;
        return m.CountPresent(IocPowerShell, true) >= 2;
    }

    
    
    
    private static bool R_ANTI_AMSI_ETW(MatchEngine m)
    {
        var pe = m.Pe;
        if (pe == null || ForensicUtil.IsOsBinaryPath(m.Path ?? "")) return false;
        if (pe.HasEmbeddedSignature) return false;
        if (!HasExecutableSection(pe)) return false;
        return m.CountPresent(IocAmsiEtw, true) >= 2;
    }

    private delegate bool RuleFn(MatchEngine m);

    private static readonly (string Name, RuleFn Fn)[] Rules =
    {
        ("AUTOCLICKER", R_AUTOCLICKER),
        ("CLICK_INPUT_COMBO", R_CLICK_INPUT_COMBO),
        ("CSHARP_CLICKER", R_CSHARP_CLICKER),
        ("CHEAT", R_CHEAT),
        ("HIGH_ENTROPY_UNSIGNED_PE", R_HIGH_ENTROPY_UNSIGNED_PE),
        ("HIGH_ENTROPY_SECTION", R_HIGH_ENTROPY_SECTION),
        ("INJECTOR_API", R_INJECTOR_API),
        ("MANUAL_MAP_HINTS", R_MANUAL_MAP_HINTS),
        ("KDMAPPER_LIKE", R_KDMAPPER_LIKE),
        ("JAVA_AGENT_CHEAT", R_JAVA_AGENT_CHEAT),
        ("DLL_SIDELOAD_NAMES", R_DLL_SIDELOAD_NAMES),
        ("STRING_CLEANER_PACKER", R_STRING_CLEANER_PACKER),
        ("SUSPICIOUS_MUTEX", R_SUSPICIOUS_MUTEX),
        ("NULL_FORKED_RECOVERY", R_NULL_FORKED_RECOVERY),
        ("PE_INJECT_COMBO", R_PE_INJECT_COMBO),
        ("PE_DEBUG_ANTI", R_PE_DEBUG_ANTI),
        ("PE_HIGH_ENTROPY_NO_SIG_HINT", R_PE_HIGH_ENTROPY_NO_SIG_HINT),
        ("PE_RWX_SECTION", R_PE_RWX_SECTION),
        ("GAME_OVERLAY_ABUSE", R_GAME_OVERLAY_ABUSE),
        ("NTDLL_UNDOCUMENTED", R_NTDLL_UNDOCUMENTED),
        ("THREAD_HIJACK_HINTS", R_THREAD_HIJACK_HINTS),
        ("DRIVER_LOAD_ABUSE", R_DRIVER_LOAD_ABUSE),
        ("MEMORY_MODULE_HINTS", R_MEMORY_MODULE_HINTS),
        ("TOKEN_STEAL_PRIV", R_TOKEN_STEAL_PRIV),
        
        ("DYNAMIC_API_RESOLUTION", R_DYNAMIC_API_RESOLUTION),
        ("NO_IMPORT_PE", R_NO_IMPORT_PE),
        ("PE_OVERLAY_PAYLOAD", R_PE_OVERLAY_PAYLOAD),
        ("PACKED_CODE_SECTION", R_PACKED_CODE_SECTION),
        ("VIRTUALIZATION_DEBUGGING", R_VIRTUALIZATION_DEBUGGING),
        ("KNOWN_CHEAT_NAME", R_KNOWN_CHEAT_NAME),
        ("ENCODED_CHEAT_STRING", R_ENCODED_CHEAT_STRING),
        ("LOW_LEVEL_INPUT_HOOK", R_LOW_LEVEL_INPUT_HOOK),
        ("AIMBOT_HINTS", R_AIMBOT_HINTS),
        ("NTDLL_UNHOOK", R_NTDLL_UNHOOK),
        ("D3D_RENDER_HOOK", R_D3D_RENDER_HOOK),
        ("MEMORY_SCANNER", R_MEMORY_SCANNER),
        ("STARTUP_PERSISTENCE", R_STARTUP_PERSISTENCE),
        ("MC_PACKET_MANIP", R_MC_PACKET_MANIP),
        
        ("NETWORK_C2", R_NETWORK_C2),
        ("REGISTRY_PERSISTENCE", R_REGISTRY_PERSISTENCE),
        ("DLL_SIDELOAD_TECHNIQUE", R_DLL_SIDELOAD_TECHNIQUE),
        ("WMI_PERSISTENCE", R_WMI_PERSISTENCE),
        ("PROCESS_DOPPELGANGING", R_PROCESS_DOPPELGANGING),
        ("DOTNET_CHEAT_FRAMEWORK", R_DOTNET_CHEAT_FRAMEWORK),
        
        ("SCREENSHARE_EVASION", R_SCREENSHARE_EVASION),
        ("KEYSTROKE_LOGGER", R_KEYSTROKE_LOGGER),
        ("WINDOW_CAPTURE_EVASION", R_WINDOW_CAPTURE_EVASION),
        ("MC_ESP_HINTS", R_MC_ESP_HINTS),
        ("VEH_INJECTION_HINTS", R_VEH_INJECTION_HINTS),
        ("POWERSHELL_STAGER", R_POWERSHELL_STAGER),
        ("ANTI_AMSI_ETW", R_ANTI_AMSI_ETW)
    };

    private static readonly HashSet<string> MinecraftHostAllow = new(StringComparer.Ordinal)
    {
        "JAVA_AGENT_CHEAT", "CHEAT", "ENCODED_CHEAT_STRING", "KNOWN_CHEAT_HASH",
        "KNOWN_CHEAT_NAME"
    };

    private static readonly HashSet<string> UnsignedGatedRules = new(StringComparer.Ordinal)
    {
        "DYNAMIC_API_RESOLUTION", "VIRTUALIZATION_DEBUGGING", "DLL_SIDELOAD_NAMES",
        "NTDLL_UNDOCUMENTED", "PE_DEBUG_ANTI",
        "INJECTOR_API", "PE_INJECT_COMBO", "CLICK_INPUT_COMBO", "THREAD_HIJACK_HINTS",
        "GAME_OVERLAY_ABUSE", "PE_RWX_SECTION", "DRIVER_LOAD_ABUSE",
        "MEMORY_MODULE_HINTS", "D3D_RENDER_HOOK", "MEMORY_SCANNER",
        "STARTUP_PERSISTENCE",
        
        "NETWORK_C2", "REGISTRY_PERSISTENCE", "WMI_PERSISTENCE",
        "PROCESS_DOPPELGANGING", "DOTNET_CHEAT_FRAMEWORK",
        "SCREENSHARE_EVASION", "KEYSTROKE_LOGGER", "WINDOW_CAPTURE_EVASION",
        "MC_ESP_HINTS", "VEH_INJECTION_HINTS", "POWERSHELL_STAGER",
        "ANTI_AMSI_ETW"
    };

    private static bool IsMinecraftHost(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        string name = Path.GetFileNameWithoutExtension(path);
        return name.Equals("javaw", StringComparison.OrdinalIgnoreCase)
            || name.Equals("java", StringComparison.OrdinalIgnoreCase)
            || name.Equals("javaws", StringComparison.OrdinalIgnoreCase)
            || name.Contains("minecraft", StringComparison.OrdinalIgnoreCase);
    }

    
    
    
    
    internal static List<string> ScanBytes(byte[] data, string? path, bool checkPe)
    {
        var result = new List<string>();
        if (data.Length < 2)
            return result;

        bool isPe = data[0] == (byte)'M' && data[1] == (byte)'Z';
        PeAnalyzer? pe = checkPe && isPe ? PeAnalyzer.FromBytes(data) : null;
        var engine = new MatchEngine(data, pe, path);
        result.AddRange(Evaluate(engine, path));

        if (KnownCheatHashes.Count > 0)
        {
            try
            {
                if (KnownCheatHashes.Contains(Convert.ToHexString(SHA256.HashData(data))))
                    result.Add("KNOWN_CHEAT_HASH");
            }
            catch
            {
                
            }
        }

        return result;
    }

    
    private static List<string> Evaluate(MatchEngine engine, string? path)
    {
        var result = new List<string>();
        var pe = engine.Pe;
        bool isPe = pe != null;
        bool mcHost = IsMinecraftHost(path);
        bool trustedOs = isPe && path != null && ForensicUtil.IsOsBinaryPath(path);

        foreach (var (name, fn) in Rules)
        {
            try
            {
                if (mcHost && !MinecraftHostAllow.Contains(name))
                    continue;
                if (trustedOs && UnsignedGatedRules.Contains(name))
                    continue;
                if (fn(engine)) result.Add(name);
            }
            catch
            {
                
            }
        }

        return result;
    }

    
    public static List<string> Scan(string path, bool checkPe)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(path) || ForensicUtil.IsUnresolved(path))
            return result;
        path = ForensicUtil.ResolveExistingPath(path);
        if (string.IsNullOrEmpty(path) || !ForensicUtil.FileExistsNative(path))
            return result;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 0x8000, FileOptions.SequentialScan);
            if (fs.Length < 2) return result;

            var head = new byte[2];
            fs.ReadExactly(head, 0, 2);
            bool isPe = head[0] == (byte)'M' && head[1] == (byte)'Z';
            bool isZip = head[0] == (byte)'P' && head[1] == (byte)'K';
            string ext = Path.GetExtension(path);
            bool scanStrings = isPe || isZip
                || ext.Equals(".jar", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".class", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".asar", StringComparison.OrdinalIgnoreCase);
            if (!scanStrings)
                return result;

            int cap = isPe ? MaxFileBytes : StringScanLimit;
            long remaining = fs.Length - 2;
            int toRead = (int)Math.Min(remaining, cap - 2) + 2;
            var data = new byte[toRead];
            data[0] = head[0];
            data[1] = head[1];
            int offset = 2;
            while (offset < toRead)
            {
                int n = fs.Read(data, offset, toRead - offset);
                if (n <= 0)
                    break;
                offset += n;
            }
            if (offset < toRead)
                Array.Resize(ref data, offset);
            if (data.Length < 2)
                return result;

            
            if (fs.Length <= toRead)
                return ScanBytes(data, path, checkPe);

            
            
            
            
            
            PeAnalyzer? pe = checkPe && isPe ? PeAnalyzer.FromBytes(data) : null;
            var engine = new MatchEngine(data, pe, path);

            IncrementalHash? hasher = null;
            if (KnownCheatHashes.Count > 0)
            {
                hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                hasher.AppendData(data);
            }

            var buf = new byte[ChunkSize + ChunkOverlap];
            int carried = Math.Min(ChunkOverlap, data.Length);
            Array.Copy(data, data.Length - carried, buf, 0, carried);
            while (true)
            {
                int got = fs.Read(buf, carried, ChunkSize);
                if (got <= 0)
                    break;
                engine.ScanMore(buf.AsSpan(0, carried + got));
                hasher?.AppendData(buf, carried, got);
                if (got < ChunkSize)
                    break;
                Array.Copy(buf, carried + got - ChunkOverlap, buf, 0, ChunkOverlap);
            }

            result.AddRange(Evaluate(engine, path));

            if (hasher is not null)
            {
                try
                {
                    if (KnownCheatHashes.Contains(Convert.ToHexString(hasher.GetHashAndReset())))
                        result.Add("KNOWN_CHEAT_HASH");
                }
                catch
                {
                    
                }
            }

            return result;
        }
        catch
        {
            return result;
        }
    }
}
