# A -File entry has no calling PowerShell statement. PowerShell 5.1 can misinterpret
# brackets in its initial location, so capture the process directory for that entry.
# Calls from PowerShell statements retain the session's filesystem location.
if ($__rukbat_direct_file) {
    $__rukbat_working_dir = [Environment]::CurrentDirectory
} else {
    $__rukbat_working_dir = $PWD.ProviderPath
}
