// The SDK's implicit using set, reproduced for this old-style net48 csproj.
//
// It was introduced for the catalog source that used to be link-compiled in from the service;
// that engine is now referenced as a library (KitveiHakodesh.Core), but this project's own
// files have been written against these usings since, so the set stays.
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

// The catalog engine is now a referenced library (KitveiHakodesh.Core), so the extension-method
// alias the link-compiled source needed is gone with it.
