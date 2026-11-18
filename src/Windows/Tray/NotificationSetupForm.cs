using BackThread.Host;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace BackThread.Tray;

internal sealed class NotificationSetupForm : Form
{
    private readonly Label status=new(){AutoSize=false,Dock=DockStyle.Top,Height=110,Padding=new Padding(18)};
    private readonly Button enable=new(){Text="Cho phép đọc thông báo",AutoSize=true};
    private readonly Button test=new(){Text="Gửi thông báo Windows thử",AutoSize=true};
    private readonly HostRuntime runtime;
    public NotificationSetupForm(HostRuntime runtime)
    {
        this.runtime=runtime;Text="BackThread — Thông báo Windows";ClientSize=new Size(540,210);StartPosition=FormStartPosition.CenterScreen;
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=64,Padding=new Padding(18)};buttons.Controls.Add(enable);buttons.Controls.Add(test);Controls.Add(status);Controls.Add(buttons);
        enable.Click+=async(_,_)=>{enable.Enabled=false;try{var permission=await NotificationAccess.RequestFromUiAsync();if(permission.Status=="Allowed")runtime.EnableNotificationCollection();RefreshStatus();}catch(Exception ex){status.Text=ex.Message;enable.Enabled=true;}};
        test.Click+=(_,_)=>{try{var notifier=ToastNotificationManager.CreateToastNotifier();if(notifier.Setting!=NotificationSetting.Enabled){status.Text="Windows đang tắt thông báo của BackThread. Kiểm tra quyền thông báo của ứng dụng trong Settings.";return;}var xml=new XmlDocument();xml.LoadXml("<toast><visual><binding template='ToastGeneric'><text>BackThread kiểm tra</text><text>Đây là thông báo Windows thật để kiểm tra banner trên iPhone.</text></binding></visual></toast>");notifier.Show(new ToastNotification(xml){ExpirationTime=DateTimeOffset.Now.AddSeconds(30)});status.Text="Đã yêu cầu Windows hiển thị thông báo. Giữ BackThread trên iPhone mở; nội dung riêng tư có thể đang được ẩn theo tùy chọn của bạn.";}catch(Exception ex){status.Text=ex.Message;}};
        RefreshStatus();
    }
    private void RefreshStatus(){var permission=NotificationAccess.Inspect();enable.Enabled=permission.Supported&&permission.Status!="Allowed";test.Enabled=permission.Supported&&permission.Status=="Allowed";status.Text=!permission.Supported?"Bản portable chưa đọc được thông báo Windows. Hãy mở BackThread đã cài bằng gói Windows, rồi cho phép đọc thông báo ở cửa sổ này.":permission.Status=="Allowed"?"Windows đã cho phép đọc thông báo. Bấm Cho phép để bật thu thập cho lần chạy này, hoặc gửi một thông báo Windows thử.":"Bấm Cho phép; Windows sẽ hỏi bạn trước khi BackThread được đọc thông báo.";if(permission.Supported&&permission.Status=="Allowed"){runtime.EnableNotificationCollection();status.Text="Đã bật chuyển thông báo Windows cho lần chạy này. Có thể gửi thông báo Windows thử.";}}
}
