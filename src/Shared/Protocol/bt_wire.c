#include "bt_wire.h"
uint16_t bt_u16(const uint8_t *p) { return (uint16_t)(p[0]|((uint16_t)p[1]<<8)); }
uint32_t bt_u32(const uint8_t *p) { return (uint32_t)bt_u16(p)|((uint32_t)bt_u16(p+2)<<16); }
uint64_t bt_u64(const uint8_t *p) { return (uint64_t)bt_u32(p)|((uint64_t)bt_u32(p+4)<<32); }
static void p16(uint8_t *p,uint16_t v) { p[0]=(uint8_t)v;p[1]=(uint8_t)(v>>8); }
static void p32(uint8_t *p,uint32_t v) { p16(p,(uint16_t)v);p16(p+2,(uint16_t)(v>>16)); }
void bt_put64(uint8_t *p,uint64_t v) { p32(p,(uint32_t)v);p32(p+4,(uint32_t)(v>>32)); }
static uint32_t limit(uint16_t c) { return c==2?8192:c==6?32808:c>=1&&c<=7?65536:0; }
int bt_header_decode(const uint8_t *p,BTHeader *h) {
    if(bt_u32(p+4)!=BT_MAGIC||bt_u16(p+8)!=1) return -1;
    h->length=bt_u32(p);h->channel=bt_u16(p+10);h->sequence=bt_u32(p+12);h->timestamp=bt_u64(p+16);
    if(!limit(h->channel)||h->length>limit(h->channel)) return -1;
    return 0;
}
int bt_header_encode(uint8_t *p,const BTHeader *h) {
    if(!limit(h->channel)||h->length>limit(h->channel)) return -1;
    p32(p,h->length);p32(p+4,BT_MAGIC);p16(p+8,1);p16(p+10,h->channel);p32(p+12,h->sequence);bt_put64(p+16,h->timestamp);return 0;
}
