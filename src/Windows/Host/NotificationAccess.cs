using System.Runtime.InteropServices;
using Windows.UI.Notifications.Management;

namespace BackThread.Host;
public sealed record NotificationPermission(bool Supported,string Status,string Reason);
public static class NotificationAccess
{
    public static bool HasPackageIdentity(){uint length=0;int result=GetCurrentPackageFullName(ref length,IntPtr.Zero);return result is 0 or 122;}
    public static NotificationPermission Inspect()
    {
        if(!HasPackageIdentity())return new(false,"unavailable","Windows notification collection requires package identity and the userNotificationListener capability. The authenticated events API remains available.");
        try{var status=UserNotificationListener.Current.GetAccessStatus();return new(true,status.ToString(),status==UserNotificationListenerAccessStatus.Allowed?"Permission granted; collection still requires opt-in":"Use the explicit desktop permission button");}
        catch(COMException){return new(false,"unavailable","This package or Windows environment does not provide notification listener access");}
    }
    public static async Task<NotificationPermission> RequestFromUiAsync()
    {
        if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)throw new InvalidOperationException("Request notification consent from the desktop UI thread");
        var current=Inspect();if(!current.Supported)return current;
        await UserNotificationListener.Current.RequestAccessAsync();return Inspect();
    }
    [DllImport("kernel32.dll")]private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength,IntPtr packageFullName);
}
