#import <UIKit/UIKit.h>
UIColor *BTBackground(void);
UIColor *BTForeground(void);
UIColor *BTMuted(void);
UIColor *BTAccent(void);
UILabel *BTLabel(CGFloat size,UIFontWeight weight);
UIImage *BTGlyph(NSString *name);
UIImage *BTThumbnail(NSString *encoded,NSUInteger pixels);
UIButton *BTButton(NSString *glyph,NSString *label,id target,SEL action);
NSString *BTString(id value,NSString *fallback);
double BTDouble(id value);
void BTAnimate(NSTimeInterval duration,void (^changes)(void),void (^completion)(BOOL));
@interface BTActionCell : UICollectionViewCell
@property(nonatomic,strong) UIImageView *icon;
@property(nonatomic,strong) UILabel *title;
@property(nonatomic,strong) UILabel *detail;
@end
