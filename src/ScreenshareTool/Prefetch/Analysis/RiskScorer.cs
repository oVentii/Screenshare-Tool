using System.IO;
internal static class RiskScorer
{
    public static void Score(PrefetchInfo info, PrefetchParser.RiskContext? ctx) => Compute(info, ctx);
    
    private static void Compute(PrefetchInfo info, PrefetchParser.RiskContext? context)
    {
        var flags = PrefetchRiskFlags.RiskNone; int score=0; var tags=new List<string>(16);
        
        
        bool trustedOs = PrefetchParser.IsTrustedOsHost(info) || info.IsBootPrefetch;
        void Add(PrefetchRiskFlags b,int p,string t){ if((flags&b)==0){flags|=b;score+=p;tags.Add(t);}}
        switch(info.MainSignatureStatus){
            case SignatureStatus.Unsigned: Add(PrefetchRiskFlags.RiskUnsigned,25,"Unsigned");break;
            case SignatureStatus.Fake: Add(PrefetchRiskFlags.RiskFakeSig,50,"FakeSig");break;
            case SignatureStatus.Cheat: Add(PrefetchRiskFlags.RiskCheatSig,75,"CheatSig");break;
            case SignatureStatus.NotMZ: Add(PrefetchRiskFlags.RiskNotMZ,15,"NotMZ");break;
            case SignatureStatus.NotFound:
                if(!trustedOs && info.PresenceKind is not PresenceKind.Present)
                    Add(PrefetchRiskFlags.RiskNotFound, info.PresenceKind==PresenceKind.UnresolvedPath?12:22,
                        info.PresenceKind==PresenceKind.DeletedPrefetch?"DeletedPf":info.PresenceKind==PresenceKind.GhostLeftover?"Ghost":info.PresenceKind==PresenceKind.UnresolvedPath?"Unresolved":"MissingFile");
                break;
        }
        if(info.MatchedRules.Count>0){ Add(PrefetchRiskFlags.RiskYaraMatch,40,"YARA"); foreach(var r in info.MatchedRules) if(YaraScanner.IsDeepRule(r)){Add(PrefetchRiskFlags.RiskYaraDeep,35,"YARA-Deep");break;}}
        if(info.MatchedRules.Count>=3) Add(PrefetchRiskFlags.RiskYaraMatch,15,"YARA-Multi");
        if(PathClassifier.IsSuspiciousExecutablePath(info.MainExecutablePath??"") && !trustedOs) Add(PrefetchRiskFlags.RiskSuspiciousPath,20,"BadPath");
        bool mcUnsigned = PathClassifier.IsMinecraftRelatedPath(info.MainExecutablePath??"") && info.MainSignatureStatus is SignatureStatus.Unsigned or SignatureStatus.Fake or SignatureStatus.Cheat or SignatureStatus.NotMZ;
        if(mcUnsigned){ Add(PrefetchRiskFlags.RiskMcPathUnsigned,22,"McPathUnsigned"); info.UnsignedInMinecraftPath=true; }
        if(!trustedOs){ if(info.SuspiciousReferenced>0) Add(PrefetchRiskFlags.RiskSuspiciousRef,15,"SuspiciousRefs"); if(info.UnsignedReferenced>=3) Add(PrefetchRiskFlags.RiskUnsignedRefs,10,"UnsignedRefs"); }
        if(info.CheatSignedReferenced>0) Add(PrefetchRiskFlags.RiskCheatRef,30,"CheatRefs");
        
        if(info.ExecutableLoadedRefs>0 && !trustedOs) Add(PrefetchRiskFlags.RiskExecutableLoadedRef,18,"ExecLoadedRefs");
        if(info.MultiVolume && !trustedOs) Add(PrefetchRiskFlags.RiskMultiVolume,12,"MultiVolume");
        if(info.IsHidden && info.IsSystem) Add(PrefetchRiskFlags.RiskHiddenFile,20,"HiddenExe"); else if(info.IsHidden) Add(PrefetchRiskFlags.RiskHiddenFile,12,"HiddenExe");
        if(info.IsReadOnly && !trustedOs) Add(PrefetchRiskFlags.RiskReadOnlyFile,6,"ReadOnly");
        if(info.WasDeleted) Add(PrefetchRiskFlags.RiskDeletedRecovered,22,"DeletedRecovered");
        if(info.IntegrityMismatch) Add(PrefetchRiskFlags.RiskIntegrityMismatch,16,"Integrity");
        if(info.RenamedFile && !trustedOs) Add(PrefetchRiskFlags.RiskRenamedPf,18,"RenamedPf");
        if(info.PrefetchHashMismatch && !trustedOs) Add(PrefetchRiskFlags.RiskPrefetchHashMismatch,20,"HashMismatch");
        if(!string.IsNullOrEmpty(info.DuplicateOf)) Add(PrefetchRiskFlags.RiskDuplicateHash,14,"Duplicate");
        
        
        
        long latestExec = info.LastExecUnix > 0 ? info.LastExecUnix : info.FirstExecUnix;
        if(!trustedOs && context?.LogonTime>0 && latestExec>context.LogonTime) Add(PrefetchRiskFlags.RiskPostLogon,10,"PostLogon");
        if(!trustedOs && info.RunCount>=50 && (flags & (PrefetchRiskFlags.RiskUnsigned|PrefetchRiskFlags.RiskYaraMatch|PrefetchRiskFlags.RiskSuspiciousPath))!=0) Add(PrefetchRiskFlags.RiskHighRunCount,15,"HighRuns");
        if(LooksTimestomped(info)){ info.Timestomped=true; Add(PrefetchRiskFlags.RiskTimestomp,18,"Timestomp"); }
        bool inShim=false,inAm=false,inBam=false; string mainPath=info.MainExecutablePath??""; string probe=!ForensicUtil.IsUnresolved(mainPath)?mainPath:(info.LastSeenPath??"");
        if(probe.Length>0 && !ForensicUtil.IsUnresolved(probe)){ inShim=context?.ShimIndex.Contains(probe)==true; inAm=context?.AmcacheIndex.Contains(probe)==true; inBam=context?.BamIndex.Contains(probe)==true; }
        if(inShim) Add(PrefetchRiskFlags.RiskInShimCache,8,"ShimCache"); if(inAm) Add(PrefetchRiskFlags.RiskInAmcache,8,"Amcache"); if(inBam) Add(PrefetchRiskFlags.RiskInBam,8,"BAM");
        bool interesting=(flags & (PrefetchRiskFlags.RiskUnsigned|PrefetchRiskFlags.RiskYaraMatch|PrefetchRiskFlags.RiskSuspiciousPath|PrefetchRiskFlags.RiskCheatSig|PrefetchRiskFlags.RiskFakeSig|PrefetchRiskFlags.RiskMcPathUnsigned))!=0;
        if((inShim||inAm||inBam)&&interesting) Add(PrefetchRiskFlags.RiskMultiArtifact,12,"MultiArtifact");
        bool exeMissing=info.FileMissing||info.PresenceKind is not PresenceKind.Present; bool leftover=!trustedOs&&(inShim||inAm||inBam)&&(exeMissing||info.WasDeleted);
        if(leftover){ info.ArtifactLeftover=true; Add(PrefetchRiskFlags.RiskArtifactLeftover,24,"ArtifactLeftover");}
        if(!trustedOs&&interesting&&context is{ShimLoaded:true,AmcacheLoaded:true}&&!inShim&&!inAm&&!inBam) Add(PrefetchRiskFlags.RiskArtifactGap,14,"ArtifactGap");
        info.InShimCache=inShim; info.InAmcache=inAm; info.InBam=inBam;
        if(score>100) score=100; info.RiskFlags=flags; info.RiskScore=score; info.RiskSummary=string.Join(" | ",tags);
    }
    private static bool LooksTimestomped(PrefetchInfo info){
        string exe=info.MainExecutablePath??""; if(string.IsNullOrEmpty(exe)||ForensicUtil.IsUnresolved(exe)) return false;
        string r=ForensicUtil.ResolveExistingPath(exe);
        var stat=PrefetchParser.GetCachedFileStat(r);
        if(!stat.Exists) return false;
        var utc=new DateTime(stat.WriteUtcTicks,DateTimeKind.Utc);
        if(string.IsNullOrEmpty(info.PeLastWrite)) info.PeLastWrite=utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss",System.Globalization.CultureInfo.InvariantCulture);
        if(utc>DateTime.UtcNow.AddDays(2)||utc.Year<1995) return true;
        bool und=info.MainSignatureStatus is SignatureStatus.Unsigned or SignatureStatus.Fake or SignatureStatus.Cheat;
        if(und&&utc.Minute==0&&utc.Second==0&&utc.Millisecond==0&&PathClassifier.IsSuspiciousExecutablePath(r)) return true;
        return false;
    }
}
