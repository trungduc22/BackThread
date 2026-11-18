using System.Runtime.InteropServices;
using BackThread.Host;
using Windows.Management.Deployment;
using Windows.ApplicationModel.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace BackThread.Tray;

internal sealed class AppsForm : Form
{
    private readonly AppCatalog catalog;
    private readonly ListView list=new(){View=View.Details,FullRowSelect=true,MultiSelect=false,Dock=DockStyle.Fill};
    private readonly Label summary=new(){Dock=DockStyle.Top,Height=44,Text="Only the applications you pin here are shared with your iPhone. Hold an iPhone tile to reorder."};
    public AppsForm(AppCatalog catalog)
    {
        this.catalog=catalog;Text="BackThread — Pinned Windows apps";Width=710;Height=440;StartPosition=FormStartPosition.CenterScreen;
        list.Columns.Add("Pinned application",210);list.Columns.Add("Type",85);list.Columns.Add("Approved target (stays on PC)",350);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=78,WrapContents=true};
        void Button(string text,Action action){var b=new Button{Text=text,AutoSize=true,Height=32};b.Click+=(_,_)=>Safe(action);buttons.Controls.Add(b);}
        Button("Pin executable / shortcut…",AddExecutable);Button("Browse Start Menu…",BrowseStartMenu);
        var packaged=new Button{Text="Choose packaged app…",AutoSize=true,Height=32};packaged.Click+=async(_,_)=>{try{await AddPackaged();}catch(Exception ex){MessageBox.Show(this,ex.Message,"BackThread");}};buttons.Controls.Add(packaged);
        Button("Remove pin",()=>{if(Selected is{} app)catalog.Remove(app.Id);RefreshEntries();});
        Button("Move up",()=>MovePinned(-1));Button("Move down",()=>MovePinned(1));Button("Refresh icon",RefreshIcon);
        Controls.Add(list);Controls.Add(summary);Controls.Add(buttons);RefreshEntries();
    }
    private PinnedApplication? Selected=>list.SelectedItems.Count==1?catalog.Entries.FirstOrDefault(x=>x.Id==(string)list.SelectedItems[0].Tag!):null;
    private void Safe(Action action){try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"BackThread",MessageBoxButtons.OK,MessageBoxIcon.Information);}}
    private void RefreshEntries(){list.Items.Clear();foreach(var app in catalog.Entries){var item=new ListViewItem(new[]{app.Title,app.Kind,app.Target}){Tag=app.Id};list.Items.Add(item);}}
    private void AddExecutable(){using var dialog=new OpenFileDialog{Title="Choose one application to pin",Filter="Applications and shortcuts|*.exe;*.lnk",CheckFileExists=true};if(dialog.ShowDialog(this)==DialogResult.OK)PinPath(dialog.FileName);}
    private void PinPath(string path)
    {
        string title=Path.GetFileNameWithoutExtension(path),target=path;string[] arguments=[];
        if(Path.GetExtension(path).Equals(".lnk",StringComparison.OrdinalIgnoreCase))
        {
            var type=Type.GetTypeFromProgID("WScript.Shell")??throw new IOException("Windows shortcut metadata service unavailable");dynamic shell=Activator.CreateInstance(type)!;dynamic shortcut=shell.CreateShortcut(path);
            try{target=(string)shortcut.TargetPath;arguments=ParseArguments((string)shortcut.Arguments);}finally{Marshal.FinalReleaseComObject(shortcut);Marshal.FinalReleaseComObject(shell);}
        }
        target=AppCatalog.CanonicalExecutable(target);
        if(MessageBox.Show(this,$"Pin {title}?\n\nTarget: {target}\nFixed arguments: {string.Join(" ",arguments)}\n\nThe phone can send only this app's ID.","Approve application",MessageBoxButtons.OKCancel,MessageBoxIcon.Question)!=DialogResult.OK)return;
        catalog.Pin(new(Guid.NewGuid().ToString("N"),title,"exe",target,arguments,ExtractIcon(target)));RefreshEntries();
    }
    private void BrowseStartMenu()
    {
        var paths=new[]{Environment.GetFolderPath(Environment.SpecialFolder.Programs),Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)}.Where(Directory.Exists)
            .SelectMany(p=>Directory.EnumerateFiles(p,"*.lnk",new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint})).Take(512).OrderBy(Path.GetFileName).ToArray();
        using var picker=new Form{Text="Choose a Start Menu shortcut (local only)",Width=580,Height=450,StartPosition=FormStartPosition.CenterParent};
        var choices=new ListBox{Dock=DockStyle.Fill};foreach(var path in paths)choices.Items.Add(new Choice(Path.GetFileNameWithoutExtension(path),path));
        var choose=new Button{Text="Pin selected shortcut",Dock=DockStyle.Bottom,Height=40};choose.Click+=(_,_)=>{if(choices.SelectedItem is Choice selected){PinPath(selected.Target);picker.Close();}};picker.Controls.Add(choices);picker.Controls.Add(choose);picker.ShowDialog(this);
    }
    private async Task AddPackaged()
    {
        using var picker=new Form{Text="Choose a packaged Windows app (local only)",Width=580,Height=450,StartPosition=FormStartPosition.CenterParent};
        var choices=new ListBox{Dock=DockStyle.Fill};var choose=new Button{Text="Pin selected app",Dock=DockStyle.Bottom,Height=40};
        var entries=new List<AppListEntry>();
        foreach(var package in new PackageManager().FindPackagesForUser(""))
        {
            try{foreach(var entry in await package.GetAppListEntriesAsync()){if(entries.Count>=512)break;entries.Add(entry);choices.Items.Add(new Choice(entry.DisplayInfo.DisplayName,entry.AppUserModelId));}}catch(Exception ex)when(ex is UnauthorizedAccessException or InvalidOperationException){ }
        }
        choose.Click+=async(_,_)=>{if(choices.SelectedIndex<0)return;var entry=entries[choices.SelectedIndex];try{string? icon=await PackageIcon(entry);catalog.Pin(new(Guid.NewGuid().ToString("N"),entry.DisplayInfo.DisplayName,"packaged",entry.AppUserModelId,[],icon));RefreshEntries();picker.Close();}catch(Exception ex){MessageBox.Show(picker,ex.Message,"BackThread");}};
        picker.Controls.Add(choices);picker.Controls.Add(choose);picker.ShowDialog(this);
    }
    private static async Task<string?> PackageIcon(AppListEntry entry)
    {
        try
        {
            using var input=await entry.DisplayInfo.GetLogo(new Windows.Foundation.Size(64,64)).OpenReadAsync();if(input.Size>2*1024*1024)return null;
            var decoder=await BitmapDecoder.CreateAsync(input);if(decoder.PixelWidth is 0 or >8192||decoder.PixelHeight is 0 or >8192)return null;
            var pixels=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,new BitmapTransform{ScaledWidth=64,ScaledHeight=64},ExifOrientationMode.RespectExifOrientation,ColorManagementMode.DoNotColorManage);
            using var output=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,output);encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,64,64,96,96,pixels.DetachPixelData());await encoder.FlushAsync();if(output.Size>8192)return null;
            using var reader=new DataReader(output.GetInputStreamAt(0));await reader.LoadAsync((uint)output.Size);var data=new byte[(int)output.Size];reader.ReadBytes(data);return Convert.ToBase64String(data);
        }
        catch(Exception ex)when(ex is IOException or InvalidOperationException or ArgumentException){return null;}
    }
    private void RefreshIcon(){if(Selected is not{} app)return;if(app.Kind!="exe"){MessageBox.Show(this,"Re-pin the packaged app to refresh its icon.","BackThread");return;}catalog.Pin(app with{IconBase64=ExtractIcon(AppCatalog.CanonicalExecutable(app.Target))});RefreshEntries();}
    private void MovePinned(int direction){if(Selected is not{} app)return;var ids=catalog.Entries.Select(x=>x.Id).ToList();int from=ids.IndexOf(app.Id),to=Math.Clamp(from+direction,0,ids.Count-1);ids.RemoveAt(from);ids.Insert(to,app.Id);catalog.Reorder(ids.ToArray());RefreshEntries();}
    private static string? ExtractIcon(string path)
    {
        try{using var icon=Icon.ExtractAssociatedIcon(path);if(icon is null)return null;using var bitmap=new Bitmap(64,64);using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.Transparent);graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;using var original=icon.ToBitmap();graphics.DrawImage(original,new Rectangle(0,0,64,64));}using var stream=new MemoryStream();bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);return stream.Length<=8192?Convert.ToBase64String(stream.ToArray()):null;}
        catch(Exception ex)when(ex is ArgumentException or IOException or System.ComponentModel.Win32Exception){return null;}
    }
    private static string[] ParseArguments(string text){if(string.IsNullOrEmpty(text))return [];IntPtr pointer=CommandLineToArgvW("app.exe "+text,out int count);if(pointer==IntPtr.Zero)throw new IOException("Cannot parse trusted shortcut arguments");try{return Enumerable.Range(1,count-1).Select(i=>Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer,i*IntPtr.Size))!).ToArray();}finally{LocalFree(pointer);}}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr CommandLineToArgvW(string commandLine,out int count);
    [DllImport("kernel32.dll")]private static extern IntPtr LocalFree(IntPtr pointer);
    private sealed record Choice(string Title,string Target){public override string ToString()=>Title;}
}
