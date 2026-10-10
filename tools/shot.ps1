# Dev helper: launches the built hub, waits, saves a PNG of its window.
#   powershell -File tools\shot.ps1 -Out shot.png -ExeArgs "--page=1"
param(
  [string]$Out,
  [int]$Wait = 8,
  [string]$ExeArgs = "",
  [string]$Exe = (Join-Path $PSScriptRoot "..\bin\Release\net10.0-windows\1FlowHub.exe")
)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
}
"@
Get-Process 1FlowHub -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
if ($ExeArgs) { $p = Start-Process $Exe -ArgumentList $ExeArgs -PassThru } else { $p = Start-Process $Exe -PassThru }
Start-Sleep -Seconds $Wait
$p.Refresh()
$h = $p.MainWindowHandle
if ($h -eq 0) { Write-Host "no window handle"; exit 1 }
[W]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 900
$r = New-Object W+R
[W]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Rt - $r.L; $hh = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size $w, $hh))
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "saved $w x $hh"
