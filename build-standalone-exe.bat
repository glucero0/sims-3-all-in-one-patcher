@echo off
echo ====================================================================
echo  BUILDING SELF-CONTAINED SINGLE-FILE C# .NET 8 SIMS 3 PATCHER
echo ====================================================================
echo.
echo Restoring NuGet packages and compiling single-file executable...
echo Target: Windows 11 64-bit (win-x64)
echo.

dotnet publish Sims3ModernPatcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o ./Output/Sims3Patcher

echo.
echo ====================================================================
echo  BUILD COMPLETE!
echo  Self-contained EXE generated at: ./Output/Sims3Patcher/Sims3ModernPatcher.exe
echo  This .exe requires ZERO dependencies on end-user Windows 11 PCs!
echo ====================================================================
pause
