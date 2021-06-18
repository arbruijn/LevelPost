#include <windows.h>

#define UNUSED_VAR(x) (void)(x)

BOOL WINAPI _DllMainCRTStartup(
    HANDLE  hDllHandle,
    DWORD   dwReason,
    LPVOID  lpreserved
    )
{
	UNUSED_VAR(hDllHandle);
	UNUSED_VAR(dwReason);
	UNUSED_VAR(lpreserved);
	return TRUE;
}
