#import "BTTheme.h"
#import <ImageIO/ImageIO.h>
UIImage *BTThumbnail(NSString *encoded,NSUInteger pixels){if(![encoded isKindOfClass:NSString.class]||encoded.length>12000||pixels>256)return nil;NSData *data=[[NSData alloc] initWithBase64EncodedString:encoded options:0];CGImageSourceRef source=data?CGImageSourceCreateWithData((__bridge CFDataRef)data,NULL):NULL;if(!source)return nil;NSDictionary *properties=CFBridgingRelease(CGImageSourceCopyPropertiesAtIndex(source,0,NULL));if([properties[(id)kCGImagePropertyPixelWidth] unsignedIntegerValue]>512||[properties[(id)kCGImagePropertyPixelHeight] unsignedIntegerValue]>512){CFRelease(source);return nil;}CGImageRef image=CGImageSourceCreateThumbnailAtIndex(source,0,(__bridge CFDictionaryRef)@{(id)kCGImageSourceCreateThumbnailFromImageAlways:@YES,(id)kCGImageSourceThumbnailMaxPixelSize:@(pixels)});UIImage *result=image?[UIImage imageWithCGImage:image]:nil;if(image)CGImageRelease(image);CFRelease(source);return result;}
UIColor *BTBackground(void){return [UIColor colorWithRed:.035 green:.046 blue:.061 alpha:1];}
UIColor *BTForeground(void){return [UIColor colorWithRed:.95 green:.955 blue:.93 alpha:1];}
UIColor *BTMuted(void){return [UIColor colorWithRed:.57 green:.63 blue:.66 alpha:1];}
UIColor *BTAccent(void){return [UIColor colorWithRed:.45 green:.88 blue:.78 alpha:1];}
NSString *BTString(id value,NSString *fallback){return [value isKindOfClass:NSString.class]?value:fallback;}
double BTDouble(id value){return [value isKindOfClass:NSNumber.class]?[value doubleValue]:0;}
UILabel *BTLabel(CGFloat size,UIFontWeight weight){UILabel *l=[UILabel new];l.font=[UIFont systemFontOfSize:size weight:weight];l.textColor=BTForeground();l.numberOfLines=2;return l;}
void BTAnimate(NSTimeInterval duration,void(^changes)(void),void(^completion)(BOOL)){
    [UIView animateWithDuration:UIAccessibilityIsReduceMotionEnabled()?0:duration delay:0 options:UIViewAnimationOptionBeginFromCurrentState|UIViewAnimationOptionAllowUserInteraction animations:changes completion:completion];
}
static void line(CGFloat x,CGFloat y,CGFloat a,CGFloat b){UIBezierPath *p=[UIBezierPath bezierPath];p.lineWidth=1.7;p.lineCapStyle=kCGLineCapRound;[p moveToPoint:CGPointMake(x,y)];[p addLineToPoint:CGPointMake(a,b)];[p stroke];}
UIImage *BTGlyph(NSString *name){
    static NSCache *cache;static dispatch_once_t once;dispatch_once(&once,^{cache=[NSCache new];cache.countLimit=40;cache.totalCostLimit=256*1024;});
    UIImage *saved=[cache objectForKey:name];if(saved)return saved;
    UIGraphicsBeginImageContextWithOptions(CGSizeMake(24,24),NO,2);[[UIColor whiteColor] setStroke];[[UIColor whiteColor] setFill];
    if([name isEqual:@"play"]||[name isEqual:@"next"]||[name isEqual:@"previous"]){
        BOOL previous=[name isEqual:@"previous"];CGContextRef c=UIGraphicsGetCurrentContext();if(previous){CGContextTranslateCTM(c,24,0);CGContextScaleCTM(c,-1,1);}
        UIBezierPath *p=[UIBezierPath bezierPath];[p moveToPoint:CGPointMake(7,5)];[p addLineToPoint:CGPointMake(18,12)];[p addLineToPoint:CGPointMake(7,19)];[p closePath];[p fill];if(![name isEqual:@"play"])line(19,5,19,19);
    }else if([name isEqual:@"pause"]){UIRectFill(CGRectMake(7,5,3,14));UIRectFill(CGRectMake(14,5,3,14));}
    else if([name isEqual:@"menu"]){line(5,7,19,7);line(5,12,19,12);line(5,17,19,17);}
    else if([name isEqual:@"close"]){line(6,6,18,18);line(18,6,6,18);}
    else if([name isEqual:@"apps"]){for(int y=0;y<2;y++)for(int x=0;x<2;x++){UIBezierPath *p=[UIBezierPath bezierPathWithRoundedRect:CGRectMake(4+x*10,4+y*10,6,6) cornerRadius:1.5];p.lineWidth=1.5;[p stroke];}}
    else if([name isEqual:@"actions"]){UIBezierPath *p=[UIBezierPath bezierPath];[p moveToPoint:CGPointMake(14,3)];[p addLineToPoint:CGPointMake(5,14)];[p addLineToPoint:CGPointMake(11,14)];[p addLineToPoint:CGPointMake(10,21)];[p addLineToPoint:CGPointMake(19,10)];[p addLineToPoint:CGPointMake(13,10)];[p closePath];[p stroke];}
    else if([name isEqual:@"bell"]){UIBezierPath *p=[UIBezierPath bezierPath];[p moveToPoint:CGPointMake(5,17)];[p addLineToPoint:CGPointMake(7,14)];[p addLineToPoint:CGPointMake(7,9)];[p addArcWithCenter:CGPointMake(12,9) radius:5 startAngle:M_PI endAngle:0 clockwise:YES];[p addLineToPoint:CGPointMake(17,14)];[p addLineToPoint:CGPointMake(19,17)];[p closePath];[p stroke];line(10,21,14,21);}
    else if([name isEqual:@"wifi"]){for(int i=0;i<3;i++){UIBezierPath *p=[UIBezierPath bezierPathWithArcCenter:CGPointMake(12,20) radius:4+i*5 startAngle:-M_PI*.77 endAngle:-M_PI*.23 clockwise:YES];p.lineWidth=1.6;[p stroke];}UIRectFill(CGRectMake(11,19,2,2));}
    else if([name isEqual:@"worker"]){UIBezierPath *p=[UIBezierPath bezierPathWithRoundedRect:CGRectMake(4,6,16,14) cornerRadius:3];p.lineWidth=1.5;[p stroke];line(8,3,16,3);line(8,11,16,11);line(8,15,13,15);}
    else if([name isEqual:@"settings"]){UIBezierPath *p=[UIBezierPath bezierPathWithOvalInRect:CGRectMake(5,5,14,14)];p.lineWidth=1.5;[p stroke];[[UIBezierPath bezierPathWithOvalInRect:CGRectMake(10,10,4,4)] stroke];for(int i=0;i<8;i++){CGFloat a=i*M_PI/4;line(12+8*cos(a),12+8*sin(a),12+10*cos(a),12+10*sin(a));}}
    else if([name isEqual:@"usb"]){line(12,21,12,4);line(8,8,12,4);line(16,8,12,4);line(12,16,6,12);line(6,12,6,9);line(12,13,18,10);line(18,10,18,7);}
    else if([name isEqual:@"back"]){line(15,5,8,12);line(8,12,15,19);}
    else if([name isEqual:@"pin"]){line(9,4,16,4);line(10,4,10,10);line(15,4,15,10);line(7,13,18,13);line(10,10,7,13);line(15,10,18,13);line(12,13,12,21);}
    else {UIBezierPath *p=[UIBezierPath bezierPathWithRoundedRect:CGRectMake(5,4,14,16) cornerRadius:3];p.lineWidth=1.5;[p stroke];line(9,9,15,9);line(9,13,15,13);}
    UIImage *image=[UIGraphicsGetImageFromCurrentImageContext() imageWithRenderingMode:UIImageRenderingModeAlwaysTemplate];UIGraphicsEndImageContext();[cache setObject:image forKey:name cost:24*24*4*4];return image;
}
UIButton *BTButton(NSString *glyph,NSString *label,id target,SEL action){UIButton *b=[UIButton buttonWithType:UIButtonTypeSystem];[b setImage:BTGlyph(glyph) forState:UIControlStateNormal];b.tintColor=BTForeground();b.accessibilityLabel=label;b.accessibilityIdentifier=label;[b addTarget:target action:action forControlEvents:UIControlEventTouchUpInside];return b;}
@implementation BTActionCell
- (instancetype)initWithFrame:(CGRect)frame{if((self=[super initWithFrame:frame])){self.contentView.backgroundColor=[UIColor colorWithWhite:1 alpha:.045];self.contentView.layer.cornerRadius=14;_icon=[UIImageView new];_icon.contentMode=UIViewContentModeScaleAspectFit;_icon.tintColor=BTAccent();_title=BTLabel(13,UIFontWeightMedium);_title.textAlignment=NSTextAlignmentCenter;_detail=BTLabel(10,UIFontWeightRegular);_detail.textColor=BTMuted();_detail.textAlignment=NSTextAlignmentCenter;[self.contentView addSubview:_icon];[self.contentView addSubview:_title];[self.contentView addSubview:_detail];self.isAccessibilityElement=YES;}return self;}
- (void)layoutSubviews{[super layoutSubviews];CGFloat w=self.bounds.size.width;_icon.frame=CGRectMake((w-40)/2,14,40,40);_title.frame=CGRectMake(6,62,w-12,32);_detail.frame=CGRectMake(6,97,w-12,16);}
- (void)setHighlighted:(BOOL)highlighted{[super setHighlighted:highlighted];self.contentView.alpha=highlighted?.55:1;}
- (void)prepareForReuse{[super prepareForReuse];_icon.image=nil;_title.text=nil;_detail.text=nil;self.contentView.alpha=1;}
@end
