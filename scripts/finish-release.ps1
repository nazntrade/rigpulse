[CmdletBinding()]
param([switch]$Preview)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$tool=if($env:AGENT_DESK_HYGIENE){$env:AGENT_DESK_HYGIENE}else{'D:\Projects\Agent-Desk\scripts\local-build-hygiene.mjs'}
$arguments=@($tool,'complete-release','--project',$root)
if(-not $Preview){$arguments+='--apply'}
& node @arguments
if($LASTEXITCODE -ne 0){throw 'Release completion failed. Review the specific publication or ownership failure before retrying.'}
