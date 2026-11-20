function Assert-CleanDistributionRoot([string]$Directory) {
    foreach ($name in @('Data', 'StarPie.portable', 'portable.flag')) {
        $path = Join-Path $Directory $name
        if (Test-Path -LiteralPath $path -ErrorAction Stop) {
            throw "Portable profile content in distribution directory: $path"
        }
    }
}

function Assert-NoPortableProfileInCleanupTree([string]$Directory) {
    # -Clean removes the whole tree, including unpacked versions from earlier runs.
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push((Get-Item -LiteralPath $Directory -Force -ErrorAction Stop).FullName)
    while ($pending.Count) {
        $current = $pending.Pop()
        $attributes = [IO.File]::GetAttributes($current)
        if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked cleanup directory: $current" }
        Assert-CleanDistributionRoot $current
        foreach ($child in [IO.Directory]::EnumerateDirectories($current)) { $pending.Push($child) }
    }
}
