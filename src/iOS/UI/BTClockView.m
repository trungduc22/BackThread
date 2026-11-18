#import "BTClockView.h"
#import "BTTheme.h"
#import <AVFoundation/AVFoundation.h>
#include <ifaddrs.h>
#include <net/if.h>
#include <sys/socket.h>
@interface BTBatteryInk : UIView
@property(nonatomic) float level;
@end
@implementation BTBatteryInk
- (void)drawRect:(CGRect)rect{(void)rect;CGRect body=CGRectMake(1,2,36,18);UIBezierPath *outline=[UIBezierPath bezierPathWithRoundedRect:body cornerRadius:6];[[UIColor colorWithWhite:1 alpha:.65] setStroke];outline.lineWidth=.7;[outline stroke];[[UIColor colorWithWhite:1 alpha:.65] setFill];[[UIBezierPath bezierPathWithRoundedRect:CGRectMake(38,8,2,6) cornerRadius:1] fill];if(self.level>=0){CGContextRef c=UIGraphicsGetCurrentContext();CGContextSaveGState(c);[outline addClip];[[UIColor colorWithWhite:1 alpha:.16] setFill];UIRectFill(CGRectMake(1,2,36*MAX(0,MIN(1,self.level)),18));CGContextRestoreGState(c);}NSString *value=self.level<0?@"—":[NSString stringWithFormat:@"%.0f",self.level*100];NSShadow *shadow=[NSShadow new];shadow.shadowColor=[UIColor colorWithWhite:0 alpha:.8];shadow.shadowBlurRadius=2;shadow.shadowOffset=CGSizeMake(0,1);NSDictionary *attrs=@{NSFontAttributeName:[UIFont monospacedDigitSystemFontOfSize:12 weight:UIFontWeightBold],NSForegroundColorAttributeName:UIColor.whiteColor,NSShadowAttributeName:shadow};CGSize size=[value sizeWithAttributes:attrs];[value drawAtPoint:CGPointMake(19-size.width/2,11-size.height/2) withAttributes:attrs];}
@end
@interface BTBatteryBadge : UIView
@property(nonatomic) float level;
@property(nonatomic) BOOL charging;
@end
@implementation BTBatteryBadge {
    UIVisualEffectView *_glass;BTBatteryInk *_ink;
}
- (instancetype)initWithFrame:(CGRect)frame{if((self=[super initWithFrame:frame])){_glass=[[UIVisualEffectView alloc] initWithEffect:UIAccessibilityIsReduceTransparencyEnabled()?nil:[UIBlurEffect effectWithStyle:UIBlurEffectStyleDark]];_glass.backgroundColor=[UIColor colorWithWhite:0 alpha:UIAccessibilityIsReduceTransparencyEnabled()?.7:.08];_glass.layer.cornerRadius=6;_glass.clipsToBounds=YES;[self addSubview:_glass];_ink=[BTBatteryInk new];_ink.backgroundColor=UIColor.clearColor;[self addSubview:_ink];self.layer.shadowColor=UIColor.blackColor.CGColor;self.layer.shadowOpacity=.35;self.layer.shadowRadius=3;self.layer.shadowOffset=CGSizeMake(0,1);}return self;}
- (void)layoutSubviews{[super layoutSubviews];_glass.frame=CGRectMake(1,2,36,18);_ink.frame=self.bounds;self.layer.shadowPath=[UIBezierPath bezierPathWithRoundedRect:_glass.frame cornerRadius:6].CGPath;}
- (void)setLevel:(float)level{_level=level;_ink.level=level;[_ink setNeedsDisplay];}
@end
@implementation BTClockView {
    UILabel *_time,*_route;UIImageView *_wifiIcon,*_btIcon;BTBatteryBadge *_badge;NSDateFormatter *_format;NSTimer *_timer;BOOL _wasMonitoring;NSUInteger _ticks;
}
- (instancetype)initWithFrame:(CGRect)frame{if((self=[super initWithFrame:frame])){self.userInteractionEnabled=NO;_format=[NSDateFormatter new];_format.dateFormat=@"HH:mm";_time=BTLabel(18,UIFontWeightMedium);_time.font=[UIFont monospacedDigitSystemFontOfSize:18 weight:UIFontWeightMedium];_time.numberOfLines=1;[self addSubview:_time];_badge=[BTBatteryBadge new];_badge.backgroundColor=UIColor.clearColor;_badge.isAccessibilityElement=YES;[self addSubview:_badge];_wifiIcon=[[UIImageView alloc] initWithImage:BTGlyph(@"wifi")];_wifiIcon.tintColor=BTForeground();[self addSubview:_wifiIcon];_btIcon=[[UIImageView alloc] initWithImage:BTGlyph(@"bluetooth")];_btIcon.tintColor=BTForeground();[self addSubview:_btIcon];_route=BTLabel(10,UIFontWeightMedium);_route.textColor=BTMuted();_route.textAlignment=NSTextAlignmentRight;_route.numberOfLines=1;[self addSubview:_route];_wasMonitoring=UIDevice.currentDevice.batteryMonitoringEnabled;UIDevice.currentDevice.batteryMonitoringEnabled=YES;[[NSNotificationCenter defaultCenter] addObserver:self selector:@selector(routeChanged:) name:AVAudioSessionRouteChangeNotification object:nil];[self refresh];[self refreshRoutes];}return self;}
- (void)layoutSubviews{[super layoutSubviews];_time.frame=CGRectMake(0,0,57,25);_badge.frame=CGRectMake(62,1,42,22);_wifiIcon.frame=CGRectMake(115,4,18,18);_btIcon.frame=CGRectMake(141,4,16,18);_route.frame=CGRectMake(164,4,MAX(0,self.bounds.size.width-164),20);_route.textAlignment=NSTextAlignmentLeft;}
- (void)didMoveToWindow{[super didMoveToWindow];[_timer invalidate];_timer=nil;if(self.window){[self refresh];[self refreshRoutes];__weak typeof(self) weak=self;_timer=[NSTimer scheduledTimerWithTimeInterval:1 repeats:YES block:^(NSTimer *timer){(void)timer;[weak refresh];}];}}
- (void)refresh{_format.timeZone=NSTimeZone.localTimeZone;_time.text=[_format stringFromDate:NSDate.date];_badge.level=UIDevice.currentDevice.batteryLevel;UIDeviceBatteryState state=UIDevice.currentDevice.batteryState;_badge.charging=state==UIDeviceBatteryStateCharging||state==UIDeviceBatteryStateFull;_badge.accessibilityLabel=_badge.level<0?@"iPhone battery unavailable":[NSString stringWithFormat:@"iPhone battery %.0f percent%@",_badge.level*100,_badge.charging?@", charging":@""];[_badge setNeedsDisplay];if(++_ticks%5==0)[self refreshRoutes];}
- (void)routeChanged:(NSNotification *)notice{(void)notice;__weak typeof(self) weak=self;dispatch_async(dispatch_get_main_queue(),^{[weak refreshRoutes];});}
- (void)refreshRoutes{BOOL wifi=NO;struct ifaddrs *addresses=NULL;if(getifaddrs(&addresses)==0){for(struct ifaddrs *a=addresses;a;a=a->ifa_next){if(a->ifa_addr&&strcmp(a->ifa_name,"en0")==0&&(a->ifa_flags&IFF_UP)&&(a->ifa_addr->sa_family==AF_INET||a->ifa_addr->sa_family==AF_INET6))wifi=YES;}freeifaddrs(addresses);}_wifiIcon.alpha=wifi?1:.25;_wifiIcon.isAccessibilityElement=YES;_wifiIcon.accessibilityLabel=wifi?@"Wi-Fi interface has an IP address; internet reachability not tested":@"Wi-Fi address unavailable";BOOL bluetoothAudio=NO;NSString *route=@"BT —";for(AVAudioSessionPortDescription *port in AVAudioSession.sharedInstance.currentRoute.outputs){if([@[AVAudioSessionPortBluetoothA2DP,AVAudioSessionPortBluetoothHFP,AVAudioSessionPortBluetoothLE,AVAudioSessionPortHeadphones] containsObject:port.portType]){route=port.portName.length?port.portName:@"Headphones";bluetoothAudio=![port.portType isEqual:AVAudioSessionPortHeadphones];break;}}_btIcon.alpha=bluetoothAudio?1:.25;_btIcon.isAccessibilityElement=YES;_btIcon.accessibilityLabel=bluetoothAudio?@"Bluetooth audio route active":@"No Bluetooth audio route reported; radio state is unknown";_route.text=[route isEqual:@"BT —"]?@"":route;_route.accessibilityLabel=[route isEqual:@"BT —"]?@"No Bluetooth audio route reported; Bluetooth radio state is unknown":route;}
- (NSDictionary *)snapshot{return @{@"time":_time.text?:@"",@"format":_format.dateFormat?:@"",@"x":@(self.frame.origin.x),@"headphoneName":_route.text?:@"",@"batteryStyle":@"monochrome-blur"};}
- (void)dealloc{[[NSNotificationCenter defaultCenter] removeObserver:self];[_timer invalidate];UIDevice.currentDevice.batteryMonitoringEnabled=_wasMonitoring;}
@end
