rem Get lzma sdk from https://www.7-zip.org/sdk.html and copy these files to CPP/7zip/Bundles/LzmaCon
rem run "C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\VC\Auxiliary\Build\vcvars64.bat"
rem You'll probably need to update section/relocation offsets in LzmaDec.cs!
rem
echo Building lzmadec.dll
mkdir x64
ml64 -Dx64 -WX -c -Fox64/ ../../../../Asm/x86/LzmaDecOpt.asm
cl  -DUNICODE -D_UNICODE -Gr -nologo -c -Fox64/ -W4 -WX -EHsc -Gy -GR- -GF -MT -GS- -Zc:forScope -Zc:wchar_t -MP2 -O2 -D_LZMA_DEC_OPT ../../../../C\LzmaDec.c
cl -c -O2 memcpy.c dllmain.c dec.c
link -dll -opt:ref -opt:icf /nodefaultlib /largeaddressaware /fixed:no -out:lzmadec.dll x64\LzmaDec.obj x64\LzmaDecOpt.obj memcpy.obj dllmain.obj dec.obj kernel32.lib
echo Building lzmadec32.dll
mkdir x86
cl  -DUNICODE -D_UNICODE -Gr -nologo -c -Fox86/ -W4 -WX -EHsc -Gy -GR- -GF -MT -GS- -Zc:forScope -Zc:wchar_t -MP2 -O2  ../../../../C\LzmaDec.c
cl -DUNICODE -D_UNICODE -Gr -nologo -c -Fox86/ -W4 -WX -EHsc -Gy -GR- -GF -MT -GS- -Zc:forScope -Zc:wchar_t -MP2 -O2 memcpy.c dllmain.c dec.c
link -dll -opt:ref -opt:icf /nodefaultlib /largeaddressaware /fixed:no -out:lzmadec32.dll x86\LzmaDec.obj x86\memcpy.obj x86\dllmain.obj x86\dec.obj kernel32.lib
rem link -dll -opt:ref -opt:icf /largeaddressaware /fixed:no -out:lzmadec32.dll x86\LzmaDec.obj x86\dec.obj
