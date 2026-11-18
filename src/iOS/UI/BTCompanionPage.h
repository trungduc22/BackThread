#import <UIKit/UIKit.h>
@interface BTCompanionPage : UIViewController <UICollectionViewDataSource,UICollectionViewDelegateFlowLayout,UITableViewDataSource,UITableViewDelegate>
@property(nonatomic,copy) void (^command)(NSString *,NSDictionary *);
@property(nonatomic,copy) void (^interactionChanged)(BOOL);
@property(nonatomic) NSInteger kind;
@property(nonatomic,copy) NSString *transport;
- (void)receive:(NSString *)type body:(NSDictionary *)body;
- (void)setConnected:(BOOL)connected;
- (void)purgeIcons;
@end
