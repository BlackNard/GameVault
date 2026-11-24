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

# The preview number of the release being prepared (<GameVaultPreview>): the app reports <version>-preview.<N> and the update check compares it with release tags.
function Get-AppPreview([string]$Root) {
    $file = Join-Path $Root 'Directory.Build.props'
    $node = (Select-Xml -LiteralPath $file -XPath '/Project/PropertyGroup/GameVaultPreview').Node
    if ($null -eq $node -or $node.InnerText -notmatch '^[1-9]\d{0,5}$') {
        throw 'Directory.Build.props must define GameVaultPreview as a positive whole number.'
    }
    $node.InnerText
}

Export-ModuleMember -Function Get-AppVersion, Get-AppPreview
