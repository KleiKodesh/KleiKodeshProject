# Build Instructions

## MSBuild location

MSBuild is installed with Visual Studio 18 (Community) at:

```
C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe
```

## Building the solution

Always use MSBuild directly — `dotnet build` does not work for WPF projects in this solution.

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild FtsLib.slnx /p:Configuration=Debug /v:minimal
```

To build a single project:

```powershell
& $msbuild FtsLibDemo/FtsLibDemo.csproj /p:Configuration=Debug /v:minimal
& $msbuild FtsLib/FtsLib.csproj /p:Configuration=Debug /v:minimal
```
