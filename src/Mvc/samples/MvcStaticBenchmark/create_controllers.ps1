param(
    [string]$path,
    [int]$count
)

$random = New-Object System.Random

for ($i = 0; $i -lt $count; $i++) {
    $filePath = Join-Path $path "Controller$i.cs"

    $content = @"
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using Microsoft.AspNetCore.Mvc;

namespace BenchmarkApplication.Controllers;
"@

    $route = "`[Route(`"api/controller$i`")]`r`n`[ApiController]"
    $content += "$route`r`npublic class Controller$i : ControllerBase`r`n{"

    for ($j = 0; $j -lt $random.Next(1, 5); $j++) {
        $httpVerbs = @("HttpGet", "HttpPost", "HttpPut", "HttpDelete")
        $returnType = if ($random.Next(2) -eq 0) { "int" } else { "string" }
        $paramName = "param$j"
        $actionName = "Action$j"
        $httpVerb = $httpVerbs[$random.Next($httpVerbs.Length)]

        $actionContent = @"
    [$httpVerb("$actionName")]
    public ActionResult<$returnType> $actionName([FromQuery] string $paramName)
    {
"@

        switch ($returnType) {
            "int" { $actionContent += "        return " + $random.Next(1000) + ";" }
            "string" { $actionContent += '        return "' + $actionName + ' result";' }
        }

        $actionContent += "`r`n    }"
        $content += $actionContent
    }

    $content += "`r`n}"

    Set-Content -Path $filePath -Value $content
}