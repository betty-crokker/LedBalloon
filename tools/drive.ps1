<#
.SYNOPSIS
  Drives the running LedBalloon window without touching the mouse or the keyboard.

.DESCRIPTION
  Input goes through UI Automation, which invokes a control directly rather than moving a cursor
  onto it, and capture goes through PrintWindow with PW_RENDERFULLCONTENT, which renders the window
  even when it is behind something else. Together that means the app can be exercised while the
  machine is being used for something else: no focus stealing, no cursor jumping, no window having
  to be on top.

  What it cannot do is anything UI Automation has no pattern for -- dragging a run between outputs,
  and the color wheel inside a flyout. Those still need real input.

.EXAMPLE
  .\drive.ps1 shot
  .\drive.ps1 click "Save to controllers"
  .\drive.ps1 type "Scene name" "Christmas"
  .\drive.ps1 pick "Pick a scene to look at" "Bpm"
  .\drive.ps1 list Button
#>
param(
  [Parameter(Position = 0)][ValidateSet('shot', 'click', 'type', 'pick', 'list', 'text')]
  [string]$Action = 'shot',

  [Parameter(Position = 1)][string]$Name,
  [Parameter(Position = 2)][string]$Value,
  [string]$Out = "$env:TEMP\ledballoon.png"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

Add-Type @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;

public class Cap {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public struct RECT { public int L, T, R, B; }

  // Flag 2 is PW_RENDERFULLCONTENT. Without it a GPU-composited window, which is what Avalonia
  // draws, comes back blank.
  public static Bitmap Shoot(IntPtr h) {
    RECT r;
    GetWindowRect(h, out r);
    var bmp = new Bitmap(r.R - r.L, r.B - r.T);
    using (var g = Graphics.FromImage(bmp)) {
      IntPtr dc = g.GetHdc();
      PrintWindow(h, dc, 2);
      g.ReleaseHdc(dc);
    }
    return bmp;
  }
}
'@ -ReferencedAssemblies System.Drawing, System.Drawing.Primitives

$proc = Get-Process LedBalloon.App -ErrorAction SilentlyContinue
if (-not $proc) { Write-Output 'LedBalloon is not running.'; exit 1 }

$hwnd = $proc.MainWindowHandle
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$auto = [System.Windows.Automation.AutomationElement]
$tree = [System.Windows.Automation.TreeScope]::Descendants

function All([string]$type) {
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    $auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::$type)
  return $root.FindAll($tree, $cond)
}

# Matched on a distinctive fragment, case-insensitively: captions carry em dashes and middots that
# are a nuisance to reproduce exactly, and a fragment identifies one just as well.
function Find([string]$type, [string]$fragment) {
  $want = $fragment.ToLower()
  foreach ($e in All $type) {
    # The automation id first: a control given one is meant to be addressed by it, and a combo box
    # has no caption to match against at all.
    foreach ($label in @($e.Current.AutomationId, $e.Current.Name)) {
      if ($label -and $label.ToLower().Contains($want)) { return $e }
    }
  }
  return $null
}

switch ($Action) {
  'shot' {
    $bmp = [Cap]::Shoot($hwnd)
    $bmp.Save($Out)
    Write-Output "saved $Out ($($bmp.Width)x$($bmp.Height))"
  }

  'click' {
    $el = Find 'Button' $Name
    if (-not $el) { Write-Output "no button matching '$Name'"; exit 1 }
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Output "invoked '$($el.Current.Name)'"
  }

  'type' {
    $el = Find 'Edit' $Name
    if (-not $el) { Write-Output "no text box matching '$Name'"; exit 1 }
    $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
    Write-Output "set '$Name' to '$Value'"
  }

  'pick' {
    $box = Find 'ComboBox' $Name
    if (-not $box) { Write-Output "no combo box matching '$Name'"; exit 1 }

    # Opened first: an Avalonia combo box builds its items only once it is dropped down.
    $expand = $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 400

    $cond = New-Object System.Windows.Automation.PropertyCondition(
      $auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)

    foreach ($item in $box.FindAll($tree, $cond)) {
      if ($item.Current.Name -and $item.Current.Name.ToLower().Contains($Value.ToLower())) {
        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        $expand.Collapse()
        Write-Output "picked '$($item.Current.Name)'"
        exit 0
      }
    }

    $expand.Collapse()
    Write-Output "no entry matching '$Value' in '$Name'"
    exit 1
  }

  'text' {
    foreach ($e in All 'Text') {
      if ($e.Current.Name) { Write-Output $e.Current.Name }
    }
  }

  'list' {
    $type = if ($Name) { $Name } else { 'Button' }
    foreach ($e in All $type) {
      Write-Output ("{0}`t{1}" -f $e.Current.AutomationId, $e.Current.Name)
    }
  }
}
