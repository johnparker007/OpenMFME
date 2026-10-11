param(
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [string]$OutputPath = (Join-Path $PSScriptRoot '../src/OpenMFME.Desktop/Assets/form-inventory.json')
)
$ErrorActionPreference = 'Stop'
$bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $ExecutablePath).Path)
$text = [Text.Encoding]::GetEncoding(28591).GetString($bytes)
$forms = @()
$offset = 0
$signatures = 0
while (($offset = $text.IndexOf('TPF0', $offset, [StringComparison]::Ordinal)) -ge 0) {
    $signatures++
    $cursor = $offset + 4
    if ($cursor -lt $bytes.Length) {
        $classLength = [int]$bytes[$cursor]
        if ($classLength -gt 0 -and $classLength -lt 80 -and ($cursor + 1 + $classLength) -lt $bytes.Length) {
            $className = $text.Substring($cursor + 1, $classLength)
            $nameCursor = $cursor + 1 + $classLength
            $nameLength = [int]$bytes[$nameCursor]
            if ($nameLength -gt 0 -and $nameLength -lt 80 -and ($nameCursor + 1 + $nameLength) -le $bytes.Length) {
                $instanceName = $text.Substring($nameCursor + 1, $nameLength)
                if ($className -cmatch '^T[A-Za-z0-9_]+$' -and $instanceName -cmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
                    $forms += [ordered]@{
                        fileOffset = ('0x{0:X8}' -f $offset)
                        className = $className
                        instanceName = $instanceName
                    }
                }
            }
        }
    }
    $offset += 4
}
$inventory = [ordered]@{
    schemaVersion = 1
    sourceSha256 = (Get-FileHash -LiteralPath $ExecutablePath -Algorithm SHA256).Hash.ToLowerInvariant()
    signatureCount = $signatures
    note = 'Candidate named binary-form headers; complete form streams have not yet been parsed.'
    forms = @($forms)
}
$destination = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
[IO.File]::WriteAllText($destination, ($inventory | ConvertTo-Json -Depth 6) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Output "Recorded $($forms.Count) named form headers from $signatures signatures."
