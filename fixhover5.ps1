$p = "src\Nocturne\Themes\Theme.xaml"
$lines = Get-Content $p
$lines[799] = '                        <Grid x:Name="Hover" Opacity="0">'
$lines[800] = '                            <Border Background="#73000000" IsHitTestVisible="False" />'
$lines[801] = '                            <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="True">'
# remove duplicate stackpanel line (index 801 originally, now shifted) - check
[System.IO.File]::WriteAllLines($p, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "step1"