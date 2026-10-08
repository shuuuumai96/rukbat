'env.get' {
    $name = [string]$values[0]
    $value = [System.Environment]::GetEnvironmentVariable($name, 'Process')
    if ($null -ne $value) { return $value }
    # .NET Framework returns null for both a missing variable and an inherited
    # variable whose native Windows value is empty. The environment table keeps
    # the key, which lets Rukbat preserve that distinction when reading it.
    $environment = [System.Environment]::GetEnvironmentVariables('Process')
    if ($environment.Contains($name)) { return '' }
    return $null
}
'env.set' {
    $name = [string]$values[0]
    $value = [string]$values[1]
    if ($value.Length -eq 0) {
        throw "env.set does not accept an empty value; use env.remove to delete '$name'."
    }
    [System.Environment]::SetEnvironmentVariable($name, $value, 'Process')
    return
}
'env.remove' {
    $environmentPath = 'Env:' + [string]$values[0]
    if (Test-Path -LiteralPath $environmentPath) {
        Remove-Item -LiteralPath $environmentPath
    }
    return
}
