$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '../..')
$output = 'artifacts/performance/lyrics-cpu'
New-Item -ItemType Directory -Force $output | Out-Null
$source = Get-Content -Raw HyPlayer/LyricRenderer/Text/FocusedLyricTextRenderer.cs
$start = $source.IndexOf('internal static bool CanPruneLineContributions(')
$end = $source.IndexOf('private void DrawPlannedContribution(', $start)
if ($start -lt 0 -or $end -lt 0) { throw 'Pruning method not found' }
$method = $source.Substring($start, $end - $start)
$code = @"
using System;
using System.Diagnostics;
using System.Collections.Generic;
public static class FocusedTextBuiltInOperationTypes {
 public const string HighlightReveal="reveal", Color="color", Opacity="opacity", GlyphLift="lift";
}
public sealed class Operation { public object DrawScript; public Operation Definition => this; public string TypeId; }
public sealed class CompiledFocusedTextEffectProfile { public IReadOnlyList<Operation> Operations; }
public static class PruningProbe {
 $method
 static int sink;
 static int Frame(CompiledFocusedTextEffectProfile p, int glyphs, bool hoisted) {
   bool allowed = hoisted && CanPruneLineContributions(p);
   int count=0;
   for(int i=0;i<glyphs*2;i++) if(hoisted ? allowed : CanPruneLineContributions(p)) count++;
   return count;
 }
 public static void Run() {
   var p = new CompiledFocusedTextEffectProfile { Operations = new[] {
     new Operation {TypeId="reveal"}, new Operation {TypeId="color"}, new Operation {TypeId="opacity"}, new Operation {TypeId="lift"} } };
   Console.WriteLine("glyphs,hoisted,round,ns_per_frame");
   foreach(int glyphs in new[]{24,80,256}) {
     for(int i=0;i<20000;i++){sink=Frame(p,glyphs,false);sink=Frame(p,glyphs,true);}
     for(int round=0;round<9;round++) foreach(bool hoisted in new[]{round%2==0,round%2!=0}) {
       long start=Stopwatch.GetTimestamp();
       for(int i=0;i<20000;i++) sink=Frame(p,glyphs,hoisted);
       double ns=(Stopwatch.GetTimestamp()-start)*1e9/Stopwatch.Frequency/20000;
       if(sink!=glyphs*2) throw new Exception("Contribution count changed");
       Console.WriteLine($"{glyphs},{hoisted},{round},{ns:F2}");
     }
   }
 }
}
"@
$code | Set-Content "$output/pruning-probe.cs"
Add-Type -TypeDefinition $code -CompilerOptions '/optimize+'
[PruningProbe]::Run()
