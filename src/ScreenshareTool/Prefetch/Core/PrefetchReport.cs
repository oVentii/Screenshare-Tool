using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;



public static class PrefetchReport
{
    private static readonly Regex PidLine = new(@"PID\s*:\s*(\d+)", RegexOptions.IgnoreCase|RegexOptions.Compiled);
    public static string SignatureName(SignatureStatus s)=> s switch{ SignatureStatus.Signed=>"Signed", SignatureStatus.Unsigned=>"Unsigned", SignatureStatus.Cheat=>"Cheat", SignatureStatus.Fake=>"Fake", SignatureStatus.NotMZ=>"NotMZ", _=>"NotFound"};

    public static string ExportCsv(IEnumerable<PrefetchInfo> entries, string? desktopDir=null)
    {
        string dir = desktopDir ?? AppPaths.Exports;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = AppPaths.Exports;
        string path=Path.Combine(dir,$"iRis_Prefetch_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        var sb=new StringBuilder(); sb.Append('\uFEFF');
        sb.AppendLine("RiskScore,RiskTags,Signature,Presence,LastSeen,LastSeenSource,RunCount,ExecTime,YARA,RefFiles,FileMetrics,Integrity,Dirs,Volumes,Path,LastSeenPath,SHA256");
        foreach(var e in entries)
        {
            string exec=e.LastExecutionTimes.Count>0?e.LastExecutionTimes[0]:"";
            string presence=e.PresenceKind switch{ PresenceKind.MissingFile=>"MissingFile", PresenceKind.UnresolvedPath=>"UnresolvedPath", PresenceKind.DeletedPrefetch=>"DeletedPrefetch", PresenceKind.GhostLeftover=>"GhostLeftover", _=>"Present"};
            sb.AppendLine(CsvRow(e.RiskScore.ToString(), e.RiskSummary??"", SignatureName(e.MainSignatureStatus), presence, e.LastSeenTime??"", e.LastSeenSource??"", e.RunCount.ToString(), exec, e.MatchedRules.Count>0?string.Join(" | ",e.MatchedRules):"", e.ReferencedFileCount.ToString(), e.FileMetricsCount.ToString(), e.IntegrityMismatch?"MISMATCH":"OK", e.DirectoryCount.ToString(), e.VolumeCount.ToString(), e.MainExecutablePath??"", e.LastSeenPath??"", e.Sha256??""));
        }
        File.WriteAllText(path,sb.ToString(),Encoding.UTF8);
        return path;
    }
    private static string CsvRow(params string[] f)=> string.Join(',', f.Select(CsvCell));

    
    
    
    
    
    internal static string CsvCell(string? value)
    {
        string s = (value ?? "").Replace("\"", "\"\"");
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n')
            s = "\t" + s;
        return "\"" + s + "\"";
    }

    public static Dictionary<string,object?> GetSysMainInfo()
    {
        var r=new Dictionary<string,object?>{["status"]="Not Found",["pid"]=0,["uptime"]="",["logonTime"]="",["delayedStart"]=false,["serviceName"]=""};
        string? name=null; ServiceControllerStatus st=ServiceControllerStatus.Stopped;
        foreach(var n in new[]{"SysMain","Superfetch"}){ try{ using var sc=new ServiceController(n); st=sc.Status; name=n; break; }catch{}}
        if(name is null) return r;
        r["serviceName"]=name; bool running=st==ServiceControllerStatus.Running; r["status"]=running?"Running":st.ToString(); if(!running) return r;
        int pid=ForensicUtil.GetServicePid(name, out _); if(pid<=0) pid=QueryPidFallback(name); r["pid"]=pid; if(pid<=0) return r;
        try{ using var p=Process.GetProcessById(pid); var up=DateTime.Now-p.StartTime; r["uptime"]=$"{(int)up.TotalHours}h {up.Minutes}m {up.Seconds}s"; long lg=ForensicUtil.GetLogonUnixTime(); if(lg>0){ r["logonTime"]=ForensicUtil.UnixTimeToLocalString(lg); if(new DateTimeOffset(p.StartTime.ToUniversalTime()).ToUnixTimeSeconds()>lg+120) r["delayedStart"]=true; }}catch{}
        return r;
    }
    private static int QueryPidFallback(string svc){ try{ var psi=new ProcessStartInfo("sc.exe","queryex "+svc){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,StandardOutputEncoding=Encoding.UTF8}; using var p=Process.Start(psi); if(p is null) return 0; string o=p.StandardOutput.ReadToEnd(); if(!p.WaitForExit(4000)){ try{p.Kill(true);}catch{} return 0;} var m=PidLine.Match(o); if(m.Success && int.TryParse(m.Groups[1].Value,out int pid)) return pid; }catch{} return 0; }
    public static long GetLogonUnixTime()=>ForensicUtil.GetLogonUnixTime();
}
