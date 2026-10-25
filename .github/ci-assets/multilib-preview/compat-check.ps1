# compat-check.ps1 - does the installed add-in still find every member of KitveiHakodeshLib it calls?
#
# Reflection-ONLY: nothing is executed. Every method body of the add-in assembly is scanned for
# metadata tokens (call / callvirt / newobj / ldftn / ldfld ...) and each token is resolved against
# the Lib given in -Lib. A token that does not resolve is a member the add-in calls but the Lib does
# not have - the exact "System.MissingMethodException" a user would hit when the add-in starts.
#
# Run it TWICE (once with the currently installed Lib as the baseline, once with the new Lib) and
# compare: a token that already fails against the installed Lib is not caused by the update.
#
# Output (last line): UNRESOLVED=<n>   Exit code: 0 = ran fine, 9 = could not run (see output).
param(
    [Parameter(Mandatory)][string]$Addin,      # installed KleiKodesh.dll
    [Parameter(Mandatory)][string]$Lib,        # KitveiHakodeshLib.dll to resolve against
    [Parameter(Mandatory)][string]$InstallDir, # where the add-in's other dependencies live
    [string]$ListFile                          # optional: write the unresolved members here
)
$ErrorActionPreference = 'Stop'
try {
    # --- opcode table (operand size + kind) from the framework itself ---
    $one = @{}; $two = @{}
    foreach ($f in [Reflection.Emit.OpCodes].GetFields('Public,Static')) {
        $op = $f.GetValue($null)
        if ($op.Size -eq 1) { $one[[int]$op.Value -band 0xFF] = $op } else { $two[[int]$op.Value -band 0xFF] = $op }
    }
    $operandSize = @{
        InlineNone = 0; ShortInlineBrTarget = 1; ShortInlineI = 1; ShortInlineVar = 1
        InlineVar = 2; InlineI = 4; InlineBrTarget = 4; InlineField = 4; InlineMethod = 4; InlineSig = 4
        InlineString = 4; InlineTok = 4; InlineType = 4; ShortInlineR = 4; InlineI8 = 8; InlineR = 8
    }
    $tokenKinds = 'InlineField', 'InlineMethod', 'InlineTok', 'InlineType'

    $libPath = (Resolve-Path -LiteralPath $Lib).Path
    $libName = [Reflection.AssemblyName]::GetAssemblyName($libPath).Name   # the real assembly identity, not the file name
    $script:cache = @{}
    $handler = [ResolveEventHandler] {
        param($s, $a)
        $short = (New-Object Reflection.AssemblyName($a.Name)).Name
        if ($short -ieq $libName) { return [Reflection.Assembly]::ReflectionOnlyLoadFrom($libPath) }
        if ($script:cache.ContainsKey($short)) { return $script:cache[$short] }
        $p = Join-Path $InstallDir ($short + '.dll')
        $r = $null
        if (Test-Path -LiteralPath $p) { $r = [Reflection.Assembly]::ReflectionOnlyLoadFrom($p) }
        else { try { $r = [Reflection.Assembly]::ReflectionOnlyLoad($a.Name) } catch { $r = $null } }
        $script:cache[$short] = $r
        return $r
    }
    [AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve($handler)

    $asm = [Reflection.Assembly]::ReflectionOnlyLoadFrom((Resolve-Path -LiteralPath $Addin).Path)
    $module = $asm.ManifestModule
    try { $types = $asm.GetTypes() } catch [Reflection.ReflectionTypeLoadException] { $types = @($_.Exception.Types | Where-Object { $_ }) }

    $bf = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
    $seen = @{}; $unresolved = New-Object 'System.Collections.Generic.List[string]'
    foreach ($t in $types) {
        $methods = @(); try { $methods = @($t.GetConstructors($bf)) + @($t.GetMethods($bf)) } catch { continue }
        foreach ($m in $methods) {
            $body = $null; try { $body = $m.GetMethodBody() } catch { continue }
            if ($null -eq $body) { continue }
            $il = $body.GetILAsByteArray(); if ($null -eq $il) { continue }
            $i = 0
            while ($i -lt $il.Length) {
                $b = $il[$i]; $i++
                if ($b -eq 0xFE) { $op = $two[[int]$il[$i]]; $i++ } else { $op = $one[[int]$b] }
                if ($null -eq $op) { break }
                $kind = $op.OperandType.ToString()
                if ($op.Name -eq 'switch') { $n = [BitConverter]::ToInt32($il, $i); $i += 4 + 4 * $n; continue }
                if ($tokenKinds -contains $kind) {
                    $tok = [BitConverter]::ToInt32($il, $i)
                    if (-not $seen.ContainsKey($tok)) {
                        $seen[$tok] = $true
                        try {
                            $ga = $null; $ma = $null
                            if ($t.IsGenericType) { $ga = $t.GetGenericArguments() }
                            if ($m.IsGenericMethod) { $ma = $m.GetGenericArguments() }
                            if ($null -eq $ga -and $null -eq $ma) { [void]$module.ResolveMember($tok) } else { [void]$module.ResolveMember($tok, $ga, $ma) }
                        } catch {
                            $unresolved.Add(('0x{0:X8} in {1}::{2}  ({3})' -f $tok, $t.FullName, $m.Name, $_.Exception.GetType().Name))
                        }
                    }
                }
                $i += $operandSize[$kind]
            }
        }
    }
    if ($ListFile) { [IO.File]::WriteAllLines($ListFile, $unresolved.ToArray()) }
    "TOKENS=$($seen.Count)"
    "UNRESOLVED=$($unresolved.Count)"
    exit 0
}
catch {
    "ERROR: $($_.Exception.GetType().Name): $($_.Exception.Message)"
    exit 9
}
