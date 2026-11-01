$p = "src\Nocturne\Themes\Theme.xaml"
$lines = [System.IO.File]::ReadAllLines($p)
$lines[799] = '                        <Grid x:Name="Hover" Opacity="0">'
$lines[800] = '                            <Border Background="#73000000" IsHitTestVisible="False" />'
$lines[801] = '                            <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="True">'
# line 836 (index 835) should be </Grid>
$lines[835] = '                        </Grid>'
# remove the duplicate stackpanel line at 802 (index 801) if still there
if ($lines[801] -match 'StackPanel' -and $lines[802] -match 'StackPanel') {
    $lines = $lines[0..801] + $lines[803..($lines.Count-1)]
}
[System.IO.File]::WriteAllLines($p, $lines)
Write-Output "ok"