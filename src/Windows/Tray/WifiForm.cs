using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using BackThread.Host;

namespace BackThread.Tray;
internal sealed class WifiForm : Form
{
    private readonly System.Windows.Forms.Timer timer=new(){Interval=1000};
    public WifiForm(HostRuntime host)
    {
        Text="BackThread — Temporary Wi-Fi";Width=500;Height=320;StartPosition=FormStartPosition.CenterParent;
        var address=new ComboBox{Left=24,Top=44,Width=430,DropDownStyle=ComboBoxStyle.DropDownList};
        foreach(var ip in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address).Where(a=>a.AddressFamily==AddressFamily.InterNetwork&&!System.Net.IPAddress.IsLoopback(a)).Distinct())address.Items.Add(ip.ToString());
        if(address.Items.Count>0)address.SelectedIndex=0;
        var label=new Label{Text="Choose this PC's private address. USB authorizes the phone.",Left=24,Top=16,Width=440,Height=24};
        var duration=new ComboBox{Left=24,Top=87,Width=190,DropDownStyle=ComboBoxStyle.DropDownList};duration.Items.AddRange(["15 minutes","30 minutes","Until disconnected"]);duration.SelectedIndex=0;
        var fallback=new CheckBox{Text="Prefer USB and allow automatic fallback",Left=24,Top=126,Width=430,Height=28};
        var state=new Label{Left=24,Top=167,Width=430,Height=42};
        var enable=new Button{Text="Enable temporary Wi-Fi",Left=24,Top=223,Width=210,Height=34};var disable=new Button{Text="Disconnect Wi-Fi",Left=249,Top=223,Width=205,Height=34};
        enable.Click+=async(_,_)=>{enable.Enabled=false;try{await host.EnableWifiAsync(address.Text,duration.SelectedIndex==0?15:duration.SelectedIndex==1?30:0,fallback.Checked,CancellationToken.None);}catch(Exception ex){MessageBox.Show(this,ex.Message,"BackThread Wi-Fi");}finally{enable.Enabled=true;}};
        disable.Click+=async(_,_)=>{try{await host.DisableWifiAsync(CancellationToken.None);}catch(Exception ex){MessageBox.Show(this,ex.Message,"BackThread Wi-Fi");}};
        timer.Tick+=(_,_)=>{var status=JsonSerializer.SerializeToElement(host.WifiStatus);state.Text=status.GetProperty("state").GetString()+" · "+Math.Ceiling(status.GetProperty("remainingSeconds").GetDouble())+" seconds remaining";};timer.Start();
        Controls.AddRange([label,address,duration,fallback,state,enable,disable]);
    }
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
