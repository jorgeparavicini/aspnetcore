// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.AspNetCore.Mvc.Generators.Models;
internal record RouteTemplateProvider(string? Template, string? Order, string? Name, AttributeData Attribute)
{
    internal bool IsSilent => Template is null && Order is null && Name is null;
}
