#include "BTPcmRing.h"
#include <string.h>
static size_t bt_pcm_capacity(const BTPcmRing *r) {
    if (r->capacity >= 1920 && r->capacity <= BT_PCM_CAPACITY_MAX && (r->capacity % 4) == 0) return r->capacity;
    return BT_PCM_CAPACITY;
}
void bt_pcm_init(BTPcmRing *r, size_t capacity) {
    memset(r, 0, sizeof(*r));
    if (capacity < 1920) capacity = BT_PCM_CAPACITY;
    if (capacity > BT_PCM_CAPACITY_MAX) capacity = BT_PCM_CAPACITY_MAX;
    capacity -= (capacity % 4);
    r->capacity = capacity;
}
void bt_pcm_clear(BTPcmRing *r) { r->head = 0; r->count = 0; }
int bt_pcm_write(BTPcmRing *r, const uint8_t *data, size_t size) {
    if (size % 4) return -1;
    size_t cap = bt_pcm_capacity(r);
    if (size > cap) { r->dropped += size - cap; data += size - cap; size = cap; }
    size_t drop = r->count + size > cap ? r->count + size - cap : 0;
    r->head = (r->head + drop) % cap; r->count -= drop; r->dropped += drop;
    size_t tail = (r->head + r->count) % cap;
    size_t first = size < cap - tail ? size : cap - tail;
    memcpy(r->data + tail, data, first); memcpy(r->data, data + first, size - first); r->count += size; return 0;
}
size_t bt_pcm_read(BTPcmRing *r, uint8_t *data, size_t size) {
    if (size % 4) return 0;
    size_t cap = bt_pcm_capacity(r);
    size_t n = size < r->count ? size : r->count;
    size_t first = n < cap - r->head ? n : cap - r->head;
    memcpy(data, r->data + r->head, first); memcpy(data + first, r->data, n - first);
    r->head = (r->head + n) % cap; r->count -= n;
    memset(data + n, 0, size - n); r->silence += size - n; return n;
}
