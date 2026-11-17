#include "BTPcmRing.h"
#include <string.h>
void bt_pcm_clear(BTPcmRing *r) { r->head=0; r->count=0; }
int bt_pcm_write(BTPcmRing *r,const uint8_t *data,size_t size) {
    if(size%4) return -1;
    if(size>BT_PCM_CAPACITY) { r->dropped+=size-BT_PCM_CAPACITY; data+=size-BT_PCM_CAPACITY; size=BT_PCM_CAPACITY; }
    size_t drop=r->count+size>BT_PCM_CAPACITY?r->count+size-BT_PCM_CAPACITY:0;
    r->head=(r->head+drop)%BT_PCM_CAPACITY; r->count-=drop; r->dropped+=drop;
    size_t tail=(r->head+r->count)%BT_PCM_CAPACITY;
    size_t first=size<BT_PCM_CAPACITY-tail?size:BT_PCM_CAPACITY-tail;
    memcpy(r->data+tail,data,first); memcpy(r->data,data+first,size-first); r->count+=size; return 0;
}
size_t bt_pcm_read(BTPcmRing *r,uint8_t *data,size_t size) {
    if(size%4) return 0;
    size_t n=size<r->count?size:r->count;
    size_t first=n<BT_PCM_CAPACITY-r->head?n:BT_PCM_CAPACITY-r->head;
    memcpy(data,r->data+r->head,first); memcpy(data+first,r->data,n-first);
    r->head=(r->head+n)%BT_PCM_CAPACITY; r->count-=n;
    memset(data+n,0,size-n); r->silence+=size-n; return n;
}
