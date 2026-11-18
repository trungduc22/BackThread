/* Minimal entry proof without UIKit/Foundation, for isolating loader/runtime startup. */
#include <unistd.h>
#include <fcntl.h>
#include <stdio.h>
#include <sys/time.h>
#include <errno.h>
int main(void) {
    const char marker[]="BackThread.Entry C main entered\n";
    (void)write(2,marker,sizeof(marker)-1);
    int fd=open("/var/mobile/BackThread/native-entry-0.1.2.json",O_WRONLY|O_CREAT|O_TRUNC|O_NOFOLLOW,0600);
    if(fd<0){char error[80];int n=snprintf(error,sizeof(error),"BackThread.Entry open failed errno=%d\n",errno);(void)write(2,error,(size_t)n);return 1;}
    struct timeval time={0};gettimeofday(&time,NULL);
    char json[160];int n=snprintf(json,sizeof(json),"{\"entryExecuted\":true,\"pid\":%d,\"uid\":%d,\"unixSeconds\":%lld}\n",getpid(),getuid(),(long long)time.tv_sec);
    int result=write(fd,json,(size_t)n)==n?0:1;close(fd);return result;
}
