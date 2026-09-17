using System.Diagnostics;
using System.IO;
using System.Collections.Concurrent;








internal static class PrefetchEnrichment
{
    private const int CacheLimit = 5000;
    private static readonly ConcurrentDictionary<string, (string? pn,string? cn,string? fd,string? fv, string? ts)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheGate = new();
    public static void EnrichVersionInfo(PrefetchInfo info)
    {
        var exe = info.MainExecutablePath;
        if(string.IsNullOrEmpty(exe) || ForensicUtil.IsUnresolved(exe)) return;
        var resolved = File.Exists(exe) ? exe : ForensicUtil.ResolveExistingPath(exe);
        if(!File.Exists(resolved)) return;
        if(Cache.TryGetValue(resolved, out var c)){ info.ProductName=c.pn; info.CompanyName=c.cn; info.FileDescription=c.fd; info.FileVersion=c.fv; info.PeLastWrite=c.ts; return; }
        string? pn=null,cn=null,fd=null,fv=null,ts=null;
        try{ var vi=FileVersionInfo.GetVersionInfo(resolved); pn=vi.ProductName; cn=vi.CompanyName; fd=vi.FileDescription; fv=vi.FileVersion; }catch{}
        try{ ts=File.GetLastWriteTimeUtc(resolved).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);}catch{}
        lock (CacheGate)
        {
            if (Cache.Count >= CacheLimit)
                Cache.Clear();
            Cache[resolved]=(pn,cn,fd,fv,ts);
        }
        info.ProductName=pn; info.CompanyName=cn; info.FileDescription=fd; info.FileVersion=fv; info.PeLastWrite=ts;
    }
    public static void Clear()=>Cache.Clear();
}
