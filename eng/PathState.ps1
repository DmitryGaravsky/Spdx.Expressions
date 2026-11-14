#Requires -Version 7.0
function Get-PathState {
    $state = [ordered]@{
        processPath = [Environment]::GetEnvironmentVariable('PATH', 'Process')
        persistentPathSupported = $IsWindows
        user = $null
        machine = $null
    }
    if ($IsWindows) {
        foreach ($scope in 'user', 'machine') {
            $hive = if ($scope -eq 'user') { [Microsoft.Win32.Registry]::CurrentUser } else { [Microsoft.Win32.Registry]::LocalMachine }
            $subkey = if ($scope -eq 'user') { 'Environment' } else { 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment' }
            $key = $hive.OpenSubKey($subkey, $false)
            try {
                $exists = $null -ne $key -and $key.GetValueNames() -contains 'Path'
                $entry = [ordered]@{ exists = $exists; kind = $null; rawValue = $null }
                if ($exists) {
                    $entry.kind = $key.GetValueKind('Path').ToString()
                    $entry.rawValue = $key.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                }
                $state[$scope] = $entry
            }
            finally {
                if ($null -ne $key) {
                    $key.Dispose()
                }
            }
        }
    }
    return $state
}

function Test-PathStateEqual {
    param([Parameter(Mandatory)]$Before, [Parameter(Mandatory)]$After)
    $beforeJson = ConvertTo-Json -InputObject $Before -Depth 5 -Compress
    $afterJson = ConvertTo-Json -InputObject $After -Depth 5 -Compress
    return [string]::Equals($beforeJson, $afterJson, [StringComparison]::Ordinal)
}
