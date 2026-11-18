#ifndef BT_INTERACTION_H
#define BT_INTERACTION_H
#include <math.h>
#include <stdbool.h>
/* Shared decisions; UIKit owns recognizers, views and animation lifetimes. */
static inline int bt_page_target(int page,double x,double width,double vx,double vy,bool control,bool blocked){
    if(blocked||control||width<=0||page<0||page>5||fabs(vx)<10||fabs(vx)<fabs(vy)*1.5)return -1;
    if(page==0&&x>52&&x<width-52)return -1;
    int target=page+(vx<0?1:-1);return target>=0&&target<=5?target:-1;
}
static inline bool bt_page_commit(double dx,double vx,int direction,double width,bool cancelled){return !cancelled&&width>0&&(fabs(dx)>width*.28||vx*direction< -320);}
static inline double bt_drawer_fraction(double start,double dx){double fraction=start-dx/236;return fmax(0,fmin(1,fraction));}
static inline bool bt_drawer_commit(double fraction,double vx,bool cancelled){return !cancelled&&vx<=260&&(fraction>.45||vx< -260);}
static inline int bt_notification_style(int priority,bool selected_source,bool critical_enabled){if(priority<=0)return 0;if((priority==3&&critical_enabled)||selected_source)return 3;return priority>=2?2:1;}
#endif
