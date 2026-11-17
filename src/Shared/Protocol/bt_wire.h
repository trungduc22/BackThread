#ifndef BT_WIRE_H
#define BT_WIRE_H
#include <stddef.h>
#include <stdint.h>
#define BT_HEADER_SIZE 24
#define BT_MAGIC 0x31485442u
typedef struct { uint32_t length; uint16_t channel; uint32_t sequence; uint64_t timestamp; } BTHeader;
uint16_t bt_u16(const uint8_t *p);
uint32_t bt_u32(const uint8_t *p);
uint64_t bt_u64(const uint8_t *p);
void bt_put64(uint8_t *p,uint64_t n);
int bt_header_decode(const uint8_t *bytes,BTHeader *out);
int bt_header_encode(uint8_t *bytes,const BTHeader *in);
#endif
