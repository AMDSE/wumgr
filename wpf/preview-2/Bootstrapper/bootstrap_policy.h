#ifndef WUMGR_BOOTSTRAP_POLICY_H
#define WUMGR_BOOTSTRAP_POLICY_H
#include <stddef.h>
#include <stdint.h>
#include <string.h>
#define WUMGR_DOTNET_RELEASE_MIN 528040u
#define WUMGR_INSTALL_SUCCESS 0u
#define WUMGR_INSTALL_REBOOT_REQUIRED 3010u
#define WUMGR_INSTALL_REBOOT_INITIATED 1641u
#define WUMGR_WPF_EXE L"WuMgr.Wpf.exe"
#define WUMGR_DOTNET_URL "https://download.microsoft.com/download/f/3/a/f3a6af84-da23-40a5-8d1c-49cc10c8e76f/NDP48-x86-x64-AllOS-ENU.exe"
#define WUMGR_ALLOWED_HOST "download.microsoft.com"
static inline int wumgr_release_ready(uint32_t release) { return release >= WUMGR_DOTNET_RELEASE_MIN; }
static inline int wumgr_install_exit_allows_recheck(uint32_t code) { return code == WUMGR_INSTALL_SUCCESS; }
static inline int wumgr_install_exit_needs_reboot(uint32_t code) { return code == WUMGR_INSTALL_REBOOT_REQUIRED || code == WUMGR_INSTALL_REBOOT_INITIATED; }
/* Exact https host allowlist. URL length is checked before every indexed access. */
static inline int wumgr_is_allowed_url(const char *url) {
    const char *p,*host,*end; size_t n,i;
    if (!url) return 0;
    n=0;
    while (url[n] && n<8) n++;
    if (n<8) return 0;
    if (url[0]!='h'||url[1]!='t'||url[2]!='t'||url[3]!='p'||url[4]!='s'||url[5]!=':'||url[6]!='/'||url[7]!='/') return 0;
    p=url+8; host=p; while (*p&&*p!='/'&&*p!='?'&&*p!='#'&&*p!=':') p++; if (*p==':') return 0; end=p; n=(size_t)(end-host); if (n==0||n>255) return 0;
    if (n != strlen(WUMGR_ALLOWED_HOST)) return 0;
    for(i=0;i<n;i++){char c=host[i];if(c>='A'&&c<='Z')c=(char)(c+32);if(c!=WUMGR_ALLOWED_HOST[i])return 0;} return WUMGR_ALLOWED_HOST[n]=='\0';
}
#endif
