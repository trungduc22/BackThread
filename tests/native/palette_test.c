#include "../../src/Shared/UI/bt_palette.h"
#include <assert.h>
#include <stdio.h>

int main(void) {
    BTColor colors[2];
    uint8_t pixels[400];
    for (int i = 0; i < 100; i++) {
        pixels[i * 4] = i < 70 ? 220 : 20;
        pixels[i * 4 + 1] = 30;
        pixels[i * 4 + 2] = i < 70 ? 20 : 220;
        pixels[i * 4 + 3] = 255;
    }
    bt_art_palette(pixels, 100, colors);
    assert(colors[0].r > .8 && colors[1].b > .8);
    assert(colors[0].b < .1 && colors[1].r < .1);
    bt_art_palette(pixels, 70, colors);
    assert(colors[0].r == colors[1].r && colors[0].b == colors[1].b);

    for (int i = 0; i < 100; i++) {
        pixels[i * 4 + 3] = 0;
    }
    bt_art_palette(pixels, 100, colors);
    assert(colors[0].r == .15 && colors[1].b == .34);
    bt_art_palette(NULL, 100, colors);
    assert(colors[0].g == .32);
    bt_art_palette(pixels, 5000, colors);
    assert(colors[0].g == .32);
    puts("PASS palette: two actual colors, monochrome, transparency, missing and bounded input");
    return 0;
}
