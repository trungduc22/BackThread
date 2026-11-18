using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using BackThread.Transport;
using Windows.Management.Deployment;

namespace BackThread.Host;

public sealed record PinnedApplication(string Id,string Title,string Kind,string Target,string[] Arguments,string? IconBase64=null);
public sealed class AppCatalog
{
    private readonly string path;
    private readonly object gate=new();
    private List<PinnedApplication> entries=[];
    public event Action? Changed;
    public string? LoadError {get;private set;}
    public AppCatalog(string path){this.path=Path.GetFullPath(path);try{Reload();}catch(Exception ex)when(ex is IOException or JsonException or InvalidDataException or ArgumentException){LoadError=ex.Message;}}
    public PinnedApplication[] Entries {get{lock(gate)return entries.Select(x=>x with{Arguments=x.Arguments.ToArray()}).ToArray();}}
    public void Reload()
    {
        if(!File.Exists(path)){LoadError=null;return;}
        if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("Pinned app catalog exceeds its size limit");
        var loaded=JsonSerializer.Deserialize<List<PinnedApplication>>(File.ReadAllText(path),Message.Json)??[];
        if(loaded.Count>32||loaded.Select(x=>x.Id).Distinct(StringComparer.Ordinal).Count()!=loaded.Count)throw new InvalidDataException("Invalid pinned app catalog");
        foreach(var app in loaded)Validate(app,false);
        lock(gate){entries=loaded;LoadError=null;}
    }
    public void Pin(PinnedApplication app)
    {
        Validate(app,true);
        lock(gate){var next=entries.ToList();var existing=next.FindIndex(x=>x.Id==app.Id);if(existing>=0)next[existing]=app with{Arguments=app.Arguments.ToArray()};else{if(next.Count>=32)throw new IOException("At most 32 applications may be pinned");next.Add(app with{Arguments=app.Arguments.ToArray()});}Save(next);}
        Changed?.Invoke();
    }
    public void Remove(string id){lock(gate){Save(entries.Where(x=>x.Id!=id).ToList());}Changed?.Invoke();}
    public void Reorder(string[] ids)
    {
        lock(gate){if(ids.Length!=entries.Count||ids.Distinct(StringComparer.Ordinal).Count()!=ids.Length||ids.Any(id=>!entries.Any(x=>x.Id==id)))throw new InvalidDataException("Reorder must contain exactly the approved app IDs");Save(ids.Select(id=>entries.Single(x=>x.Id==id)).ToList());}Changed?.Invoke();
    }
    private void Save(List<PinnedApplication> next){if(LoadError is not null)throw new IOException("The existing catalog needs repair; its file has been preserved");Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path+".tmp",JsonSerializer.Serialize(next,Message.Json));File.Move(path+".tmp",path,true);entries=next;}
    private static void Validate(PinnedApplication app,bool requirePresent)
    {
        if(app.Id is not{Length:>0 and <=64}||!app.Id.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_')||app.Title is not{Length:>0 and <=80}||app.Target is not{Length:>0 and <=1024}||app.Arguments is null||app.Arguments.Length>16||app.Arguments.Any(x=>x is null||x.Length>2048||x.Contains('\0'))||app.Arguments.Sum(x=>x.Length)>4096)throw new InvalidDataException("Invalid pinned application");
        if(app.Kind=="exe"){if(!Path.IsPathFullyQualified(app.Target)||app.Target.StartsWith("\\\\",StringComparison.Ordinal)||!app.Target.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("An approved local executable is required");if(requirePresent)CanonicalExecutable(app.Target);}
        else if(app.Kind=="packaged"){if(app.Arguments.Length!=0||app.Target.Length>256||app.Target.Count(c=>c=='!')!=1||!app.Target.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_' or '!'))throw new InvalidDataException("Invalid packaged application identity");}
        else throw new InvalidDataException("Unsupported application type");
        if(app.IconBase64 is not null){byte[] png;try{png=Convert.FromBase64String(app.IconBase64);}catch(FormatException){throw new InvalidDataException("Invalid app icon");}if(png.Length is <24 or >8192||!png.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})||BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)) is 0 or >128||BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20)) is 0 or >128)throw new InvalidDataException("App icon must be a bounded PNG thumbnail");}
    }
    public static string CanonicalExecutable(string target)
    {
        string full=Path.GetFullPath(target);
        if(!Path.IsPathFullyQualified(target)||full.StartsWith("\\\\",StringComparison.Ordinal)||!full.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(full))throw new FileNotFoundException("Approved executable is unavailable");
        for(string? part=full;part is not null;part=Path.GetDirectoryName(part))if((File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("Reparse-point application paths are not approved; pin the canonical executable");
        return full;
    }
    public async Task<object[]> WireCatalogAsync()
    {
        var result=new List<object>();
        foreach(var app in Entries)
        {
            bool available=false;
            try{if(app.Kind=="exe"){CanonicalExecutable(app.Target);available=true;}else available=await IsInstalledAsync(app.Target);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or COMException){ }
            string version=app.IconBase64 is null?"fallback":Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(app.IconBase64))).ToLowerInvariant();
            result.Add(new{id=app.Id,title=app.Title,available,iconVersion=version,iconBase64=app.IconBase64,icon="apps"});
        }
        return result.ToArray();
    }
    private static async Task<bool> IsInstalledAsync(string appId)
    {
        foreach(var package in new PackageManager().FindPackagesForUser(""))
        {
            if(!appId.StartsWith(package.Id.FamilyName+"!",StringComparison.Ordinal))continue;
            if((await package.GetAppListEntriesAsync()).Any(x=>x.AppUserModelId==appId))return true;
        }
        return false;
    }
    public async Task<object> LaunchRequestAsync(JsonElement body,CancellationToken ct)
    {
        if(body.ValueKind!=JsonValueKind.Object||body.EnumerateObject().Any(p=>p.Name!="appId")||!body.TryGetProperty("appId",out var value)||value.ValueKind!=JsonValueKind.String)throw new UnauthorizedAccessException("Only an approved app ID may be supplied remotely");
        string id=value.GetString()!;PinnedApplication app=Entries.SingleOrDefault(x=>x.Id==id)??throw new UnauthorizedAccessException("Application is not pinned");Validate(app,true);ct.ThrowIfCancellationRequested();
        if(app.Kind=="exe")
        {
            var start=new ProcessStartInfo(CanonicalExecutable(app.Target)){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(app.Target)!};
            foreach(string argument in app.Arguments)start.ArgumentList.Add(argument);
            using var process=Process.Start(start)??throw new IOException("Windows did not start the approved application");
            return new{success=true,accepted=true,started=true,processId=process.Id,appId=id};
        }
        bool installed=await IsInstalledAsync(app.Target);
        if(!installed)throw new IOException("Pinned packaged application is not installed for this user");
        var activation=(IApplicationActivationManager)new ApplicationActivationManager();
        try{int result=activation.ActivateApplication(app.Target,"",0,out uint pid);Marshal.ThrowExceptionForHR(result);return new{success=true,accepted=true,started=true,processId=pid,appId=id};}
        finally{Marshal.FinalReleaseComObject(activation);}
    }
    [ComImport,Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]private class ApplicationActivationManager { }
    [ComImport,Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)]string appId,[MarshalAs(UnmanagedType.LPWStr)]string arguments,uint options,out uint processId);
        [PreserveSig]int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)]string appId,IntPtr items,[MarshalAs(UnmanagedType.LPWStr)]string verb,out uint processId);
        [PreserveSig]int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)]string appId,IntPtr items,out uint processId);
    }
}
