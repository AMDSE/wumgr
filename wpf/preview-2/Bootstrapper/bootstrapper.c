/* WuMgr WPF native prerequisite launcher. GPL-3.0, see ../LICENSE. */
#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winhttp.h>
#include <wintrust.h>
#include <softpub.h>
#include <wincrypt.h>
#include <shellapi.h>
#include <shlwapi.h>
#include <shlobj.h>
#include <strsafe.h>
#include <wchar.h>
#include <string.h>
#include "bootstrap_policy.h"

#define PACKAGE_BYTES 121346568ULL
#define WM_STATUS (WM_APP + 1)
#define IDC_CANCEL 1001
#define PHASE_DOWNLOAD 0
#define PHASE_VERIFY 1
#define PHASE_INSTALL 2
#define PHASE_DONE 3
/* Hash of the fixed official Microsoft installer, not a user-supplied file. */
static const BYTE package_hash[32] = {
    0x0a,0x3a,0x39,0x0c,0x47,0xe6,0x39,0xd0,0xf7,0xfc,0x65,0xb2,0x11,0x95,0xfe,0xe6,
    0xb7,0xf6,0x5b,0x06,0x6f,0x80,0xf7,0x0c,0x60,0xfa,0xb1,0x91,0xd1,0x4b,0x7e,0x40
};
static HWND window_handle, status_control, cancel_control;
static HANDLE cancel_event, worker_handle;
static CRITICAL_SECTION state_lock;
static int phase, finished;
static DWORD result_code;
static ULONGLONG downloaded;
static wchar_t status_text[512], failure_text[512];

static void message(const wchar_t *text, UINT icon) {
    MessageBoxW(window_handle && IsWindow(window_handle) ? window_handle : NULL,
                text, L"WuMgr WPF", MB_OK | icon);
}
static void log_line(const wchar_t *text) {
    wchar_t base[MAX_PATH], path[MAX_PATH], line[1200];
    char utf8[4800]; DWORD written;
    DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", base, ARRAYSIZE(base));
    if (!n || n >= ARRAYSIZE(base)) return;
    if (FAILED(StringCchPrintfW(path, ARRAYSIZE(path), L"%s\\WuMgr\\Logs", base))) return;
    if (SHCreateDirectoryExW(NULL, path, NULL) != ERROR_SUCCESS && GetFileAttributesW(path) == INVALID_FILE_ATTRIBUTES) return;
    if (FAILED(StringCchCatW(path, ARRAYSIZE(path), L"\\bootstrapper.log"))) return;
    SYSTEMTIME t; GetLocalTime(&t);
    StringCchPrintfW(line, ARRAYSIZE(line), L"%04u-%02u-%02u %02u:%02u:%02u %s\r\n",
                    t.wYear,t.wMonth,t.wDay,t.wHour,t.wMinute,t.wSecond,text);
    int length = WideCharToMultiByte(CP_UTF8, 0, line, -1, utf8, sizeof(utf8), NULL, NULL);
    HANDLE h = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                           NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h != INVALID_HANDLE_VALUE) {
        if (length > 1) WriteFile(h, utf8, (DWORD)length - 1, &written, NULL);
        CloseHandle(h);
    }
}
static void status(const wchar_t *text, int new_phase) {
    EnterCriticalSection(&state_lock);
    phase = new_phase;
    StringCchCopyW(status_text, ARRAYSIZE(status_text), text);
    LeaveCriticalSection(&state_lock);
    if (window_handle) PostMessageW(window_handle, WM_STATUS, 0, 0);
    log_line(text);
}
static void fail(const wchar_t *text, DWORD error) {
    StringCchPrintfW(failure_text, ARRAYSIZE(failure_text), L"%s\n错误代码：%lu。可解决问题后重新打开 WuMgr.exe。", text, error);
    log_line(failure_text);
}
static int is_cancelled(void) { return WaitForSingleObject(cancel_event, 0) == WAIT_OBJECT_0; }
static int join_path(wchar_t *out, size_t cap, const wchar_t *directory, const wchar_t *name) {
    return SUCCEEDED(StringCchPrintfW(out, cap, L"%s\\%s", directory, name));
}
static int os_version(DWORD *build) {
    typedef LONG (WINAPI *VersionFunction)(PRTL_OSVERSIONINFOW);
    VersionFunction fn = (VersionFunction)GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion");
    RTL_OSVERSIONINFOW version = {0}; version.dwOSVersionInfoSize = sizeof(version);
    if (!fn || fn(&version) != 0 || version.dwMajorVersion < 10) return 0;
    *build = version.dwBuildNumber;
    return 1;
}
/* 1=ready, 0=missing/older, -1=unreadable. Registry failure never means install. */
static int release_view(REGSAM view) {
    HKEY key; DWORD release = 0, size = sizeof(release);
    LONG error = RegOpenKeyExW(HKEY_LOCAL_MACHINE,
        L"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full", 0, KEY_QUERY_VALUE | view, &key);
    if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND || error == ERROR_INVALID_PARAMETER) return 0;
    if (error != ERROR_SUCCESS) return -1;
    error = RegGetValueW(key, NULL, L"Release", RRF_RT_REG_DWORD, NULL, &release, &size);
    RegCloseKey(key);
    if (error == ERROR_FILE_NOT_FOUND) return 0;
    if (error != ERROR_SUCCESS) return -1;
    return wumgr_release_ready(release) ? 1 : 0;
}
static int runtime_state(void) {
    int a = release_view(KEY_WOW64_32KEY), b = release_view(KEY_WOW64_64KEY);
    if (a == 1 || b == 1) return 1;
    return a < 0 || b < 0 ? -1 : 0;
}
static int verify_hash(HANDLE file) {
    HCRYPTPROV provider = 0; HCRYPTHASH hash = 0;
    BYTE buffer[65536], actual[32]; DWORD size = sizeof(actual), count;
    int ok = 0;
    if (!CryptAcquireContextW(&provider, NULL, NULL, PROV_RSA_AES, CRYPT_VERIFYCONTEXT)) goto done;
    if (!CryptCreateHash(provider, CALG_SHA_256, 0, 0, &hash)) goto done;
    LARGE_INTEGER zero; zero.QuadPart = 0;
    if (!SetFilePointerEx(file, zero, NULL, FILE_BEGIN)) goto done;
    for (;;) {
        if (is_cancelled() || !ReadFile(file, buffer, sizeof(buffer), &count, NULL)) goto done;
        if (!count) break;
        if (!CryptHashData(hash, buffer, count, 0)) goto done;
    }
    if (!CryptGetHashParam(hash, HP_HASHVAL, actual, &size, 0) || size != sizeof(actual)) goto done;
    ok = memcmp(actual, package_hash, sizeof(actual)) == 0;
done:
    if (hash) CryptDestroyHash(hash);
    if (provider) CryptReleaseContext(provider, 0);
    return ok;
}
static int verify_microsoft(const wchar_t *file, HANDLE held_file) {
    WINTRUST_FILE_INFO info = {0}; info.cbStruct = sizeof(info); info.pcwszFilePath = file; info.hFile = held_file;
    WINTRUST_DATA trust = {0}; trust.cbStruct = sizeof(trust);
    GUID action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
    trust.dwUIChoice = WTD_UI_NONE; trust.fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN;
    trust.dwUnionChoice = WTD_CHOICE_FILE; trust.pFile = &info;
    trust.dwProvFlags = WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT;
    trust.dwStateAction = WTD_STATEACTION_VERIFY;
    LONG error = WinVerifyTrust(NULL, &action, &trust);
    int ok = 0;
    if (error == ERROR_SUCCESS) {
        CRYPT_PROVIDER_DATA *data = WTHelperProvDataFromStateData(trust.hWVTStateData);
        CRYPT_PROVIDER_SGNR *signer = data ? WTHelperGetProvSignerFromChain(data, 0, FALSE, 0) : NULL;
        if (signer && signer->csCertChain && signer->pasCertChain[0].pCert) {
            wchar_t name[256];
            DWORD length = CertGetNameStringW(signer->pasCertChain[0].pCert,
                CERT_NAME_SIMPLE_DISPLAY_TYPE, 0, NULL, name, ARRAYSIZE(name));
            ok = length > 1 && wcscmp(name, L"Microsoft Corporation") == 0;
        }
    }
    trust.dwStateAction = WTD_STATEACTION_CLOSE;
    WinVerifyTrust(NULL, &action, &trust);
    return ok;
}
static int download(const wchar_t *file) {
    if (!wumgr_is_allowed_url(WUMGR_DOTNET_URL)) { fail(L"下载地址不符合安全策略。", ERROR_INVALID_PARAMETER); return 0; }
    const wchar_t *host = L"download.microsoft.com";
    const wchar_t *target = L"/download/f/3/a/f3a6af84-da23-40a5-8d1c-49cc10c8e76f/NDP48-x86-x64-AllOS-ENU.exe";
    HINTERNET session = NULL, connection = NULL, request = NULL;
    HANDLE output = INVALID_HANDLE_VALUE;
    int ok = 0; DWORD code = 0, size, content_length = 0;
    DWORD redirect = WINHTTP_OPTION_REDIRECT_POLICY_NEVER;
    session = WinHttpOpen(L"WuMgr Preview 2", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, NULL, NULL, 0);
    if (!session) goto done;
    if (!WinHttpSetTimeouts(session, 15000, 15000, 15000, 15000)) goto done;
    if (!WinHttpSetOption(session, WINHTTP_OPTION_REDIRECT_POLICY, &redirect, sizeof(redirect))) goto done;
    connection = WinHttpConnect(session, host, INTERNET_DEFAULT_HTTPS_PORT, 0);
    if (!connection) goto done;
    request = WinHttpOpenRequest(connection, L"GET", target, NULL, WINHTTP_NO_REFERER,
                                WINHTTP_DEFAULT_ACCEPT_TYPES, WINHTTP_FLAG_SECURE);
    if (!request || !WinHttpSendRequest(request, NULL, 0, NULL, 0, 0, 0) || !WinHttpReceiveResponse(request, NULL)) goto done;
    size = sizeof(code);
    if (!WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
        WINHTTP_HEADER_NAME_BY_INDEX, &code, &size, WINHTTP_NO_HEADER_INDEX) || code != 200) goto done;
    size = sizeof(content_length);
    if (WinHttpQueryHeaders(request, WINHTTP_QUERY_CONTENT_LENGTH | WINHTTP_QUERY_FLAG_NUMBER,
        WINHTTP_HEADER_NAME_BY_INDEX, &content_length, &size, WINHTTP_NO_HEADER_INDEX) && content_length != PACKAGE_BYTES) goto done;
    output = CreateFileW(file, GENERIC_WRITE, 0, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    if (output == INVALID_HANDLE_VALUE) goto done;
    ULONGLONG total = 0;
    for (;;) {
        BYTE buffer[65536]; DWORD count = 0, written = 0;
        if (is_cancelled()) goto done;
        if (!WinHttpReadData(request, buffer, sizeof(buffer), &count)) goto done;
        if (!count) break;
        total += count;
        if (total > PACKAGE_BYTES || !WriteFile(output, buffer, count, &written, NULL) || written != count) goto done;
        EnterCriticalSection(&state_lock); downloaded = total; LeaveCriticalSection(&state_lock);
    }
    if (total != PACKAGE_BYTES || !FlushFileBuffers(output)) goto done;
    ok = 1;
done:
    if (!ok && !is_cancelled()) fail(L"无法完整下载微软运行环境安装包。请检查网络、代理、磁盘空间。", GetLastError());
    if (output != INVALID_HANDLE_VALUE) CloseHandle(output);
    if (request) WinHttpCloseHandle(request);
    if (connection) WinHttpCloseHandle(connection);
    if (session) WinHttpCloseHandle(session);
    return ok;
}
static DWORD install_runtime(const wchar_t *file) {
    SHELLEXECUTEINFOW execute = {0}; execute.cbSize = sizeof(execute);
    execute.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC;
    execute.lpVerb = L"runas"; execute.lpFile = file;
    execute.lpParameters = L"/passive /norestart"; execute.nShow = SW_SHOWNORMAL;
    if (!ShellExecuteExW(&execute)) {
        DWORD error = GetLastError();
        fail(error == ERROR_CANCELLED ? L"管理员授权已取消，未启动运行环境安装。" : L"无法启动微软运行环境安装器。", error);
        return error == ERROR_CANCELLED ? ERROR_CANCELLED : ERROR_INSTALL_FAILURE;
    }
    if (!execute.hProcess) { fail(L"未取得安装器进程句柄。", ERROR_INVALID_HANDLE); return ERROR_INSTALL_FAILURE; }
    DWORD code = ERROR_INSTALL_FAILURE;
    if (WaitForSingleObject(execute.hProcess, INFINITE) != WAIT_OBJECT_0 || !GetExitCodeProcess(execute.hProcess, &code)) {
        fail(L"无法取得安装器结果，请确认系统安装状态后重试。", GetLastError());
        code = ERROR_INSTALL_FAILURE;
    }
    CloseHandle(execute.hProcess);
    if (!wumgr_install_exit_allows_recheck(code) && !wumgr_install_exit_needs_reboot(code))
        fail(code == ERROR_INSTALL_USEREXIT ? L"微软运行环境安装已取消。" : L"微软运行环境安装失败。", code);
    return code;
}
static DWORD WINAPI worker(void *argument) {
    const wchar_t *directory = (const wchar_t *)argument;
    wchar_t file[MAX_PATH] = {0}; HANDLE locked = INVALID_HANDLE_VALUE;
    DWORD code = ERROR_INSTALL_FAILURE;
    HRESULT com = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    if (!join_path(file, ARRAYSIZE(file), directory, L"NDP48.exe")) { fail(L"临时路径过长。", ERROR_FILENAME_EXCED_RANGE); goto done; }
    status(L"缺少 .NET Framework 4.8，正在从 Microsoft 下载约 116 MiB…", PHASE_DOWNLOAD);
    if (!download(file)) goto done;
    if (is_cancelled()) goto done;
    /* Lock BEFORE verifying hash/trust and keep it until the installer exits.
       Any replacement before this lock fails the pinned SHA-256 check. */
    locked = CreateFileW(file, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (locked == INVALID_HANDLE_VALUE) { fail(L"无法锁定安装包以安全验证。", GetLastError()); goto done; }
    status(L"正在核对 SHA-256 和 Microsoft 数字签名…", PHASE_VERIFY);
    if (!verify_hash(locked) || SetFilePointer(locked, 0, NULL, FILE_BEGIN) == INVALID_SET_FILE_POINTER || !verify_microsoft(file, locked)) {
        if (!is_cancelled()) fail(L"微软安装包完整性或签名验证失败，已拒绝执行。", ERROR_INVALID_EXE_SIGNATURE);
        goto done;
    }
    /* Serialize the last cancellation check and transition into installation. */
    EnterCriticalSection(&state_lock);
    if (is_cancelled()) { LeaveCriticalSection(&state_lock); goto done; }
    phase = PHASE_INSTALL;
    StringCchCopyW(status_text, ARRAYSIZE(status_text), L"即将请求管理员授权；正在安装，请等待完成…");
    LeaveCriticalSection(&state_lock);
    PostMessageW(window_handle, WM_STATUS, 0, 0);
    log_line(L"Validated Microsoft installer; requesting administrator approval");
    code = install_runtime(file);
done:
    if (locked != INVALID_HANDLE_VALUE) CloseHandle(locked);
    if (*file && GetFileAttributesW(file) != INVALID_FILE_ATTRIBUTES && !DeleteFileW(file)) log_line(L"Could not remove temporary installer");
    if (!RemoveDirectoryW(directory)) log_line(L"Could not remove temporary directory");
    if (SUCCEEDED(com)) CoUninitialize();
    if (is_cancelled() && code == ERROR_INSTALL_FAILURE) code = ERROR_INSTALL_USEREXIT;
    EnterCriticalSection(&state_lock); result_code = code; phase = PHASE_DONE; finished = 1; LeaveCriticalSection(&state_lock);
    PostMessageW(window_handle, WM_STATUS, 0, 0);
    return 0;
}
static void request_cancel(void) {
    EnterCriticalSection(&state_lock);
    if (phase < PHASE_INSTALL) {
        SetEvent(cancel_event);
        StringCchCopyW(status_text, ARRAYSIZE(status_text), L"正在取消，请等待当前网络请求结束…");
    }
    LeaveCriticalSection(&state_lock);
    EnableWindow(cancel_control, FALSE);
    /* Never destroy the window or free shared state while the worker is alive. */
    PostMessageW(window_handle, WM_STATUS, 0, 0);
}
static LRESULT CALLBACK window_proc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    if (msg == WM_CLOSE || (msg == WM_COMMAND && LOWORD(wp) == IDC_CANCEL)) { request_cancel(); return 0; }
    if (msg == WM_STATUS || msg == WM_TIMER) {
        wchar_t text[512]; int current_phase, done; ULONGLONG bytes;
        EnterCriticalSection(&state_lock);
        current_phase = phase; done = finished; bytes = downloaded;
        StringCchCopyW(text, ARRAYSIZE(text), status_text);
        LeaveCriticalSection(&state_lock);
        if (done) { DestroyWindow(hwnd); return 0; }
        if (current_phase == PHASE_DOWNLOAD && bytes && !is_cancelled())
            StringCchPrintfW(text, ARRAYSIZE(text), L"正在从 Microsoft 下载… %llu / 116 MiB", bytes / (1024ULL * 1024ULL));
        SetWindowTextW(status_control, text);
        EnableWindow(cancel_control, current_phase < PHASE_INSTALL && !is_cancelled());
        return 0;
    }
    if (msg == WM_DESTROY) { PostQuitMessage(0); return 0; }
    return DefWindowProcW(hwnd, msg, wp, lp);
}
static int launch_wpf(const wchar_t *directory, LPCWSTR raw) {
    wchar_t file[MAX_PATH], command[32768];
    if (!join_path(file, ARRAYSIZE(file), directory, WUMGR_WPF_EXE)) return 0;
    if (FAILED(StringCchPrintfW(command, ARRAYSIZE(command), L"\"%s\"%s%s", file, raw && *raw ? L" " : L"", raw ? raw : L""))) return 0;
    STARTUPINFOW startup = {0}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION process = {0};
    if (!CreateProcessW(file, command, NULL, NULL, FALSE, 0, NULL, directory, &startup, &process)) return 0;
    CloseHandle(process.hThread); CloseHandle(process.hProcess); return 1;
}
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, LPWSTR raw, int show) {
    (void)previous; (void)show;
    wchar_t directory[MAX_PATH], file[MAX_PATH], temp[MAX_PATH], guid_text[40], private_dir[MAX_PATH];
    DWORD build = 0; HANDLE mutex = NULL; int exit_code = 1;
    DWORD length = GetModuleFileNameW(NULL, directory, ARRAYSIZE(directory));
    if (!length || length >= ARRAYSIZE(directory) || !PathRemoveFileSpecW(directory) ||
        !join_path(file, ARRAYSIZE(file), directory, WUMGR_WPF_EXE) ||
        GetFileAttributesW(file) == INVALID_FILE_ATTRIBUTES || (GetFileAttributesW(file) & FILE_ATTRIBUTE_DIRECTORY)) {
        message(L"应用目录缺少 WuMgr.Wpf.exe，或路径过长。请解压完整包后再运行；未尝试安装。", MB_ICONERROR); return 2;
    }
    if (!os_version(&build)) { message(L"此预览版支持 Windows 10 / 11。", MB_ICONERROR); return 2; }
    int state = runtime_state();
    if (state < 0) { message(L"无法可靠读取 .NET Framework 状态（权限或注册表错误），已停止，未尝试安装。", MB_ICONERROR); return 4; }
    if (state == 1) {
        if (launch_wpf(directory, raw)) return 0;
        message(L"环境已检测到，但无法打开 WuMgr.Wpf.exe。请确认完整解压且未被安全策略阻止。", MB_ICONERROR); return 8;
    }
    SYSTEM_INFO system; GetNativeSystemInfo(&system);
    if (build < 14393 || (system.wProcessorArchitecture != PROCESSOR_ARCHITECTURE_INTEL && system.wProcessorArchitecture != PROCESSOR_ARCHITECTURE_AMD64)) {
        message(L"自动补装支持 Windows 10 1607 及以后的 x86/x64 系统。此系统暂不支持自动补装，请先通过 Windows 或微软官方渠道安装兼容环境。", MB_ICONERROR); return 2;
    }
    mutex = CreateMutexW(NULL, FALSE, L"Local\\WuMgr.Preview2.RuntimeSetup");
    if (!mutex || GetLastError() == ERROR_ALREADY_EXISTS) {
        message(L"另一个 WuMgr 运行环境安装流程正在运行，请等待它完成。", MB_ICONINFORMATION);
        if (mutex) CloseHandle(mutex);
        return 3;
    }
    GUID guid; length = GetTempPathW(ARRAYSIZE(temp), temp);
    if (!length || length >= ARRAYSIZE(temp) || CoCreateGuid(&guid) != S_OK || !StringFromGUID2(&guid, guid_text, ARRAYSIZE(guid_text)) ||
        FAILED(StringCchPrintfW(private_dir, ARRAYSIZE(private_dir), L"%sWuMgr-%s", temp, guid_text)) || !CreateDirectoryW(private_dir, NULL)) {
        message(L"无法创建独立临时目录，未启动安装。", MB_ICONERROR); CloseHandle(mutex); return 5;
    }
    InitializeCriticalSection(&state_lock);
    cancel_event = CreateEventW(NULL, TRUE, FALSE, NULL);
    WNDCLASSW cls = {0}; cls.lpfnWndProc = window_proc; cls.hInstance = instance;
    cls.lpszClassName = L"WuMgrRuntimeSetup"; cls.hCursor = LoadCursor(NULL, IDC_ARROW);
    cls.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
    if (!cancel_event || !RegisterClassW(&cls)) goto ui_failure;
    window_handle = CreateWindowExW(0, cls.lpszClassName, L"WuMgr — 运行环境准备", WS_CAPTION | WS_SYSMENU,
        CW_USEDEFAULT, CW_USEDEFAULT, 540, 210, NULL, NULL, instance, NULL);
    if (!window_handle) goto ui_failure;
    status_control = CreateWindowW(L"STATIC", L"缺少 .NET Framework 4.8。将自动下载微软安装包，验证后请求管理员授权。",
        WS_VISIBLE | WS_CHILD, 20, 20, 485, 85, window_handle, NULL, instance, NULL);
    cancel_control = CreateWindowW(L"BUTTON", L"取消下载", WS_VISIBLE | WS_CHILD | BS_PUSHBUTTON,
        205, 120, 110, 30, window_handle, (HMENU)(INT_PTR)IDC_CANCEL, instance, NULL);
    if (!status_control || !cancel_control || !SetTimer(window_handle, 1, 250, NULL)) goto ui_failure;
    SendMessageW(status_control, WM_SETFONT, (WPARAM)GetStockObject(DEFAULT_GUI_FONT), TRUE);
    SendMessageW(cancel_control, WM_SETFONT, (WPARAM)GetStockObject(DEFAULT_GUI_FONT), TRUE);
    ShowWindow(window_handle, SW_SHOW); UpdateWindow(window_handle);
    worker_handle = CreateThread(NULL, 0, worker, private_dir, 0, NULL);
    if (!worker_handle) goto ui_failure;
    MSG event; int received;
    while ((received = GetMessageW(&event, NULL, 0, 0)) > 0) { TranslateMessage(&event); DispatchMessageW(&event); }
    if (received == -1) SetEvent(cancel_event);
    /* Also wait if the UI loop stops unexpectedly: installation must not be orphaned. */
    WaitForSingleObject(worker_handle, INFINITE); CloseHandle(worker_handle);
    window_handle = NULL;
    if (wumgr_install_exit_needs_reboot(result_code)) {
        message(L".NET Framework 安装完成，需要手动重启 Windows。程序不会自动重启；重启后请再次运行 WuMgr.exe。", MB_ICONINFORMATION);
        exit_code = 3010;
    } else if (result_code == ERROR_INSTALL_USEREXIT || result_code == ERROR_CANCELLED) {
        message(L"运行环境准备已取消，未打开 WPF。可以稍后重新运行。", MB_ICONINFORMATION); exit_code = 6;
    } else if (result_code != ERROR_SUCCESS) {
        message(*failure_text ? failure_text : L"运行环境准备失败，未打开 WPF。", MB_ICONERROR); exit_code = 6;
    } else if (runtime_state() != 1) {
        message(L"安装器已结束，但仍未检测到 .NET Framework 4.8。请查看系统安装结果，必要时手动重启后重试。", MB_ICONERROR); exit_code = 7;
    } else if (!launch_wpf(directory, raw)) {
        message(L"运行环境已安装，但无法打开 WuMgr.Wpf.exe。", MB_ICONERROR); exit_code = 8;
    } else exit_code = 0;
    goto cleanup;
ui_failure:
    message(L"无法启动运行环境准备窗口或工作线程，未尝试安装。", MB_ICONERROR);
    if (window_handle && IsWindow(window_handle)) DestroyWindow(window_handle);
    RemoveDirectoryW(private_dir); exit_code = 5;
cleanup:
    if (cancel_event) CloseHandle(cancel_event);
    DeleteCriticalSection(&state_lock); CloseHandle(mutex);
    return exit_code;
}
