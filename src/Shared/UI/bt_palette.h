#ifndef BT_PALETTE_H
#define BT_PALETTE_H
#include <stddef.h>
#include <stdint.h>
typedef struct { double r,g,b; } BTColor;
/* Bounded histogram of an already downsampled RGBA image. No allocation. */
static inline void bt_art_palette(const uint8_t *pixels,size_t count,BTColor out[2]) {
    uint32_t n[512]={0},r[512]={0},g[512]={0},b[512]={0};
    out[0]=out[1]=(BTColor){.15,.32,.34};
    if(!pixels||count>4096)return;
    for(size_t i=0;i<count;i++){const uint8_t *p=pixels+i*4;if(p[3]<192)continue;unsigned k=((p[0]>>5)<<6)|((p[1]>>5)<<3)|(p[2]>>5);n[k]++;r[k]+=p[0];g[k]+=p[1];b[k]+=p[2];}
    int first=-1,second=-1;double best=-1;
    for(int k=0;k<512;k++)if(n[k]){double rr=(double)r[k]/n[k]/255,gg=(double)g[k]/n[k]/255,bb=(double)b[k]/n[k]/255;double hi=rr>gg?rr:gg;hi=hi>bb?hi:bb;double lo=rr<gg?rr:gg;lo=lo<bb?lo:bb;double score=n[k]*(.15+hi-lo);if(hi<.08||lo>.94)score*=.05;if(score>best){best=score;first=k;}}
    if(first<0)return;
    out[0]=(BTColor){(double)r[first]/n[first]/255,(double)g[first]/n[first]/255,(double)b[first]/n[first]/255};best=-1;
    for(int k=0;k<512;k++)if(n[k]){double rr=(double)r[k]/n[k]/255,gg=(double)g[k]/n[k]/255,bb=(double)b[k]/n[k]/255;double dr=rr-out[0].r,dg=gg-out[0].g,db=bb-out[0].b,dist=dr*dr+dg*dg+db*db;if(dist<.09)continue;double hi=rr>gg?rr:gg;hi=hi>bb?hi:bb;double lo=rr<gg?rr:gg;lo=lo<bb?lo:bb;double score=n[k]*dist*(.15+hi-lo);if(hi<.08||lo>.94)score*=.05;if(score>best){best=score;second=k;}}
    out[1]=second<0?out[0]:(BTColor){(double)r[second]/n[second]/255,(double)g[second]/n[second]/255,(double)b[second]/n[second]/255};
}
#endif
