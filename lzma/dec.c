#include <windows.h>

#include "../../../../c/LzmaDec.h"
#define UNUSED_VAR(x) (void)(x)

static void *myalloc(ISzAllocPtr arg, SizeT size) {
	UNUSED_VAR(arg);
	return HeapAlloc(GetProcessHeap(), 0, size);
}

static void myfree(ISzAllocPtr arg, void *p) {
	UNUSED_VAR(arg);
	HeapFree(GetProcessHeap(), 0, p);
}

__declspec(dllexport) SRes __stdcall mydec(Byte *dest, SizeT destlen,
	const Byte *src, SizeT srclen) {
	ELzmaStatus status;
	ISzAlloc alloc = {myalloc, myfree};
	if (srclen < 5)
		return SZ_ERROR_INPUT_EOF;
	srclen -= 5;
	return LzmaDecode(dest, &destlen,
			src + 5, &srclen,
			src, 5, LZMA_FINISH_END, &status, &alloc);
}
