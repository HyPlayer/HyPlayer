$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '../..')
$output = 'artifacts/performance/reveal-options'
New-Item -ItemType Directory -Force $output | Out-Null
git rev-parse HEAD | Set-Content "$output/environment.txt"
git status --short | Add-Content "$output/environment.txt"
dotnet --info | Add-Content "$output/environment.txt"
Get-CimInstance Win32_Processor | Select-Object Name | Out-String | Add-Content "$output/environment.txt"
powercfg /getactivescheme | Add-Content "$output/environment.txt"
$renderer = Get-Content -Raw HyPlayer/LyricRenderer/Text/FocusedLyricTextRenderer.cs
$start = $renderer.IndexOf('private static T Option<T>')
$end = $renderer.IndexOf('private static string Target(', $start)
$method = $renderer.Substring($start, $end - $start)
$start = $renderer.IndexOf('private readonly record struct RevealOptions(')
$end = $renderer.IndexOf('private readonly record struct FocusedTransitionKey(', $start)
$options = $renderer.Substring($start, $end - $start)
$code = @"
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
public enum UntimedHighlightMode { DoNotHighlight, WholeLine, InferWords }
public enum HighlightRevealMode { RectangleClip, GlyphStep, WholeWord }
public enum TransliterationProgressMode { FollowMain, WholeLine }
public sealed class FocusedTextOperationDefinition { public Dictionary<string,string> Options = new(); }
public sealed class CompiledFocusedTextOperation {
 public UntimedHighlightMode UntimedHighlightMode;
 public HighlightRevealMode HighlightRevealMode;
 public TransliterationProgressMode TransliterationProgressMode;
}
public static class RevealOptionsProbe {
 $method
 $options
 static int sink;
 [MethodImpl(MethodImplOptions.NoInlining)]
 static int Frame(FocusedTextOperationDefinition d, HighlightRevealMode mode, int count, bool cached) {
   int result=0;
   for(int i=0;i<count*2;i++) result+=(int)(cached ? mode : RevealOptions.From(d).Mode);
   return result;
 }
 public static void Run() {
   var d=new FocusedTextOperationDefinition();
   d.Options.Add("untimedMode","InferWords"); d.Options.Add("revealMode","GlyphStep");
   d.Options.Add("transliterationMode","FollowMain");
   var mode=RevealOptions.From(d).Mode;
   Console.WriteLine("glyphs,cached,round,ns_per_frame");
   foreach(int count in new[]{24,80,256}) {
     for(int i=0;i<10000;i++){sink=Frame(d,mode,count,false);sink=Frame(d,mode,count,true);}
     for(int round=0;round<9;round++) foreach(bool cached in new[]{round%2==0,round%2!=0}) {
       long start=Stopwatch.GetTimestamp();
       for(int i=0;i<10000;i++) sink=Frame(d,mode,count,cached);
       double ns=(Stopwatch.GetTimestamp()-start)*1e9/Stopwatch.Frequency/10000;
       if(sink!=count*2) throw new Exception("Mode changed");
       Console.WriteLine($"{count},{cached},{round},{ns:F2}");
     }
   }
 }
}
"@
$code | Set-Content "$output/probe.cs"
Add-Type -TypeDefinition $code -CompilerOptions '/optimize+'
[RevealOptionsProbe]::Run()
