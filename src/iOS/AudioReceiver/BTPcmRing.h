#ifndef BT_PCM_RING_H
#define BT_PCM_RING_H
#include <stddef.h>
#include <stdint.h>
#define BT_PCM_CAPACITY 19200
#define BT_PCM_CAPACITY_MAX 38400
/* Caller synchronizes producer/consumer; no allocation, system calls or Objective-C in audio callbacks. */
typedef struct { uint8_t data[BT_PCM_CAPACITY_MAX]; size_t capacity, head, count; uint64_t dropped, silence; } BTPcmRing;
void bt_pcm_init(BTPcmRing *r, size_t capacity);
void bt_pcm_clear(BTPcmRing *r);
int bt_pcm_write(BTPcmRing *r, const uint8_t *data, size_t size);
size_t bt_pcm_read(BTPcmRing *r, uint8_t *data, size_t size);
#endif
