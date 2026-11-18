#include "../../src/Shared/Protocol/bt_wire.h"
#include "../../src/iOS/AudioReceiver/BTPcmRing.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
int main(void) {
    assert(BT_HEADER_SIZE == 24);
    assert(BT_PCM_CAPACITY == 19200);

    uint8_t header[24];
    BTHeader in = {1920, 2, 0x10203040, 0x0102030405060708ULL}, out;
    assert(bt_header_encode(header, &in) == 0);
    assert(header[4] == 'B' && header[5] == 'T' && header[6] == 'H' && header[7] == '1');
    assert(bt_header_decode(header, &out) == 0);
    assert(out.sequence == in.sequence && out.timestamp == in.timestamp && out.channel == 2);

    header[8] = 2; assert(bt_header_decode(header, &out) != 0);
    header[8] = 1; header[0] = 255; header[1] = 255; assert(bt_header_decode(header, &out) != 0);

    // Channel 7 (Media) allows up to 65536 bytes; channel 6 (Files) up to 32808; channels 0/8 invalid.
    BTHeader media_ok = {65536, 7, 1, 999}; assert(bt_header_encode(header, &media_ok) == 0 && bt_header_decode(header, &out) == 0 && out.channel == 7 && out.length == 65536);
    BTHeader media_bad = {65537, 7, 2, 999}; assert(bt_header_encode(header, &media_bad) != 0);
    BTHeader file_ok = {32808, 6, 3, 0}; assert(bt_header_encode(header, &file_ok) == 0);
    BTHeader file_bad = {32809, 6, 4, 0}; assert(bt_header_encode(header, &file_bad) != 0);
    BTHeader ch0 = {16, 0, 1, 0}; assert(bt_header_encode(header, &ch0) != 0);
    BTHeader ch8 = {16, 8, 1, 0}; assert(bt_header_encode(header, &ch8) != 0);

    BTPcmRing r; memset(&r, 0, sizeof(r));
    uint8_t input[1920], output[1920];
    for (int i = 0; i < 1920; i++) input[i] = (uint8_t)i;
    for (int i = 0; i < 15; i++) assert(bt_pcm_write(&r, input, sizeof(input)) == 0);
    assert(r.count == 19200 && r.dropped == 9600);
    for (int i = 0; i < 10; i++) {
        assert(bt_pcm_read(&r, output, sizeof(output)) == 1920);
        assert(memcmp(input, output, 1920) == 0);
    }
    assert(bt_pcm_read(&r, output, sizeof(output)) == 0);
    for (int i = 0; i < 1920; i++) assert(output[i] == 0);
    assert(r.silence == 1920);
    assert(bt_pcm_write(&r, input, 3) != 0);

    // Wrap-around preservation test
    bt_pcm_clear(&r);
    assert(r.count == 0 && r.head == 0);
    uint8_t chunk[3840];
    for (size_t i = 0; i < sizeof(chunk); i++) chunk[i] = (uint8_t)((i * 7 + 3) & 0xFF);
    for (int i = 0; i < 4; i++) assert(bt_pcm_write(&r, chunk, sizeof(chunk)) == 0); // 15360 written
    uint8_t drained[7680];
    assert(bt_pcm_read(&r, drained, sizeof(drained)) == 7680); // head at 7680, count 7680
    assert(memcmp(drained, chunk, 3840) == 0);
    assert(memcmp(drained + 3840, chunk, 3840) == 0);
    for (int i = 0; i < 3; i++) assert(bt_pcm_write(&r, chunk, sizeof(chunk)) == 0); // wraps across 19200, drops 0
    assert(r.count == 19200);
    for (int i = 0; i < 5; i++) {
        assert(bt_pcm_read(&r, drained, 3840) == 3840);
        assert(memcmp(drained, chunk, 3840) == 0);
    }
    assert(r.count == 0);

    puts("PASS native protocol and PCM ring");
    return 0;
}
