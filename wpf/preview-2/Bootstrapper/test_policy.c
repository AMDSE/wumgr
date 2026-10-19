#include <stdio.h>
#include <string.h>
#include "bootstrap_policy.h"
#define T(x) do { total++; if (!(x)) { printf("FAIL: %s\n", #x); return 1; } passed++; } while (0)
int main(void) {
    int total = 0, passed = 0;
    T(!wumgr_release_ready(0));
    T(!wumgr_release_ready(461808));
    T(!wumgr_release_ready(528039));
    T(wumgr_release_ready(528040));
    T(wumgr_release_ready(528049));
    T(wumgr_release_ready(528372));
    T(wumgr_release_ready(533320));
    T(wumgr_release_ready(533509));
    T(wumgr_release_ready(UINT32_MAX));
    T(wumgr_install_exit_allows_recheck(0));
    const uint32_t stops[] = {1, 1223, 1602, 1603, 1641, 3010, 5100, UINT32_MAX};
    for (size_t i = 0; i < sizeof(stops)/sizeof(stops[0]); ++i)
        T(!wumgr_install_exit_allows_recheck(stops[i]));
    T(wumgr_install_exit_needs_reboot(3010));
    T(wumgr_install_exit_needs_reboot(1641));
    T(!wumgr_install_exit_needs_reboot(0));
    T(!wumgr_install_exit_needs_reboot(1603));
    T(!wumgr_install_exit_needs_reboot(1223));
    T(!wumgr_is_allowed_url(NULL));
    const char *bad[] = {
        "", "h", "http", "https:", "https:/", "https://", "https://d", 
        "http://download.microsoft.com/a.exe", "https://download.microsoft.com.evil/a",
        "https://evil-download.microsoft.com/a", "https://download.microsoft.com:443/a",
        "https://user@download.microsoft.com/a", "https://download.microsoft.com@evil/a",
        "https://download.microsoft.co/a", "file:///download.microsoft.com/a",
        "https://go.microsoft.com/fwlink/?linkid=2088631"
    };
    for (size_t i = 0; i < sizeof(bad)/sizeof(bad[0]); ++i) T(!wumgr_is_allowed_url(bad[i]));
    T(wumgr_is_allowed_url(WUMGR_DOTNET_URL));
    T(wumgr_is_allowed_url("https://download.microsoft.com/a.exe?x=1"));
    T(wumgr_is_allowed_url("https://DOWNLOAD.MICROSOFT.COM/a.exe"));
    /* Long host inputs used to risk reading beyond the allowed-host constant. */
    char input[300];
    for (size_t n = 1; n <= 270; ++n) {
        memcpy(input, "https://", 8);
        memset(input + 8, 'x', n);
        input[n + 8] = '\0';
        T(!wumgr_is_allowed_url(input));
    }
    printf("%d/%d policy tests passed\n", passed, total);
    return 0;
}
