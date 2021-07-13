// https://www.codeproject.com/Articles/1110153/Apex-memmove-the-fastest-memcpy-memmove-on-x-x-EVE
#include <emmintrin.h>				//	Intel/AMD SSE intrinsics

#if defined(_MSC_VER)
	#include <memory.h>
	#pragma warning( push )
	#pragma warning( disable : 4146 )	//	warning C4146: unary minus operator applied to unsigned type, result still unsigned
	#pragma warning( disable : 4244 )	//	warning C4244: '-=': conversion from '__int64' to 'std::size_t', possible loss of data
#endif

#pragma function(memcpy)
void* __cdecl memcpy( void *dst, const void *src, size_t size )
{	// based on memmove09 for "size <= 112" and memmove40 for "size > 112"
	if ( size <= 112 )
	{
		if ( size >= 16 )
		{
			const __m128i xmm0 = _mm_loadu_si128( (__m128i*)src );
			if ( size > 16 )
			{
				if ( size >= 32 )
				{
					const __m128i xmm1 = _mm_loadu_si128( (__m128i*)((char*)src + 16) );
					if ( size > 32 )
					{
						long long rax = *(long long*)((char*)src + size - 16);
						long long rcx = *(long long*)((char*)src + size - 8);
						if ( size > 48 )
						{
							const __m128i xmm2 = _mm_loadu_si128( (__m128i*)((char*)src + 32) );
							if ( size > 64 )
							{
								const __m128i xmm3 = _mm_loadu_si128( (__m128i*)((char*)src + 48) );
								if ( size > 80 )
								{
									const __m128i xmm4 = _mm_loadu_si128( (__m128i*)((char*)src + 64) );
									if ( size > 96 )
									{
										const __m128i xmm5 = _mm_loadu_si128( (__m128i*)((char*)src + 80) );
										*(long long*)((char*)dst + size - 16) = rax;
										*(long long*)((char*)dst + size - 8) = rcx;
										_mm_storeu_si128( (__m128i*)dst, xmm0 );
										_mm_storeu_si128( (__m128i*)((char*)dst + 16), xmm1 );
										_mm_storeu_si128( (__m128i*)((char*)dst + 32), xmm2 );
										_mm_storeu_si128( (__m128i*)((char*)dst + 48), xmm3 );
										_mm_storeu_si128( (__m128i*)((char*)dst + 64), xmm4 );
										_mm_storeu_si128( (__m128i*)((char*)dst + 80), xmm5 );
										return dst;
									}
									*(long long*)((char*)dst + size - 16) = rax;
									*(long long*)((char*)dst + size - 8) = rcx;
									_mm_storeu_si128( (__m128i*)dst, xmm0 );
									_mm_storeu_si128( (__m128i*)((char*)dst + 16), xmm1 );
									_mm_storeu_si128( (__m128i*)((char*)dst + 32), xmm2 );
									_mm_storeu_si128( (__m128i*)((char*)dst + 48), xmm3 );
									_mm_storeu_si128( (__m128i*)((char*)dst + 64), xmm4 );
									return dst;
								}
								*(long long*)((char*)dst + size - 16) = rax;
								*(long long*)((char*)dst + size - 8) = rcx;
								_mm_storeu_si128( (__m128i*)dst, xmm0 );
								_mm_storeu_si128( (__m128i*)((char*)dst + 16), xmm1 );
								_mm_storeu_si128( (__m128i*)((char*)dst + 32), xmm2 );
								_mm_storeu_si128( (__m128i*)((char*)dst + 48), xmm3 );
								return dst;
							}
							*(long long*)((char*)dst + size - 16) = rax;
							*(long long*)((char*)dst + size - 8) = rcx;
							_mm_storeu_si128( (__m128i*)dst, xmm0 );
							_mm_storeu_si128( (__m128i*)((char*)dst + 16), xmm1 );
							_mm_storeu_si128( (__m128i*)((char*)dst + 32), xmm2 );
							return dst;
						}
						*(long long*)((char*)dst + size - 16) = rax;
						*(long long*)((char*)dst + size - 8) = rcx;
					}
					_mm_storeu_si128( (__m128i*)dst, xmm0 );
					_mm_storeu_si128( (__m128i*)((char*)dst + 16), xmm1 );
					return dst;
				}
				long long rax = *(long long*)((char*)src + size - 16);
				long long rcx = *(long long*)((char*)src + size - 8);
				*(long long*)((char*)dst + size - 16) = rax;
				*(long long*)((char*)dst + size - 8) = rcx;
			}
			_mm_storeu_si128( (__m128i*)dst, xmm0 );
			return dst;
		}
		if ( size >= 8 )
		{
			long long rax = *(long long*)src;
			if ( size > 8 )
			{
				long long rcx = *(long long*)((char*)src + size - 8);
				*(long long*)dst = rax;
				*(long long*)((char*)dst + size - 8) = rcx;
			}
			else *(long long*)dst = rax;
		}
		else if ( size >= 4 )
		{
			int eax = *(int*)src;
			if ( size > 4 )
			{
				int ecx = *(int*)((char*)src + size - 4);
				*(int*)dst = eax;
				*(int*)((char*)dst + size - 4) = ecx;
			}
			else *(int*)dst = eax;
		}
		else if ( size >= 1 )
		{
			char al = *(char*)src;
			if ( size > 1 )
			{
				short cx = *(short*)((char*)src + size - 2);
				*(char*)dst = al;
				*(short*)((char*)dst + size - 2) = cx;
			}
			else *(char*)dst = al;
		}
		return dst;
	}
	else
	{
		void * const ret = dst;
		if ( ((size_t)dst - (size_t)src) >= size )
		{
			if ( size < (1024 * 256) )
			{
				long long offset = (long long)(size & -0x40);		// "Round down to nearest multiple of 64"
				dst = (char*)dst + offset;							// "Point to the end"
				src = (char*)src + offset;							// "Point to the end"
				size -= offset;										// "Remaining data after loop"
				offset = -offset;									// "Negative index from the end"

				do
				{
					const __m128i xmm0 = _mm_loadu_si128( (__m128i*)((char*)src + offset) );
					const __m128i xmm1 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 16) );
					const __m128i xmm2 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 32) );
					const __m128i xmm3 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 48) );
					_mm_storeu_si128( (__m128i*)((char*)dst + offset), xmm0 );
					_mm_storeu_si128( (__m128i*)((char*)dst + offset + 16), xmm1 );
					_mm_storeu_si128( (__m128i*)((char*)dst + offset + 32), xmm2 );
					_mm_storeu_si128( (__m128i*)((char*)dst + offset + 48), xmm3 );
				} while ( offset += 64 );

				if ( size >= 16 )
				{
					const __m128i xmm0 = _mm_loadu_si128( (__m128i*)src );
					if ( size > 16 )
					{
						const __m128i xmm3 = _mm_loadu_si128( (__m128i*)((char*)src + size - 16) );
						if ( size > 32 )
						{
							const __m128i xmm1 = _mm_loadu_si128( (__m128i*)((char*)src + 16) );
							if ( size > 48 )
							{
								const __m128i xmm2 = _mm_loadu_si128( (__m128i*)((char*)src + 32) );
								_mm_storeu_si128( (__m128i*)dst, xmm0 );
								_mm_storeu_si128( (__m128i*)((char*)dst + 16), xmm1 );
								_mm_storeu_si128( (__m128i*)((char*)dst + 32), xmm2 );
								_mm_storeu_si128( (__m128i*)((char*)dst + size - 16), xmm3 );
								return ret;
							}
							_mm_storeu_si128( (__m128i*)dst, xmm0 );
							_mm_storeu_si128( (__m128i*)((char*)dst + 16), xmm1 );
							_mm_storeu_si128( (__m128i*)((char*)dst + size - 16), xmm3 );
							return ret;
						}
						_mm_storeu_si128( (__m128i*)dst, xmm0 );
						_mm_storeu_si128( (__m128i*)((char*)dst + size - 16), xmm3 );
						return ret;
					}
					_mm_storeu_si128( (__m128i*)dst, xmm0 );
					return ret;
				}
			}
			else	// do forward streaming copy/move
			{
				// We MUST do prealignment on streaming copies!
				const size_t prealign = -(size_t)dst & 0xf;
				if ( prealign )
				{
					if ( prealign >= 8 )
					{
						long long rax = *(long long*)src;
						if ( prealign > 8 )
						{
							long long rcx = *(long long*)((char*)src + prealign - 8);
							*(long long*)dst = rax;
							*(long long*)((char*)dst + prealign - 8) = rcx;
						}
						else *(long long*)dst = rax;
					}
					else if ( prealign >= 4 )
					{
						int eax = *(int*)src;
						if ( prealign > 4 )
						{
							int ecx = *(int*)((char*)src + prealign - 4);
							*(int*)dst = eax;
							*(int*)((char*)dst + prealign - 4) = ecx;
						}
						else *(int*)dst = eax;
					}
					else
					{
						char al = *(char*)src;
						if ( prealign > 1 )
						{
							short cx = *(short*)((char*)src + prealign - 2);
							*(char*)dst = al;
							*(short*)((char*)dst + prealign - 2) = cx;
						}
						else *(char*)dst = al;
					}
					src = (char*)src + prealign;
					dst = (char*)dst + prealign;
					size -= prealign;
				}

				// Begin prefetching upto 4KB
				for ( long long offset = 0; offset < 4096; offset += 256 )
				{
					_mm_prefetch( ((char*)src + offset), _MM_HINT_NTA );
					_mm_prefetch( ((char*)src + offset + 64), _MM_HINT_NTA );
					_mm_prefetch( ((char*)src + offset + 128), _MM_HINT_NTA );
					_mm_prefetch( ((char*)src + offset + 192), _MM_HINT_NTA );
				}

				long long offset = (long long)(size & -0x40);		// "Round down to nearest multiple of 64"
				size -= offset;										// "Remaining data after loop"
				offset -= 4096;										// stage 1 INCLUDES prefetches
				dst = (char*)dst + offset;							// "Point to the end"
				src = (char*)src + offset;							// "Point to the end"
				offset = -offset;									// "Negative index from the end"

				do													// stage 1 ~~ WITH prefetching
				{
					_mm_prefetch( (char*)src + offset + 4096, _MM_HINT_NTA );
					const __m128i xmm0 = _mm_loadu_si128( (__m128i*)((char*)src + offset) );
					const __m128i xmm1 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 16) );
					const __m128i xmm2 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 32) );
					const __m128i xmm3 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 48) );
					_mm_stream_si128( (__m128i*)((char*)dst + offset), xmm0 );
					_mm_stream_si128( (__m128i*)((char*)dst + offset + 16), xmm1 );
					_mm_stream_si128( (__m128i*)((char*)dst + offset + 32), xmm2 );
					_mm_stream_si128( (__m128i*)((char*)dst + offset + 48), xmm3 );
				} while ( offset += 64 );

				offset = -4096;
				dst = (char*)dst + 4096;
				src = (char*)src + 4096;

				_mm_prefetch( ((char*)src + size - 64), _MM_HINT_NTA );	// prefetch the final tail section

				do													// stage 2 ~~ WITHOUT further prefetching
				{
					const __m128i xmm0 = _mm_loadu_si128( (__m128i*)((char*)src + offset) );
					const __m128i xmm1 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 16) );
					const __m128i xmm2 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 32) );
					const __m128i xmm3 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 48) );
					_mm_stream_si128( (__m128i*)((char*)dst + offset), xmm0 );
					_mm_stream_si128( (__m128i*)((char*)dst + offset + 16), xmm1 );
					_mm_stream_si128( (__m128i*)((char*)dst + offset + 32), xmm2 );
					_mm_stream_si128( (__m128i*)((char*)dst + offset + 48), xmm3 );
				} while ( offset += 64 );

				if ( size >= 16 )
				{
					const __m128i xmm0 = _mm_loadu_si128( (__m128i*)src );
					if ( size > 16 )
					{
						if ( size > 32 )
						{
							const __m128i xmm1 = _mm_loadu_si128( (__m128i*)((char*)src + 16) );
							const __m128i xmm6 = _mm_loadu_si128( (__m128i*)((char*)src + size - 32) );
							const __m128i xmm7 = _mm_loadu_si128( (__m128i*)((char*)src + size - 16) );
							_mm_stream_si128( (__m128i*)dst, xmm0 );
							_mm_stream_si128( (__m128i*)((char*)dst + 16), xmm1 );
							_mm_storeu_si128( (__m128i*)((char*)dst + size - 32), xmm6 );
							_mm_storeu_si128( (__m128i*)((char*)dst + size - 16), xmm7 );
							return ret;
						}
						const __m128i xmm7 = _mm_loadu_si128( (__m128i*)((char*)src + size - 16) );
						_mm_stream_si128( (__m128i*)dst, xmm0 );
						_mm_storeu_si128( (__m128i*)((char*)dst + size - 16), xmm7 );
						return ret;
					}
					_mm_stream_si128( (__m128i*)dst, xmm0 );
					return ret;
				}
			}

			if ( size >= 8 )
			{
				long long rax = *(long long*)src;
				if ( size > 8 )
				{
					long long rcx = *(long long*)((char*)src + size - 8);
					*(long long*)dst = rax;
					*(long long*)((char*)dst + size - 8) = rcx;
				}
				else *(long long*)dst = rax;
			}
			else if ( size >= 4 )
			{
				int eax = *(int*)src;
				if ( size > 4 )
				{
					int ecx = *(int*)((char*)src + size - 4);
					*(int*)dst = eax;
					*(int*)((char*)dst + size - 4) = ecx;
				}
				else *(int*)dst = eax;
			}
			else if ( size >= 1 )
			{
				char al = *(char*)src;
				if ( size > 1 )
				{
					short cx = *(short*)((char*)src + size - 2);
					*(char*)dst = al;
					*(short*)((char*)dst + size - 2) = cx;
				}
				else *(char*)dst = al;
			}
			return ret;
		}
		else // src < dst ... do reverse copy
		{
			src = (char*)src + size;
			dst = (char*)dst + size;

			if ( size < (1024 * 256) )
			{
				long long offset = (long long)(size & -0x40);		// "Round down to nearest multiple of 64"
				dst = (char*)dst - offset;							// "Point to the end" ... actually, we point to the start!
				src = (char*)src - offset;							// "Point to the end" ... actually, we point to the start!
				size -= offset;										// "Remaining data after loop"
				//offset = -offset;									// "Negative index from the end" ... not when doing reverse copy/move!

				offset -= 64;
				do
				{
					const __m128i xmm0 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 48) );
					const __m128i xmm1 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 32) );
					const __m128i xmm2 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 16) );
					const __m128i xmm3 = _mm_loadu_si128( (__m128i*)((char*)src + offset) );
					_mm_storeu_si128( (__m128i*)((char*)dst + offset + 48), xmm0 );
					_mm_storeu_si128( (__m128i*)((char*)dst + offset + 32), xmm1 );
					_mm_storeu_si128( (__m128i*)((char*)dst + offset + 16), xmm2 );
					_mm_storeu_si128( (__m128i*)((char*)dst + offset), xmm3 );
				} while ( (offset -= 64) >= 0 );

				if ( size >= 16 )
				{
					const __m128i xmm0 = _mm_loadu_si128( (__m128i*)((char*)src - 16) );
					if ( size > 16 )
					{
						size = -size;
						const __m128i xmm3 = _mm_loadu_si128( (__m128i*)((char*)src + size) );
						if ( size > 32 )
						{
							const __m128i xmm1 = _mm_loadu_si128( (__m128i*)((char*)src - 32) );
							if ( size > 48 )
							{
								const __m128i xmm2 = _mm_loadu_si128( (__m128i*)((char*)src - 48) );
								_mm_storeu_si128( (__m128i*)((char*)dst - 16), xmm0 );
								_mm_storeu_si128( (__m128i*)((char*)dst - 32), xmm1 );
								_mm_storeu_si128( (__m128i*)((char*)dst - 48), xmm2 );
								_mm_storeu_si128( (__m128i*)((char*)dst + size), xmm3 );
								return ret;
							}
							_mm_storeu_si128( (__m128i*)((char*)dst - 16), xmm0 );
							_mm_storeu_si128( (__m128i*)((char*)dst - 32), xmm1 );
							_mm_storeu_si128( (__m128i*)((char*)dst + size), xmm3 );
							return ret;
						}
						_mm_storeu_si128( (__m128i*)((char*)dst - 16), xmm0 );
						_mm_storeu_si128( (__m128i*)((char*)dst + size), xmm3 );
						return ret;
					}
					_mm_storeu_si128( (__m128i*)((char*)dst - 16), xmm0 );
					return ret;
				}
			}
			else	// do reversed streaming copy/move
			{
				// We MUST do prealignment on streaming copies!
				const size_t prealign = (size_t)dst & 0xf;
				if ( prealign )
				{
					src = (char*)src - prealign;
					dst = (char*)dst - prealign;
					size -= prealign;
					if ( prealign >= 8 )
					{
						long long rax = *(long long*)((char*)src + prealign - 8);
						if ( prealign > 8 )
						{
							long long rcx = *(long long*)src;
							*(long long*)((char*)dst + prealign - 8) = rax;
							*(long long*)dst = rcx;
						}
						else *(long long*)dst = rax; // different on purpose, because we know the exact size now, which is 8, and "dst" has already been aligned!
					}
					else if ( prealign >= 4 )
					{
						int eax = *(int*)((char*)src + prealign - 4);
						if ( prealign > 4 )
						{
							int ecx = *(int*)src;
							*(int*)((char*)dst + prealign - 4) = eax;
							*(int*)dst = ecx;
						}
						else *(int*)dst = eax; // different on purpose!
					}
					else
					{
						char al = *(char*)((char*)src + prealign - 1);
						if ( prealign > 1 )
						{
							short cx = *(short*)src;
							*(char*)((char*)dst + prealign - 1) = al;
							*(short*)dst = cx;
						}
						else *(char*)dst = al; // different on purpose!
					}
				}

				// Begin prefetching upto 4KB
				for ( long long offset = 0; offset > -4096; offset -= 256 )
				{
					_mm_prefetch( ((char*)src + offset - 64), _MM_HINT_NTA );
					_mm_prefetch( ((char*)src + offset - 128), _MM_HINT_NTA );
					_mm_prefetch( ((char*)src + offset - 192), _MM_HINT_NTA );
					_mm_prefetch( ((char*)src + offset - 256), _MM_HINT_NTA );
				}

				long long offset = (long long)(size & -0x40);		// "Round down to nearest multiple of 64"
				size -= offset;										// "Remaining data after loop"
				offset -= 4096;										// stage 1 INCLUDES prefetches
				dst = (char*)dst - offset;							// "Point to the end" ... actually, we point to the start!
				src = (char*)src - offset;							// "Point to the end" ... actually, we point to the start!
				//offset = -offset;									// "Negative index from the end" ... not when doing reverse copy/move!

				offset -= 64;
				do													// stage 1 ~~ WITH prefetching
				{
					_mm_prefetch( (char*)src + offset - 4096, _MM_HINT_NTA );
					const __m128i xmm0 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 48) );
					const __m128i xmm1 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 32) );
					const __m128i xmm2 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 16) );
					const __m128i xmm3 = _mm_loadu_si128( (__m128i*)((char*)src + offset) );
					_mm_stream_si128( (__m128i*)((char*)dst + offset + 48), xmm0 );
					_mm_stream_si128( (__m128i*)((char*)dst + offset + 32), xmm1 );
					_mm_stream_si128( (__m128i*)((char*)dst + offset + 16), xmm2 );
					_mm_stream_si128( (__m128i*)((char*)dst + offset), xmm3 );
				} while ( (offset -= 64) >= 0 );

				offset = 4096;
				dst = (char*)dst - 4096;
				src = (char*)src - 4096;

				_mm_prefetch( ((char*)src - 64), _MM_HINT_NTA );	// prefetch the final tail section

				offset -= 64;
				do													// stage 2 ~~ WITHOUT further prefetching
				{
					const __m128i xmm0 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 48) );
					const __m128i xmm1 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 32) );
					const __m128i xmm2 = _mm_loadu_si128( (__m128i*)((char*)src + offset + 16) );
					const __m128i xmm3 = _mm_loadu_si128( (__m128i*)((char*)src + offset) );
					_mm_stream_si128( (__m128i*)((char*)dst + offset + 48), xmm0 );
					_mm_stream_si128( (__m128i*)((char*)dst + offset + 32), xmm1 );
					_mm_stream_si128( (__m128i*)((char*)dst + offset + 16), xmm2 );
					_mm_stream_si128( (__m128i*)((char*)dst + offset), xmm3 );
				} while ( (offset -= 64) >= 0 );

				if ( size >= 16 )
				{
					const __m128i xmm0 = _mm_loadu_si128( (__m128i*)((char*)src - 16) );
					if ( size > 16 )
					{
						if ( size > 32 )
						{
							size = -size;
							const __m128i xmm1 = _mm_loadu_si128( (__m128i*)((char*)src - 32) );
							const __m128i xmm6 = _mm_loadu_si128( (__m128i*)((char*)src + size + 16) );
							const __m128i xmm7 = _mm_loadu_si128( (__m128i*)((char*)src + size) );
							_mm_stream_si128( (__m128i*)((char*)dst - 16), xmm0 );
							_mm_stream_si128( (__m128i*)((char*)dst - 32), xmm1 );
							_mm_storeu_si128( (__m128i*)((char*)dst + size + 16), xmm6 );
							_mm_storeu_si128( (__m128i*)((char*)dst + size), xmm7 );
							return ret;
						}
						size = -size;
						const __m128i xmm7 = _mm_loadu_si128( (__m128i*)((char*)src + size) );
						_mm_stream_si128( (__m128i*)((char*)dst - 16), xmm0 );
						_mm_storeu_si128( (__m128i*)((char*)dst + size), xmm7 );
						return ret;
					}
					_mm_stream_si128( (__m128i*)((char*)dst - 16), xmm0 );
					return ret;
				}
			}

			if ( size >= 8 )
			{
				long long rax = *(long long*)((char*)src - 8);
				if ( size > 8 )
				{
					size = -size; // that's right, we're converting an unsigned value to a negative, saves 2 clock cycles!
					long long rcx = *(long long*)((char*)src + size);
					*(long long*)((char*)dst - 8) = rax;
					*(long long*)((char*)dst + size) = rcx;
				}
				else *(long long*)((char*)dst - 8) = rax;
			}
			else if ( size >= 4 )
			{
				int eax = *(int*)((char*)src - 4);
				if ( size > 4 )
				{
					size = -size;
					int ecx = *(int*)((char*)src + size);
					*(int*)((char*)dst - 4) = eax;
					*(int*)((char*)dst + size) = ecx;
				}
				else *(int*)((char*)dst - 4) = eax;
			}
			else if ( size >= 1 )
			{
				char al = *((char*)src - 1);
				if ( size > 1 )
				{
					size = -size;
					short cx = *(short*)((char*)src + size);
					*((char*)dst - 1) = al;
					*(short*)((char*)dst + size) = cx;
				}
				else *((char*)dst - 1) = al;
			}
			return ret;
		}
	}
}
#if defined(_MSC_VER)
	#pragma warning( pop )
#endif
