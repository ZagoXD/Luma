param(
    [Parameter(Mandatory=$true)][string[]]$Commands,
    [string]$InstanceId = 'i-07a4a9e692e4a5c55',
    [string]$Profile = 'luma'
)
$ErrorActionPreference = 'Stop'
$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
$temporary = [IO.Path]::GetTempFileName()
try {
    @{commands=$Commands;executionTimeout=@('1800')} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $temporary -Encoding utf8NoBOM
    $sent = aws ssm send-command --profile $Profile --region us-east-2 --instance-ids $InstanceId --document-name AWS-RunShellScript --parameters "file://$temporary" --output json | ConvertFrom-Json
    if($LASTEXITCODE -ne 0){throw 'Could not send SSM command'}
    $id = $sent.Command.CommandId
    for($i=0;$i -lt 240;$i++) {
        Start-Sleep -Seconds 3
        $invocation = aws ssm get-command-invocation --profile $Profile --region us-east-2 --command-id $id --instance-id $InstanceId --output json | ConvertFrom-Json
        if($invocation.Status -in @('Success','Failed','TimedOut','Cancelled')) {
            $invocation | Select-Object Status,StandardOutputContent,StandardErrorContent | ConvertTo-Json -Depth 4
            if($invocation.Status -ne 'Success'){throw "Remote command $id failed"}
            return
        }
    }
    throw "Remote command $id did not complete within the wait period"
}
finally { Remove-Item -LiteralPath $temporary -ErrorAction SilentlyContinue }
