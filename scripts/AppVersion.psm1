Set-StrictMode -Version Latest

# The app version lives only in Directory.Build.props (<GameVaultVersion>); everything else reads it from there.
function Get-AppVersion([string]$Root) {
    $file = Join-Path $Root 'Directory.Build.props'
    $node = (Select-Xml -LiteralPath $file -XPath '/Project/PropertyGroup/GameVaultVersion').Node
    if ($null -eq $node -or $node.InnerText -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
        throw 'Directory.Build.props must define a numeric GameVaultVersion (major.minor.patch[.revision]).'
    }
    $node.InnerText
}

Export-ModuleMember -Function Get-AppVersion
