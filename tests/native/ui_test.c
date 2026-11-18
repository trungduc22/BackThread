#include "../../src/Shared/UI/bt_interaction.h"
#include <assert.h>
#include <stdio.h>
int main(void){
    assert(bt_page_target(0,640,667,-30,2,false,false)==1); /* slow edge swipe */
    assert(bt_page_target(0,300,667,-800,1,false,false)==-1); /* video center */
    assert(bt_page_target(0,640,667,-800,1,true,false)==-1); /* seek/control wins */
    assert(bt_page_target(1,300,667,500,20,false,false)==0);
    assert(bt_page_target(1,300,667,-500,20,false,false)==2);
    assert(bt_page_target(5,640,667,-500,20,false,false)==-1);
    assert(bt_page_target(2,300,667,30,80,false,false)==-1);
    assert(bt_page_target(2,300,667,500,20,false,true)==-1);
    assert(bt_page_target(0,640,0,-500,20,false,false)==-1);
    assert(!bt_page_commit(-30,-40,1,667,false));
    assert(bt_page_commit(-220,-40,1,667,false));
    assert(bt_page_commit(-30,-700,1,667,false));
    assert(!bt_page_commit(-600,-700,1,667,true));
    assert(bt_page_commit(220,40,-1,667,false));
    assert(bt_drawer_fraction(0,-118)==.5);
    assert(bt_drawer_fraction(1,500)==0);
    assert(bt_drawer_fraction(0,-500)==1);
    assert(bt_drawer_commit(.6,0,false));
    assert(!bt_drawer_commit(.6,600,false));
    assert(bt_drawer_commit(.1,-600,false));
    assert(!bt_drawer_commit(.9,-600,true));
    assert(bt_notification_style(0,true,true)==0);
    assert(bt_notification_style(1,false,true)==1);
    assert(bt_notification_style(2,false,true)==2);
    assert(bt_notification_style(3,false,true)==2);
    assert(bt_notification_style(3,false,false)==2);
    assert(bt_notification_style(1,true,false)==2);
    puts("PASS 27 shared navigation/drawer/notification decisions; UIKit touch/animation acceptance is separate");return 0;
}
