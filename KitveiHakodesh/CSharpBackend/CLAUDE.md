# CSharpBackend

Read `KitveiHakodesh/.kiro/steering/` first (canon).

## AOT traps — the demo/VSTO hosts run the native-AOT service exe; dev runs JIT and hides these

- Every type sent over the wire (and every nested member type) needs `[MessagePackObject]`. `KitveiHakodeshService/Ipc/MsgPack.cs` ends in `StandardResolver`, whose reflection fallback makes an untagged type work in dev and fail silently in the hosts. A wire type in a new assembly also needs that assembly's `[GeneratedMessagePackResolver]` added to `MsgPack.Options`.
- Only `Lucene.Net` is rooted (`<TrimmerRootAssembly>` in `KitveiHakodeshService.csproj`). Adding another Lucene package (Analysis.Common, QueryParser, ...) needs its own root entry.
- Core, FtsLib and DocConvertLib set `IsAotCompatible` (net10.0 leg) so hazards show as build warnings — keep it on, don't NoWarn IL2xxx/IL3xxx.
- Repro a host-only bug in dev: `KHS_EXE=<AOT-published exe>` (publish outside `%TEMP%`).
