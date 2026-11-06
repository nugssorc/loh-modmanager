/* 최소 Win32 선언 (SDK·CRT 없이 빌드하기 위함) */
#ifndef LOH_WIN_H
#define LOH_WIN_H
typedef unsigned char u8; typedef unsigned short u16; typedef unsigned int u32;
typedef unsigned long long u64; typedef long long i64; typedef u16 wch;
typedef void *HANDLE; typedef u64 uptr;
#define W __declspec(dllimport)
#define API __stdcall
#define INVALID_HANDLE ((HANDLE)(i64)-1)

typedef struct { u16 y, mo, dow, d, h, mi, s, ms; } SYSTEMTIME;
typedef struct { int l, t, r, b; } RECT;
typedef struct { u32 code, flags; void *rec; void *addr; u32 nparams; u32 pad; uptr info[15]; } EXCEPTION_RECORD;
typedef struct { EXCEPTION_RECORD *rec; void *ctx; } EXCEPTION_POINTERS;

W HANDLE API GetModuleHandleW(const wch *);
W HANDLE API GetModuleHandleA(const char *);
W HANDLE API LoadLibraryA(const char *);
W void * API GetProcAddress(HANDLE, const char *);
W HANDLE API CreateThread(void *, uptr, u32 (API *)(void *), void *, u32, u32 *);
W void   API Sleep(u32);
W u32    API GetCurrentProcessId(void);
W u32    API GetCurrentThreadId(void);
W HANDLE API CreateFileW(const wch *, u32, u32, void *, u32, u32, HANDLE);
W int    API WriteFile(HANDLE, const void *, u32, u32 *, void *);
W int    API ReadFile(HANDLE, void *, u32, u32 *, void *);
W int    API CloseHandle(HANDLE);
W int    API FlushFileBuffers(HANDLE);
W u32    API SetFilePointer(HANDLE, int, int *, u32);
W u32    API GetEnvironmentVariableW(const wch *, wch *, u32);
W u32    API GetModuleFileNameW(HANDLE, wch *, u32);
W int    API CreateDirectoryW(const wch *, void *);
W void   API GetLocalTime(SYSTEMTIME *);
W int    API Beep(u32, u32);
W int    API WideCharToMultiByte(u32, u32, const wch *, int, char *, int, const char *, int *);
W HANDLE API CreateMutexW(void *, int, const wch *);
W u32    API GetLastError(void);
W void * API AddVectoredExceptionHandler(u32, int (API *)(EXCEPTION_POINTERS *));
W u32    API RemoveVectoredExceptionHandler(void *);
W void * API VirtualAlloc(void *, uptr, u32, u32);
W int    API VirtualFree(void *, uptr, u32);
W int    API VirtualProtect(void *, uptr, u32, u32 *);
W uptr   API VirtualQuery(const void *, void *, uptr);
W int    API FlushInstructionCache(HANDLE, const void *, uptr);
W HANDLE API GetCurrentProcess(void);
W int    API DisableThreadLibraryCalls(HANDLE);
W u32    API GetTickCount(void);
/* loader */
W HANDLE API CreateToolhelp32Snapshot(u32, u32);
W HANDLE API OpenProcess(u32, int, u32);
W void * API VirtualAllocEx(HANDLE, void *, uptr, u32, u32);
W int    API VirtualFreeEx(HANDLE, void *, uptr, u32);
W int    API WriteProcessMemory(HANDLE, void *, const void *, uptr, uptr *);
W HANDLE API CreateRemoteThread(HANDLE, void *, uptr, void *, void *, u32, u32 *);
W u32    API WaitForSingleObject(HANDLE, u32);
W int    API GetExitCodeThread(HANDLE, u32 *);
W HANDLE API GetStdHandle(u32);
W int    API WriteConsoleW(HANDLE, const wch *, u32, u32 *, void *);
W u32    API GetFileAttributesW(const wch *);
W void   API ExitProcess(u32);
W int    API lstrcmpiW(const wch *, const wch *);
W int    API lstrlenW(const wch *);

W int    API EnumWindows(int (API *)(HANDLE, i64), i64);
W u32    API GetWindowThreadProcessId(HANDLE, u32 *);
W int    API GetClassNameW(HANDLE, wch *, int);
W i64    API SetWindowLongPtrW(HANDLE, int, i64);
W i64    API CallWindowProcW(void *, HANDLE, u32, uptr, i64);
W int    API PostMessageW(HANDLE, u32, uptr, i64);
W short  API GetAsyncKeyState(int);
W HANDLE API GetForegroundWindow(void);
W int    API GetClientRect(HANDLE, RECT *);
W int    API wvsprintfA(char *, const char *, __builtin_va_list);
W int    API IsWindowVisible(HANDLE);
#endif
