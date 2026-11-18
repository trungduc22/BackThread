#import "BTNavigation.h"
#import "BTTheme.h"
#include "../../Shared/UI/bt_interaction.h"
@implementation BTPageCoordinator {
    NSMutableDictionary *_pages;UIPanGestureRecognizer *_pan;UIPageControl *_dots;
    UIViewController *_current,*_other;NSInteger _selectedPage,_destination;CGFloat _translation;
    UIViewPropertyAnimator *_animator;NSUInteger _generation;
}
- (void)viewDidLoad{[super viewDidLoad];_pages=[NSMutableDictionary new];self.view.clipsToBounds=YES;_pan=[[UIPanGestureRecognizer alloc] initWithTarget:self action:@selector(pan:)];_pan.delegate=self;[self.view addGestureRecognizer:_pan];_dots=[UIPageControl new];_dots.numberOfPages=6;_dots.currentPageIndicatorTintColor=BTAccent();_dots.pageIndicatorTintColor=[UIColor colorWithWhite:1 alpha:.2];_dots.userInteractionEnabled=NO;_dots.alpha=0;[self.view addSubview:_dots];}
- (NSInteger)selectedPage{return _selectedPage;}
- (UIViewController *)page:(NSInteger)index{UIViewController *vc=_pages[@(index)];if(!vc){vc=self.pageFactory?self.pageFactory(index):[UIViewController new];_pages[@(index)]=vc;[self addChildViewController:vc];[self.view insertSubview:vc.view belowSubview:_dots];[vc didMoveToParentViewController:self];vc.view.hidden=YES;}return vc;}
- (void)viewDidLayoutSubviews{[super viewDidLayoutSubviews];if(!_pan.state||_pan.state==UIGestureRecognizerStatePossible){_current.view.frame=self.view.bounds;}_dots.frame=CGRectMake((self.view.bounds.size.width-140)/2,self.view.bounds.size.height-20,140,16);}
- (void)cancelAnimation{_generation++;[_animator stopAnimation:YES];_animator=nil;_other.view.hidden=YES;_other=nil;for(UIViewController *page in _pages.allValues)page.view.hidden=page!=_current;_current.view.frame=self.view.bounds;}
- (void)selectPage:(NSInteger)page animated:(BOOL)animated{
    if(page<0||page>5)return;[self loadViewIfNeeded];[self cancelAnimation];UIViewController *next=[self page:page];if(next==_current)return;
    UIViewController *old=_current;CGFloat width=self.view.bounds.size.width;NSInteger direction=page>=_selectedPage?1:-1;_selectedPage=page;_current=next;next.view.hidden=NO;next.view.frame=CGRectOffset(self.view.bounds,animated?direction*width:0,0);
    NSUInteger generation=++_generation;_dots.currentPage=page;_dots.alpha=1;
    BTAnimate(animated?.24:0,^{next.view.frame=self.view.bounds;old.view.frame=CGRectOffset(self.view.bounds,-direction*width,0);},^(BOOL done){(void)done;if(generation!=self->_generation)return;old.view.hidden=YES;if(self.pageChanged)self.pageChanged(page);UIAccessibilityPostNotification(UIAccessibilityScreenChangedNotification,next.view);[self hideDots:generation];});
}
- (void)hideDots:(NSUInteger)generation{__weak typeof(self) weak=self;dispatch_after(dispatch_time(DISPATCH_TIME_NOW,2*NSEC_PER_SEC),dispatch_get_main_queue(),^{BTPageCoordinator *owner=weak;if(owner&&generation==owner->_generation)BTAnimate(.3,^{owner->_dots.alpha=0;},nil);});}
- (BOOL)gestureRecognizerShouldBegin:(UIGestureRecognizer *)gesture{
    UIPanGestureRecognizer *pan=(id)gesture;CGPoint v=[pan velocityInView:self.view],p=[pan locationInView:self.view];BOOL control=NO;
    UIView *hit=[self.view hitTest:p withEvent:nil];for(UIView *view=hit;view&&view!=self.view;view=view.superview)if([view isKindOfClass:UIControl.class]||[view isKindOfClass:UITextView.class]||[view.accessibilityIdentifier isEqual:@"BackThread.mediaArtwork"]){control=YES;break;}
    return bt_page_target((int)_selectedPage,p.x,self.view.bounds.size.width,v.x,v.y,control,self.gesturesBlocked)>=0;
}
- (BOOL)gestureRecognizer:(UIGestureRecognizer *)gesture shouldRecognizeSimultaneouslyWithGestureRecognizer:(UIGestureRecognizer *)other{(void)gesture;return [other.view isKindOfClass:UIScrollView.class];}
- (void)pan:(UIPanGestureRecognizer *)pan{
    CGFloat width=self.view.bounds.size.width;
    if(pan.state==UIGestureRecognizerStateBegan){[self cancelAnimation];_destination=_selectedPage+([pan velocityInView:self.view].x<0?1:-1);_other=[self page:_destination];_other.view.hidden=NO;}
    if(!_other)return;NSInteger direction=_destination>_selectedPage?1:-1;_translation=[pan translationInView:self.view].x;_translation=direction>0?MAX(-width,MIN(0,_translation)):MIN(width,MAX(0,_translation));_current.view.frame=CGRectOffset(self.view.bounds,_translation,0);_other.view.frame=CGRectOffset(self.view.bounds,_translation+direction*width,0);_dots.alpha=1;
    if(pan.state==UIGestureRecognizerStateEnded||pan.state==UIGestureRecognizerStateCancelled){BOOL complete=bt_page_commit(_translation,[pan velocityInView:self.view].x,(int)direction,width,pan.state==UIGestureRecognizerStateCancelled);UIViewController *old=_current,*next=_other;NSInteger index=_destination;NSUInteger generation=++_generation;
        CGRect bounds=self.view.bounds;__weak typeof(self) weak=self;_animator=[[UIViewPropertyAnimator alloc] initWithDuration:UIAccessibilityIsReduceMotionEnabled()?0:.24 dampingRatio:.9 animations:^{old.view.frame=CGRectOffset(bounds,complete?-direction*width:0,0);next.view.frame=CGRectOffset(bounds,complete?0:direction*width,0);}];
        [_animator addCompletion:^(UIViewAnimatingPosition position){(void)position;BTPageCoordinator *owner=weak;if(!owner||generation!=owner->_generation)return;owner->_animator=nil;if(complete){old.view.hidden=YES;owner->_current=next;owner->_selectedPage=index;}else next.view.hidden=YES;owner->_other=nil;owner->_dots.currentPage=owner->_selectedPage;if(owner.pageChanged)owner.pageChanged(owner->_selectedPage);[owner hideDots:generation];}];[_animator startAnimation];
    }
}
@end
@implementation BTCornerDrawer {
    UIButton *_handle,*_shade;UIView *_panel;UILabel *_brand;NSMutableArray *_buttons;
    CGFloat _fraction,_startFraction;BOOL _open;UIViewPropertyAnimator *_animation;
}
- (instancetype)initWithFrame:(CGRect)frame{if((self=[super initWithFrame:frame])){self.backgroundColor=UIColor.clearColor;_shade=[UIButton new];_shade.backgroundColor=UIColor.blackColor;[_shade addTarget:self action:@selector(close) forControlEvents:UIControlEventTouchUpInside];[self addSubview:_shade];_panel=[UIView new];_panel.backgroundColor=[UIColor colorWithRed:.075 green:.095 blue:.11 alpha:.98];_panel.layer.cornerRadius=20;_panel.layer.borderWidth=.5;_panel.layer.borderColor=[UIColor colorWithWhite:1 alpha:.12].CGColor;[self addSubview:_panel];_brand=BTLabel(11,UIFontWeightSemibold);_brand.text=@"BACKTHREAD";_brand.textColor=BTMuted();[_panel addSubview:_brand];_buttons=[NSMutableArray new];NSArray *titles=@[@"Media",@"Apps",@"Actions",@"Worker",@"Notifications",@"Connection",@"Settings"],*glyphs=@[@"play",@"apps",@"actions",@"worker",@"bell",@"wifi",@"settings"];
        for(NSUInteger i=0;i<titles.count;i++){UIButton *b=BTButton(glyphs[i],titles[i],self,@selector(choose:));b.tag=i;[b setTitle:titles[i] forState:UIControlStateNormal];b.titleLabel.font=[UIFont systemFontOfSize:14 weight:UIFontWeightMedium];b.contentHorizontalAlignment=UIControlContentHorizontalAlignmentLeft;b.imageEdgeInsets=UIEdgeInsetsMake(0,0,0,12);b.titleEdgeInsets=UIEdgeInsetsMake(0,14,0,0);[_panel addSubview:b];[_buttons addObject:b];}
        _handle=BTButton(@"menu",@"Open navigation",self,@selector(toggle));_handle.backgroundColor=[UIColor colorWithWhite:.05 alpha:.52];_handle.layer.cornerRadius=14;_handle.imageEdgeInsets=UIEdgeInsetsMake(13,13,13,13);[self addSubview:_handle];UIPanGestureRecognizer *grip=[[UIPanGestureRecognizer alloc] initWithTarget:self action:@selector(drag:)];[_handle addGestureRecognizer:grip];UIPanGestureRecognizer *dismiss=[[UIPanGestureRecognizer alloc] initWithTarget:self action:@selector(drag:)];[_panel addGestureRecognizer:dismiss];[self layoutPanel];}return self;}
- (BOOL)open{return _open;}
- (UIView *)hitTest:(CGPoint)point withEvent:(UIEvent *)event{if(_fraction<=.001){return CGRectContainsPoint(_handle.frame,point)?[_handle hitTest:[_handle convertPoint:point fromView:self] withEvent:event]:nil;}return [super hitTest:point withEvent:event];}
- (void)layoutSubviews{[super layoutSubviews];[self layoutPanel];}
- (void)layoutPanel{CGFloat width=self.bounds.size.width,height=self.bounds.size.height,panelWidth=216,panelHeight=MIN(height-20,344);_shade.frame=self.bounds;_shade.alpha=.32*_fraction;_shade.hidden=_fraction<=0;_panel.frame=CGRectMake(width-10-panelWidth+(panelWidth+20)*(1-_fraction),(height-panelHeight)/2,panelWidth,panelHeight);_panel.hidden=_fraction<=0;_brand.frame=CGRectMake(18,15,150,18);CGFloat row=MIN(44,(panelHeight-34)/7);for(NSUInteger i=0;i<_buttons.count;i++)[_buttons[i] setFrame:CGRectMake(18,34+i*row,panelWidth-36,row)];_handle.frame=CGRectMake(width-54,6,44,44);_handle.alpha=_fraction>0?.4:.78;}
- (void)setOpen:(BOOL)open animated:(BOOL)animated{[_animation stopAnimation:YES];_animation=nil;_open=open;_fraction=open?1:0;_handle.accessibilityLabel=open?@"Close navigation":@"Open navigation";BTAnimate(animated?.25:0,^{[self layoutPanel];},nil);self.accessibilityViewIsModal=open;}
- (void)toggle{[self setOpen:!_open animated:YES];}
- (void)close{[self setOpen:NO animated:YES];}
- (void)choose:(UIButton *)button{if(self.selected)self.selected(button.tag==6?5:button.tag);[self close];}
- (void)drag:(UIPanGestureRecognizer *)pan{if(pan.state==UIGestureRecognizerStateBegan){[_animation stopAnimation:YES];_startFraction=_fraction;}CGFloat delta=[pan translationInView:self].x;_fraction=bt_drawer_fraction(_startFraction,delta);[self layoutPanel];if(pan.state==UIGestureRecognizerStateEnded||pan.state==UIGestureRecognizerStateCancelled){BOOL open=bt_drawer_commit(_fraction,[pan velocityInView:self].x,pan.state==UIGestureRecognizerStateCancelled);[self setOpen:open animated:YES];}}
@end
