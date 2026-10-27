param(
    [Parameter(Mandatory)]
    [string] $PackagePath
)

$ErrorActionPreference = 'Stop'
$package = Get-Item -LiteralPath $PackagePath
$archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $files = @($archive.Entries | ForEach-Object { $_.FullName })
    $expectedDlls = @(
        'LANCommander.Mcp.dll',
        'Microsoft.Extensions.AI.Abstractions.dll',
        'ModelContextProtocol.AspNetCore.dll',
        'ModelContextProtocol.Core.dll',
        'ModelContextProtocol.dll'
    ) | Sort-Object
    $actualDlls = @($files | Where-Object { $_ -like '*.dll' } | ForEach-Object {
        if (-not $_.StartsWith('lib/net10.0/')) {
            throw "Assembly outside plugin folder: $_"
        }
        $_.Substring('lib/net10.0/'.Length)
    }) | Sort-Object
    if (Compare-Object $expectedDlls $actualDlls) {
        throw "Unexpected or missing assemblies: $($actualDlls -join ', ')"
    }

    foreach ($required in @(
        'LANCommander.Mcp.deps.json',
        'LANCommander.Mcp.runtimeconfig.json',
        'LICENSE',
        'THIRD-PARTY-NOTICES.txt',
        'licenses/MCP.txt',
        'licenses/Microsoft.Extensions.txt'
    )) {
        if ("lib/net10.0/$required" -notin $files) {
            throw "Missing plugin file: $required"
        }
    }

    $reader = [System.IO.StreamReader]::new($archive.GetEntry('LANCommander.Mcp.nuspec').Open())
    try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $types = @($nuspec.package.metadata.packageTypes.packageType | ForEach-Object { $_.name })
    if ('LANCommander.Plugin' -notin $types -or 'LANCommander.Plugin.Server' -notin $types) {
        throw 'Package must declare both LANCommander plugin package types.'
    }
    if ($nuspec.package.metadata.dependencies) {
        throw 'Plugin package must bundle private dependencies, not request NuGet dependency installation.'
    }
    Write-Output "Verified installable plugin package: $($package.Name)"
}
finally {
    $archive.Dispose()
}
