param($installPath, $toolsPath, $package, $project)

$bindingRedirects = @(
    @{ Name = "Newtonsoft.Json"; PublicKeyToken = "30ad4fe6b2a6aeed"; OldVersionRange = "0.0.0.0-13.0.0.0"; NewVersion = "13.0.0.0" }
    @{ Name = "EntityFramework"; PublicKeyToken = "b77a5c561934e089"; OldVersionRange = "0.0.0.0-6.5.1.0"; NewVersion = "6.5.1.0" }
)

$configFile = $project.ProjectItems | Where-Object { $_.Name -eq "Web.config" }
if ($configFile -eq $null) {
    Write-Host "TemplateBuilder.Editor.Mvc5: could not locate Web.config to add binding redirects automatically."
    Write-Host "Add these manually to <runtime><assemblyBinding> if you hit a FileLoadException / assembly version mismatch:"
    $bindingRedirects | ForEach-Object { Write-Host "  $($_.Name): redirect $($_.OldVersionRange) -> $($_.NewVersion)" }
    return
}

Write-Host "TemplateBuilder.Editor.Mvc5: verify assembly binding redirects for Newtonsoft.Json and EntityFramework against your project's existing references — version conflicts on a packages.config project require explicit <bindingRedirect> entries."