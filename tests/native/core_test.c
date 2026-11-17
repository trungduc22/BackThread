#include "../../src/Shared/Protocol/bt_wire.h"
#include "../../src/iOS/AudioReceiver/BTPcmRing.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
int main(void) {
    uint8_t header[24];BTHeader in={1920,2,0x10203040,0x0102030405060708ULL},out;
    assert(bt_header_encode(header,&in)==0);assert(header[4]=='B'&&header[5]=='T'&&header[6]=='H'&&header[7]=='1');assert(bt_header_decode(header,&out)==0);assert(out.sequence==in.sequence&&out.timestamp==in.timestamp);
    header[8]=2;assert(bt_header_decode(header,&out)!=0);header[8]=1;header[0]=255;header[1]=255;assert(bt_header_decode(header,&out)!=0);
    BTPcmRing r;memset(&r,0,sizeof(r));uint8_t input[1920],output[1920];for(int i=0;i<1920;i++)input[i]=(uint8_t)i;
    for(int i=0;i<15;i++)assert(bt_pcm_write(&r,input,sizeof(input))==0);
    assert(r.count==19200&&r.dropped==9600);
    for(int i=0;i<10;i++){assert(bt_pcm_read(&r,output,sizeof(output))==1920);assert(memcmp(input,output,1920)==0);}
    assert(bt_pcm_read(&r,output,sizeof(output))==0);for(int i=0;i<1920;i++)assert(output[i]==0);assert(r.silence==1920);assert(bt_pcm_write(&r,input,3)!=0);
    puts("PASS native protocol and PCM ring");return 0;
}
