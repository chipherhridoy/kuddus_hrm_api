<#
.SYNOPSIS
Downloads ONNX models for face detection and recognition from opencv_zoo.

face_detection_yunet_2023mar.onnx
- Size: 232,589 bytes
- SHA256: 8F2383E4DD3CFBB4553EA8718107FC0423210DC964F9F4280604804ED2552FA4
- License: MIT License

face_recognition_sface_2021dec.onnx
- Size: 38,696,353 bytes
- SHA256: 0BA9FBFA01B5270C96627C4EF784DA859931E02F04419C829E83484087C34E79
- License: BSD-2-Clause License
#>
$ErrorActionPreference = 'Stop'

$outDir = Join-Path $PSScriptRoot "..\AgenticHrmApi\models\onnx"
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

$models = @(
    @{
        Url = 'https://github.com/opencv/opencv_zoo/raw/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx'
        Name = 'face_detection_yunet_2023mar.onnx'
    },
    @{
        Url = 'https://github.com/opencv/opencv_zoo/raw/main/models/face_recognition_sface/face_recognition_sface_2021dec.onnx'
        Name = 'face_recognition_sface_2021dec.onnx'
    }
)

foreach ($model in $models) {
    $outFile = Join-Path $outDir $model.Name
    if (-not (Test-Path $outFile)) {
        Write-Host "Downloading $($model.Name)..."
        Invoke-WebRequest -Uri $model.Url -OutFile $outFile
    } else {
        Write-Host "$($model.Name) already exists."
    }
    $hash = Get-FileHash -Path $outFile -Algorithm SHA256
    Write-Host "SHA256 $($model.Name): $($hash.Hash)"
}
