/* Diagnose real framework/symbol availability without running the full app. */
#include <dlfcn.h>
#include <stdio.h>
#include <sys/stat.h>
#include <string.h>
#include "audit-symbols.h"
int main(void) {
    FILE *output=fopen("/var/mobile/BackThread/loader-audit.log","w");if(!output)return 1;
    fchmod(fileno(output),0600);setbuf(output,NULL);fprintf(output,"BackThread loader audit entered\n");
    for(unsigned i=0;i<sizeof(audit_libraries)/sizeof(audit_libraries[0]);i++) {
        fprintf(output,"loading %s\n",audit_libraries[i]);
        if(!dlopen(audit_libraries[i],RTLD_NOW|RTLD_GLOBAL))fprintf(output,"load-error %s\n",dlerror());
    }
    unsigned missing=0;
    for(unsigned i=0;i<sizeof(audit_symbols)/sizeof(audit_symbols[0]);i++) {
        /* dyld's special assembly entry is not an ordinary dlsym export. */
        if(!strcmp(audit_symbols[i],"dyld_stub_binder"))continue;
        if(!dlsym(RTLD_DEFAULT,audit_symbols[i])){fprintf(output,"missing %s\n",audit_symbols[i]);missing++;}
    }
    fprintf(output,"audit-complete missing=%u\n",missing);fclose(output);return missing?1:0;
}
