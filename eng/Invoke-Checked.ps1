#Requires -Version 7.0
function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$LogBase,
        [ValidateRange(1, 3600)][int]$TimeoutSeconds = 60,
        [string]$WorkingDirectory = (Get-Location).Path,
        [hashtable]$Environment = @{}
    )
    $logPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LogBase)
    $WorkingDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkingDirectory)
    foreach ($suffix in '.json', '.stdout.log', '.stderr.log') {
        if (Test-Path -LiteralPath "$logPath$suffix") {
            throw "Refusing to overwrite verification evidence: $logPath$suffix"
        }
    }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($logPath)) | Out-Null
    $info = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    foreach ($name in $Environment.Keys) { $info.Environment[$name] = $Environment[$name] }
    # Apply after caller overrides, before any SDK command (including --version).
    # Child processes inherit these guards; the parent and persistent settings are untouched.
    $sdkGuards = [ordered]@{
        DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
        DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
        DOTNET_NOLOGO = '1'
    }
    foreach ($name in $sdkGuards.Keys) {
        $info.Environment[$name] = $sdkGuards[$name]
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $timedOut = $false
    $started = $false
    try {
        [void]$process.Start()
        $started = $true
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        while (-not $process.WaitForExit(500)) {
            if ($clock.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
                $timedOut = $true
                $process.Kill($true)
                if (-not $process.WaitForExit(10000)) { throw 'Timed-out process tree did not exit.' }
                break
            }
        }
        $outText = $stdout.GetAwaiter().GetResult()
        $errText = $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText("$logPath.stdout.log", $outText)
        [IO.File]::WriteAllText("$logPath.stderr.log", $errText)
        $record = [ordered]@{
            arguments = $Arguments; workingDirectory = $WorkingDirectory
            utc = [DateTime]::UtcNow.ToString('O'); elapsedMilliseconds = $clock.ElapsedMilliseconds
            timeoutSeconds = $TimeoutSeconds; timedOut = $timedOut; exitCode = $process.ExitCode
            sdkGuards = $sdkGuards
        }
        $record | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 "$logPath.json"
        Write-Output $outText
        if ($errText) { Write-Output $errText }
        if ($timedOut -or $process.ExitCode -ne 0) { throw "dotnet failed; see $logPath (timeout=$timedOut, exit=$($process.ExitCode))." }
    }
    finally {
        if ($started -and -not $process.HasExited) { $process.Kill($true) }
        $process.Dispose()
    }
}
