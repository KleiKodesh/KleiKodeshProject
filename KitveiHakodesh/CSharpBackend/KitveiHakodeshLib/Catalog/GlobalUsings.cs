// Global usings for the SHARED catalog source (Catalog\CatalogTocIndex.cs et al., linked in
// from KitveiHakodeshService — see the csproj).
//
// The service project sets <ImplicitUsings>enable</ImplicitUsings>, so those files are written
// without the usings the SDK injects for them. This project is an old-style net48 csproj with
// no such feature, so the same source arrives here missing List<>, CancellationToken, Func<>,
// and friends. Declaring them here reproduces the SDK's implicit set for this assembly instead
// of editing ~2800 lines of shared source to carry usings only one leg needs.
//
// Global usings apply to the WHOLE assembly, not just the linked files, so this list is kept
// to the minimum the shared source actually needs. Notably `System.Threading` is NOT here:
// it makes `Timer` ambiguous against System.Windows.Forms.Timer in this project's existing
// WinForms code (SplashOverlay). The shared files reference CancellationToken by its full
// namespace via this file's alias below instead.
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading.Tasks;

// Pulled in individually rather than via `global using System.Threading` — see above.
global using CancellationToken = System.Threading.CancellationToken;
global using CancellationTokenSource = System.Threading.CancellationTokenSource;
global using Interlocked = System.Threading.Interlocked;

// DbCommand.AddWithValue, shared source (KitveiHakodeshService\Common\DbParameterExtensions.cs).
// Global because the files that use it sit in two namespaces (KitveiHakodeshService.Catalog and
// .Common) and an extension method is only found through a using of the namespace declaring it.
global using KitveiHakodeshService.Common;
