#import "BTClockView.h"
#import "BTTheme.h"
@implementation BTClockView {
    UILabel *_time,*_battery;NSDateFormatter *_format;NSTimer *_timer;BOOL _wasMonitoring;
}
- (instancetype)initWithFrame:(CGRect)frame {
    if((self=[super initWithFrame:frame])){
        self.userInteractionEnabled=NO;_format=[NSDateFormatter new];_format.dateFormat=@"HH:mm:ss";
        _time=BTLabel(18,UIFontWeightMedium);_time.font=[UIFont monospacedDigitSystemFontOfSize:18 weight:UIFontWeightMedium];_time.textAlignment=NSTextAlignmentRight;_time.numberOfLines=1;[self addSubview:_time];
        _battery=BTLabel(10,UIFontWeightRegular);_battery.textColor=BTMuted();_battery.textAlignment=NSTextAlignmentRight;_battery.numberOfLines=1;[self addSubview:_battery];
        _wasMonitoring=UIDevice.currentDevice.batteryMonitoringEnabled;UIDevice.currentDevice.batteryMonitoringEnabled=YES;
        [self refresh];
    }return self;
}
- (void)layoutSubviews{[super layoutSubviews];_time.frame=CGRectMake(0,0,self.bounds.size.width,25);_battery.frame=CGRectMake(0,26,self.bounds.size.width,14);}
- (void)didMoveToWindow{[super didMoveToWindow];[_timer invalidate];_timer=nil;if(self.window){[self refresh];__weak typeof(self) weak=self;_timer=[NSTimer scheduledTimerWithTimeInterval:1 repeats:YES block:^(NSTimer *timer){(void)timer;[weak refresh];}];}}
- (void)refresh{
    _format.timeZone=NSTimeZone.localTimeZone;_time.text=[_format stringFromDate:NSDate.date];float level=UIDevice.currentDevice.batteryLevel;UIDeviceBatteryState state=UIDevice.currentDevice.batteryState;
    _battery.text=level<0?@"iPhone battery unavailable":[NSString stringWithFormat:@"iPhone %.0f%%%@",level*100,state==UIDeviceBatteryStateCharging?@" · Charging":state==UIDeviceBatteryStateFull?@" · Full":@""];
}
- (void)dealloc{[_timer invalidate];UIDevice.currentDevice.batteryMonitoringEnabled=_wasMonitoring;}
@end
